using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using PointCloudViewer.Addin.Views;

namespace PointCloudViewer.Addin.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ShowSettingsCommand : ExternalCommand
{
    public override void Execute()
    {
        var view = Host.GetService<PointCloudViewer_AddinView>();
        view.Show();
    }
}
