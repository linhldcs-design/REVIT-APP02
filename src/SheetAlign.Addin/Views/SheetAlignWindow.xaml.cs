using System.Windows;
using System.Windows.Interop;
using SheetAlign.Addin.ViewModels;

namespace SheetAlign.Addin.Views;

public partial class SheetAlignWindow : Window
{
    public SheetAlignWindow(IntPtr ownerHandle, SheetAlignViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        if (ownerHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(this).Owner = ownerHandle;
        }

        viewModel.CloseRequested += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };
    }
}
