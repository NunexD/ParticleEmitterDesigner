using System.IO;
using System.Windows;
using AvalonDock;
using AvalonDock.Layout.Serialization;
using Microsoft.Win32;
using ParticleEmitterDesigner.Controls;
using ParticleEmitterDesigner.Services;
using ParticleEmitterDesigner.ViewModels;
using ParticleEmitterDesigner.ViewModels.Documents;
using ParticleEmitterDesigner.ViewModels.Tools;

namespace ParticleEmitterDesigner;

/// <summary>
/// Shell window. Hosts the AvalonDock workspace (document tabs + tool docks),
/// owns the workspace-layout persistence and the file / export dialogs, and
/// delegates all simulation and rendering to the per-document views.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string LayoutDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ParticleEmitterDesigner");

    private static readonly string LayoutPath = Path.Combine(LayoutDirectory, "layout.xml");

    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;

        // Wire the view-model's layout commands back to the DockingManager.
        _viewModel.OnSaveLayout = _ => SaveLayout();
        _viewModel.OnRestoreLayout = _ => RestoreLayout();
        _viewModel.OnResetLayout = ResetLayout;

        Loaded += (_, _) => RestoreLayout();
        Closing += (_, _) => SaveLayout();
    }

    // ===================== Workspace layout persistence =====================

    private void SaveLayout()
    {
        try
        {
            Directory.CreateDirectory(LayoutDirectory);
            var serializer = new XmlLayoutSerializer(Dock);
            using var writer = new StreamWriter(LayoutPath);
            serializer.Serialize(writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persisting the layout is best-effort; never block application close.
        }
    }

    private void RestoreLayout()
    {
        if (!File.Exists(LayoutPath))
        {
            return;
        }

        try
        {
            var serializer = new XmlLayoutSerializer(Dock);

            // Reconnect deserialized layout items to the live view models by
            // their stable ContentId; drop anything that no longer exists.
            serializer.LayoutSerializationCallback += (_, args) =>
            {
                string? id = args.Model.ContentId;

                ToolViewModel? tool = _viewModel.Tools.FirstOrDefault(t => t.ContentId == id);
                if (tool is not null)
                {
                    args.Content = tool;
                    return;
                }

                EmitterDocumentViewModel? document =
                    _viewModel.Documents.FirstOrDefault(d => d.ContentId == id);
                if (document is not null)
                {
                    args.Content = document;
                    return;
                }

                args.Cancel = true;
            };

            using var reader = new StreamReader(LayoutPath);
            serializer.Deserialize(reader);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // A corrupt layout file should not prevent the app from starting.
        }
    }

    private void ResetLayout()
    {
        try
        {
            if (File.Exists(LayoutPath))
            {
                File.Delete(LayoutPath);
            }
        }
        catch (IOException)
        {
            // Ignore; the in-memory reset below still applies.
        }

        // Force AvalonDock to rebuild the default layout from the view models.
        Dock.Layout = null;
        Dock.Layout = new AvalonDock.Layout.LayoutRoot
        {
            RootPanel = new AvalonDock.Layout.LayoutPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Children =
                {
                    new AvalonDock.Layout.LayoutAnchorablePane { Name = "LeftPane", DockMinWidth = 300 },
                    new AvalonDock.Layout.LayoutPanel
                    {
                        Orientation = System.Windows.Controls.Orientation.Vertical,
                        Children =
                        {
                            new AvalonDock.Layout.LayoutDocumentPane(),
                            new AvalonDock.Layout.LayoutAnchorablePane { Name = "BottomPane", DockMinHeight = 200 }
                        }
                    },
                    new AvalonDock.Layout.LayoutAnchorablePane { Name = "RightPane", DockMinWidth = 300 }
                }
            }
        };
    }

    // ===================== Document lifecycle =====================

    private void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        if (e.Document.Content is EmitterDocumentViewModel document)
        {
            // Let the view model be the single source of truth: cancel the UI
            // close and remove it from the collection, which removes the tab.
            e.Cancel = true;
            _viewModel.CloseDocument(document);
        }
    }

    private EmitterDocumentViewModel? ActiveDocument => _viewModel.ActiveDocument;

    // ===================== File menu =====================

    private void OnNewDocumentClick(object sender, RoutedEventArgs e) => _viewModel.NewDocument();

    private void OnOpenDocumentClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open emitter",
            Filter = "Emitter files (*.json;*.vfx)|*.json;*.vfx|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true && _viewModel.OpenDocument(dialog.FileName) is null)
        {
            MessageBox.Show(this, "Could not open the emitter file.", "Open Emitter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveDocumentClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        if (document.FilePath is { Length: > 0 } path && document.TrySave(path))
        {
            return;
        }

        SaveDocumentAs(document);
    }

    private void OnSaveDocumentAsClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is { } document)
        {
            SaveDocumentAs(document);
        }
    }

    private void SaveDocumentAs(EmitterDocumentViewModel document)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save emitter",
            Filter = "Emitter preset (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = document.Title.EndsWith(".vfx") ? document.Title : $"{document.Title}.json",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) == true && !document.TrySave(dialog.FileName))
        {
            MessageBox.Show(this, "Could not save the emitter.", "Save Emitter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    // ===================== Sprite =====================

    private void OnLoadSpriteClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Load sprite sheet",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|PNG (*.png)|*.png|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            SpriteSheet? sheet = SpriteSheet.LoadFromFile(dialog.FileName);
            if (sheet is not null)
            {
                document.Sprite = sheet;
                document.SpriteFileName = Path.GetFileName(dialog.FileName);
            }
            else
            {
                MessageBox.Show(this, "Could not load the sprite sheet.", "Load Sprite",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnResetSpriteClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        document.Sprite = SpriteSheet.CreateProcedural();
        document.SpriteFileName = "Procedural (4x4)";
    }

    /// <summary>Ensures the Godot asset library folder exists and returns it.</summary>
    private string EnsureAssetFolder()
    {
        string folder = _viewModel.PresetBrowser.AssetFolder;
        Directory.CreateDirectory(folder);
        return folder;
    }

    // ===================== Export =====================

    private void OnExportGodotClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Godot 4 particle material",
            Filter = "Godot resource (*.tres)|*.tres|All files (*.*)|*.*",
            DefaultExt = ".tres",
            FileName = "particle_process_material.tres",
            AddExtension = true,
            InitialDirectory = EnsureAssetFolder()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            GodotResourceExporter.Export(document.Settings, dialog.FileName);
            _viewModel.PresetBrowser.RefreshCommand.Execute(null);
            MessageBox.Show(this, $"Exported Godot 4 material to:\n{dialog.FileName}",
                "Export to Godot 4", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not export the Godot material.\n{ex.Message}",
                "Export to Godot 4", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportSpriteSheetClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        // Default to the 8x8 / 2048px power-of-two flipbook requested for Godot.
        var prompt = new ExportSpriteSheetDialog { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        int grid = prompt.GridSize;
        double duration = prompt.Duration;

        var saveDialog = new SaveFileDialog
        {
            Title = "Export sprite sheet",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"flipbook-{grid}x{grid}-{duration:0.##}s.png",
            InitialDirectory = EnsureAssetFolder()
        };

        if (saveDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            FlipbookBaker.Bake(document.Settings, document.Sprite, grid, duration, saveDialog.FileName);
            _viewModel.PresetBrowser.RefreshCommand.Execute(null);
            MessageBox.Show(this,
                $"Baked {grid * grid} frames ({grid}x{grid}, 2048px, transparent) to:\n{saveDialog.FileName}",
                "Export Sprite Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not export the sprite sheet.\n{ex.Message}",
                "Export Sprite Sheet", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
