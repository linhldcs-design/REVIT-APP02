using System.Windows;
using RevitAPP.ViewModels;

namespace RevitAPP.Views;

public partial class BeamDrawingWindow : Window
{
    public BeamDrawingWindow(BeamDrawingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };
    }
}
