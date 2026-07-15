using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using PointCloudViewer.Addin.Services;

namespace PointCloudViewer.Addin.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class UpdateRenderCommand : ExternalCommand
{
    public override void Execute()
    {
        try
        {
            var renderer = Host.GetService<PointCloudDirectContext3DService>();
            var count = renderer.RebuildFromActiveView(Application.ActiveUIDocument, hideNativePointClouds: true);
            TaskDialog.Show("Point Cloud", $"DirectContext3D renderer rebuilt with {count} sampled point(s).");
        }
        catch (Exception exception)
        {
            TaskDialog.Show("Point Cloud", $"DirectContext3D update failed:\n{exception.Message}");
        }
    }
}
