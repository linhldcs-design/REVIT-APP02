using BeamRebarPro.ViewModels;
using System.Windows;

namespace BeamRebarPro.Views;

public sealed partial class BeamRebarProView
{
    public BeamRebarProView(BeamRebarProViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.RequestClose += Close;
        Closed += (_, _) => viewModel.RequestClose -= Close;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
