using System.Windows;
using RevitAPP.ViewModels;

namespace RevitAPP.Views;

public partial class FootingSectionWindow : Window
{
    public FootingSectionWindow(FootingSectionViewModel viewModel)
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
