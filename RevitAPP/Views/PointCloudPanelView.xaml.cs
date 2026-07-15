using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using RevitAPP.ViewModels;
using Color = System.Windows.Media.Color;

namespace RevitAPP.Views;

/// <summary>
///     Dockable panel điều khiển hiển thị Point Cloud. Modeless — neo vào cạnh cửa sổ Revit.
/// </summary>
public partial class PointCloudPanelView : UserControl, IDockablePaneProvider
{
    /// <summary>Preset màu cho chế độ "Màu cố định" (KISS — không cần thư viện color picker).</summary>
    private static readonly Color[] Swatches =
    {
        Colors.OrangeRed, Colors.Gold, Colors.LimeGreen, Colors.DeepSkyBlue,
        Colors.MediumPurple, Colors.White, Colors.Gray, Colors.Black
    };

    public PointCloudPanelView()
    {
        InitializeComponent();
        BuildSwatches();
    }

    private PointCloudPanelViewModel? ViewModel => DataContext as PointCloudPanelViewModel;

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = this;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right
        };
    }

    private void BuildSwatches()
    {
        foreach (var color in Swatches)
        {
            var border = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("Brush.Border"),
                Background = new SolidColorBrush(color),
                Cursor = Cursors.Hand,
                ToolTip = color.ToString()
            };
            var captured = color;
            border.MouseLeftButtonUp += (_, _) =>
            {
                if (ViewModel != null) ViewModel.FixedColor = captured;
            };
            SwatchHost.Items.Add(border);
        }
    }
}
