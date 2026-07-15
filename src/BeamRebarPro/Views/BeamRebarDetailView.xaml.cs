using System.Windows;
using BeamRebarPro.ViewModels;

namespace BeamRebarPro.Views;

public sealed partial class BeamRebarDetailView
{
    public BeamRebarDetailView(BeamRebarDetailViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void ToggleSection_Click(object sender, RoutedEventArgs e)
    {
        // Bước sau: chuyển giữa hình mặt cắt và mặt đứng.
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BeamRebarDetailViewModel vm)
        {
            vm.ApplyRebar();
            if (vm.ApplyRequested) Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
