using System.Windows;
using RevitAPP.ViewModels;

namespace RevitAPP.Views;

public partial class LicenseView : Window
{
    public LicenseView(LicenseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
