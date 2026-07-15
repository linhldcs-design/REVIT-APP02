using System.Windows;
using System.Windows.Interop;
using RevitAPP.ViewModels;

namespace RevitAPP.Views;

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
