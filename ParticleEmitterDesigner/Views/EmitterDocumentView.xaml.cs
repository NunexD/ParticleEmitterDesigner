using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ParticleEmitterDesigner.ViewModels.Documents;

namespace ParticleEmitterDesigner.Views;

/// <summary>
/// Hosts the particle simulation canvas for a single document and drives its
/// own per-frame update/render loop. Each open document therefore simulates
/// independently, and only visible documents consume CPU time.
/// </summary>
public partial class EmitterDocumentView : UserControl
{
    private readonly Stopwatch _clock = new();
    private EmitterDocumentViewModel? _document;
    private TimeSpan _lastFrame;

    public EmitterDocumentView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_document is not null)
        {
            _document.PropertyChanged -= OnDocumentPropertyChanged;
            _document.RequestSelectTexture = null;
        }

        _document = DataContext as EmitterDocumentViewModel;
        if (_document is not null)
        {
            Viewport.Emitter = _document.Emitter;
            Viewport.Sprite = _document.Sprite;
            _document.RequestResetView = ResetViewport;
            _document.RequestSelectTexture = SelectTexture;
            _document.PropertyChanged += OnDocumentPropertyChanged;
        }
    }

    private void ResetViewport() => Viewport.ResetView();

    private void SelectTexture()
    {
        if (_document is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Load particle texture",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|PNG (*.png)|*.png|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        if (!_document.ApplyTexture(dialog.FileName))
        {
            MessageBox.Show(Window.GetWindow(this), "Could not load the texture image.",
                "Load Texture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmitterDocumentViewModel.Sprite) && _document is not null)
        {
            Viewport.Sprite = _document.Sprite;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _clock.Start();
        _lastFrame = _clock.Elapsed;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _clock.Stop();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Only simulate/render documents that are actually on screen (the
        // active tab), so background tabs cost nothing.
        if (_document is null || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        TimeSpan now = _clock.Elapsed;
        double dt = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        if (dt <= 0)
        {
            return;
        }

        if (dt > 0.1)
        {
            dt = 0.1;
        }

        _document.Tick(dt);
        Viewport.Render();
        _document.Fps = _document.Fps <= 0 ? 1.0 / dt : _document.Fps * 0.9 + (1.0 / dt) * 0.1;
    }
}
