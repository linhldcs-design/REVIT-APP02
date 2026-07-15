using PointCloudViewer.Addin.ViewModels;

namespace PointCloudViewer.Addin.Views;

public sealed partial class PointCloudViewer_AddinView
{
    public PointCloudViewer_AddinView(PointCloudViewer_AddinViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}