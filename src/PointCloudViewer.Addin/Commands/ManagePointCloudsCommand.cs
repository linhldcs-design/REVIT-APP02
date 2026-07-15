using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using PointCloudViewer.Addin.Services;

namespace PointCloudViewer.Addin.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class ManagePointCloudsCommand : ExternalCommand
{
    public override void Execute()
    {
        var pointClouds = Host.GetService<RevitPointCloudViewService>();
        var uiDocument = Application.ActiveUIDocument;
        TaskDialog.Show("Point Cloud", pointClouds.BuildSummary(uiDocument.Document, uiDocument.ActiveView));
    }
}
