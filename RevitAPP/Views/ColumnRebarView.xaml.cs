using System.Windows;
using RevitAPP.ViewModels;

namespace RevitAPP.Views;

public partial class ColumnRebarView : Window
{
    public ColumnRebarView(ColumnRebarViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PreviewFitRequested += (_, _) =>
        {
            ColumnReview2D.Fit();
            ColumnReview3D.Fit();
        };
        viewModel.CloseRequested += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };
    }
}
