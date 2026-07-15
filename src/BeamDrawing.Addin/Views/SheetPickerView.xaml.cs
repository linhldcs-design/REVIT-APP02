using System.Windows;
using System.Windows.Controls;
using BeamDrawing.Addin.ViewModels;

namespace BeamDrawing.Addin.Views;

public partial class SheetPickerView : Window
{
    private readonly IReadOnlyList<SheetOption> _all;

    public SheetOption? SelectedSheet { get; private set; }

    public SheetPickerView(IReadOnlyList<SheetOption> sheets)
    {
        InitializeComponent();
        _all = sheets;
        SheetList.ItemsSource = _all;
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = FilterBox.Text.Trim();
        SheetList.ItemsSource = string.IsNullOrEmpty(text)
            ? _all
            : _all.Where(s => s.Display.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();
    private void SheetList_MouseDoubleClick(object sender, RoutedEventArgs e) => Confirm();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void Confirm()
    {
        if (SheetList.SelectedItem is SheetOption sheet)
        {
            SelectedSheet = sheet;
            DialogResult = true;
            Close();
        }
    }
}
