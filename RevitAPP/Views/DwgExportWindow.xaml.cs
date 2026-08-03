using System.Windows;
using System.Windows.Interop;
using RevitAPP.ViewModels;

namespace RevitAPP.Views;

public partial class DwgExportWindow : Window
{
    public DwgExportWindow(IntPtr ownerHandle, DwgExportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (ownerHandle != IntPtr.Zero) new WindowInteropHelper(this).Owner = ownerHandle;
        viewModel.CloseRequested += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };
    }
}
