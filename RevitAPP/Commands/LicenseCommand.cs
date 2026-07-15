using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.ViewModels;
using RevitAPP.Views;

namespace RevitAPP.Commands;

/// <summary>
///     Mo dialog License: dang nhap Google de kich hoat cac cong cu ve thep.
///     Khong sua document (chi mo UI) nhung van khai bao Manual theo chuan add-in.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class LicenseCommand : ExternalCommand
{
    public override void Execute()
    {
        var viewModel = new LicenseViewModel();
        var view = new LicenseView(viewModel);
        new WindowInteropHelper(view) { Owner = Application.MainWindowHandle };
        view.ShowDialog();
    }
}
