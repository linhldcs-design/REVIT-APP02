using System.Windows;
using FootingDrawing.Addin.ViewModels;

namespace FootingDrawing.Addin.Views;

/// <summary>Dialog chọn 1 sheet đích từ danh sách sheet có sẵn. Đơn giản, không MVVM.</summary>
public partial class SheetPickerView : Window
{
    public SheetPickerView(IReadOnlyList<SheetOption> sheets)
    {
        InitializeComponent();
        SheetList.ItemsSource = sheets;
    }

    public SheetOption? SelectedSheet => SheetList.SelectedItem as SheetOption;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (SelectedSheet == null) return;
        DialogResult = true;
        Close();
    }

    private void OnDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedSheet == null) return;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
