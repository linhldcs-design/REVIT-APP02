using System.Windows;
using BeamRebar.Addin.ViewModels;

namespace BeamRebar.Addin.Views;

public partial class QuickSettingView : Window
{
    public QuickSettingView(QuickSettingViewModel viewModel)
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
