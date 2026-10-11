using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace ParticleEmitterDesigner.Services;

/// <summary>
/// Self-update support backed by GitHub Releases.
///
/// <para>
/// The service queries the latest release of <c>NunexD/ParticleEmitterDesigner</c>,
/// compares the release tag against the running assembly version and, when a
/// newer build is available, downloads the attached <c>.zip</c> asset, stages it
/// in the temp folder and hands over to a detached PowerShell process that waits
/// for this process to exit, copies the files over the install directory and
/// relaunches the app.
/// </para>
/// </summary>
public sealed class UpdateService
{
    private const string Owner = "NunexD";
    private const string Repo = "ParticleEmitterDesigner";
    private const string UserAgent = "ParticleEmitterDesigner-App";
    private const string ExecutableName = "ParticleEmitterDesigner.exe";

    private const string ApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The version of the running assembly.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    /// <summary>
    /// Checks GitHub for a newer release and, if one exists, offers to download
    /// and apply it. When <paramref name="silentIfLatest"/> is true the user is
    /// not notified when the app is already up to date.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool silentIfLatest = false)
    {
        Version current = CurrentVersion;

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(ApiUrl);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Notify(silentIfLatest,
                    "No published releases were found for this application.",
                    MessageBoxImage.Information);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                Notify(silentIfLatest,
                    $"Could not check for updates ({(int)response.StatusCode} {response.ReasonPhrase}).",
                    MessageBoxImage.Warning);
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            if (!TryReadRelease(json, out string? tagName, out string? zipUrl))
            {
                Notify(silentIfLatest,
                    "The latest release could not be interpreted.",
                    MessageBoxImage.Warning);
                return;
            }

            if (!TryParseVersion(tagName, out Version latest))
            {
                Notify(silentIfLatest,
                    $"The latest release tag ('{tagName}') is not a valid version.",
                    MessageBoxImage.Warning);
                return;
            }

            if (latest <= current)
            {
                Notify(silentIfLatest,
                    $"You are already using the latest version ({current}).",
                    MessageBoxImage.Information);
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                $"Version {latest} is available! Would you like to update and restart now?",
                "Check for Updates", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (choice != MessageBoxResult.Yes)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(zipUrl))
            {
                MessageBox.Show(
                    "The latest release does not include a .zip download.",
                    "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await DownloadAndStageAsync(zipUrl);
            LaunchUpdaterAndExit();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Notify(silentIfLatest,
                $"Could not check for updates.\n{ex.Message}",
                MessageBoxImage.Error);
        }
    }

    // ===================== Download / staging =====================

    private static async Task DownloadAndStageAsync(string zipUrl)
    {
        string root = Path.Combine(Path.GetTempPath(), "ParticleEmitterDesigner_Update");
        string zipPath = Path.Combine(root, "update.zip");
        string extractPath = Path.Combine(root, "extracted");

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(extractPath);

        using (HttpResponseMessage response = await Http.GetAsync(
                   zipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using Stream source = await response.Content.ReadAsStreamAsync();
            await using FileStream target = File.Create(zipPath);
            await source.CopyToAsync(target);
        }

        ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
    }

    /// <summary>
    /// Finds the directory that actually holds the executable after extraction:
    /// release zips are sometimes packaged with a single wrapping folder.
    /// </summary>
    private static string ResolveStagingDirectory(string extractPath)
    {
        if (File.Exists(Path.Combine(extractPath, ExecutableName)))
        {
            return extractPath;
        }

        foreach (string directory in Directory.EnumerateDirectories(extractPath))
        {
            if (File.Exists(Path.Combine(directory, ExecutableName)))
            {
                return directory;
            }
        }

        return extractPath;
    }

    // ===================== Handoff to PowerShell =====================

    private static void LaunchUpdaterAndExit()
    {
        string root = Path.Combine(Path.GetTempPath(), "ParticleEmitterDesigner_Update");
        string extractPath = Path.Combine(root, "extracted");
        string staging = ResolveStagingDirectory(extractPath);
        string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string exePath = Path.Combine(appDir, ExecutableName);
        string scriptPath = Path.Combine(root, "apply-update.ps1");
        int pid = Environment.ProcessId;

        string script =
            "$ErrorActionPreference = 'SilentlyContinue'\r\n" +
            $"Wait-Process -Id {pid}\r\n" +
            "Start-Sleep -Milliseconds 800\r\n" +
            $"Copy-Item -Path {Quote(staging)}\\* -Destination {Quote(appDir)} -Recurse -Force\r\n" +
            $"Start-Process -FilePath {Quote(exePath)}\r\n" +
            $"Remove-Item -LiteralPath {Quote(root)} -Recurse -Force\r\n";

        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)}",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(psi);
        Application.Current?.Shutdown();
    }

    // ===================== Helpers =====================

    private static void Notify(bool silent, string message, MessageBoxImage icon)
    {
        if (silent)
        {
            return;
        }

        MessageBox.Show(message, "Check for Updates", MessageBoxButton.OK, icon);
    }

    private static bool TryReadRelease(string json, out string? tagName, out string? zipUrl)
    {
        tagName = null;
        zipUrl = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("tag_name", out JsonElement tag))
            {
                tagName = tag.GetString();
            }

            if (root.TryGetProperty("assets", out JsonElement assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                    if (name is not null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipUrl = asset.TryGetProperty("browser_download_url", out JsonElement url)
                            ? url.GetString()
                            : null;
                        break;
                    }
                }
            }

            return !string.IsNullOrWhiteSpace(tagName);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        if (!Version.TryParse(trimmed, out Version? parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>Single-quotes a PowerShell literal (doubling embedded quotes).</summary>
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
