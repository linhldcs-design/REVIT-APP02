using System.Windows;
using RevitAPP.ViewModels;

namespace RevitAPP.Views;

public partial class BeamLongitudinalDrawingWindow : Window
{
    public BeamLongitudinalDrawingWindow(BeamLongitudinalDrawingViewModel viewModel)
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
