using System.Windows;
using RevitAPP.ViewModels;

namespace RevitAPP.Views;

public partial class ColumnRebarView : Window
{
    public ColumnRebarView(ColumnRebarViewModel viewModel)
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
