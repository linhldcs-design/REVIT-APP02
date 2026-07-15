using System.Windows;
using BeamDrawing.Addin.ViewModels;

namespace BeamDrawing.Addin.Views;

public partial class BeamDrawingView : Window
{
    private readonly BeamDrawingViewModel _viewModel;

    public BeamDrawingView(BeamDrawingViewModel viewModel)
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
