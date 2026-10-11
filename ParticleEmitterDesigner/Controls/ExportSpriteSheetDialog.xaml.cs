using System.Windows;
using System.Windows.Controls;

namespace ParticleEmitterDesigner.Controls;

/// <summary>
/// Modal prompt that captures the flipbook bake settings: the grid size of the
/// sprite sheet and the total play length of the animation in seconds.
/// </summary>
public partial class ExportSpriteSheetDialog : Window
{
    public ExportSpriteSheetDialog()
    {
        InitializeComponent();
    }

    /// <summary>Number of cells per side (e.g. 4 or 8).</summary>
    public int GridSize
    {
        get
        {
            var item = GridCombo.SelectedItem as ComboBoxItem;
            return item is not null && int.TryParse(item.Tag?.ToString(), out int tag) ? tag : 8;
        }
    }

    /// <summary>Total animation duration in seconds.</summary>
    public double Duration => double.TryParse(DurationBox.Text?.Trim(), out double d)
        ? Math.Max(0.01, d)
        : 1.0;

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(DurationBox.Text?.Trim(), out double duration) || duration <= 0)
        {
            MessageBox.Show(this, "Please enter a valid duration in seconds.",
                "Export Sprite Sheet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}