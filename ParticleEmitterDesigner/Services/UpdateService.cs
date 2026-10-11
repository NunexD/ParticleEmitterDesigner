using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
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
        string logPath = Path.Combine(root, "updater.log");
        int pid = Environment.ProcessId;

        // Built as a single line so it survives command-line round-tripping and
        // can be passed straight to `powershell -Command` (no .ps1 file, so the
        // default execution policy can't block it). The detached script:
        //   1. waits until this process (the source PID) is genuinely gone,
        //   2. retries the overwrite up to 5 times if a file is locked,
        //   3. relaunches the freshly copied executable,
        //   4. removes the staging folder only after a successful launch.
        // Any failure is appended to updater.log for troubleshooting.
        string script =
            "$ErrorActionPreference='Stop'; " +
            $"$targetPid={pid}; " +
            $"$src={PsString(staging)}; " +
            $"$dst={PsString(appDir)}; " +
            $"$exe={PsString(exePath)}; " +
            $"$log={PsString(logPath)}; " +
            "try { " +
            "while (Get-Process -Id $targetPid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 250 }; " +
            "Start-Sleep -Milliseconds 600; " +
            "$copied=$false; " +
            "for ($i=0; $i -lt 5; $i++) { " +
            "try { Copy-Item -Path \"$src\\*\" -Destination \"$dst\" -Recurse -Force -ErrorAction Stop; $copied=$true; break } " +
            "catch { Start-Sleep -Milliseconds 500 } " +
            "}; " +
            "if (-not $copied) { throw 'Failed to copy update files after 5 attempts.' }; " +
            "Start-Process -FilePath $exe -WorkingDirectory $dst; " +
            "Start-Sleep -Milliseconds 500; " +
            $"Remove-Item -LiteralPath {PsString(root)} -Recurse -Force -ErrorAction SilentlyContinue " +
            "} catch { " +
            "try { \"$(Get-Date -Format o) $($_.Exception.Message)\" | Add-Content -LiteralPath $log } catch {} " +
            "}";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        // ArgumentList quotes the script argument (and its embedded double quotes)
        // for the OS command line, so paths with spaces survive intact.
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-WindowStyle");
        psi.ArgumentList.Add("Hidden");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not start the updater.\n{ex.Message}",
                "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

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

    /// <summary>
    /// Wraps a value in a PowerShell double-quoted string literal (escaping any
    /// backticks and double quotes) so paths containing spaces stay a single
    /// token inside the updater script.
    /// </summary>
    private static string PsString(string value) =>
        "\"" + value.Replace("`", "``").Replace("\"", "`\"") + "\"";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
