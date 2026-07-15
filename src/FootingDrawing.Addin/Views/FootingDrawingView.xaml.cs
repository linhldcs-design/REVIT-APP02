using System.Windows;
using FootingDrawing.Addin.ViewModels;

namespace FootingDrawing.Addin.Views;

/// <summary>Dialog cấu hình bản vẽ móng. Code-behind tối thiểu: DataContext + đóng dialog + pick sheet.</summary>
public partial class FootingDrawingView : Window
{
    private readonly FootingDrawingViewModel _viewModel;

    public FootingDrawingView(FootingDrawingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.CloseRequested += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };

        viewModel.PickSheetRequested += OnPickSheetRequested;
    }

    private void OnPickSheetRequested(object? sender, EventArgs e)
    {
        var picker = new SheetPickerView(_viewModel.Resources.Sheets) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedSheet != null)
            _viewModel.ApplyPickedSheet(picker.SelectedSheet);
    }
}
