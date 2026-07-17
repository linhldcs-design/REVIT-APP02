using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Services.PointCloud;

namespace RevitAPP.Commands;

/// <summary>Hiện/ẩn dockable panel điều khiển hiển thị Point Cloud.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class TogglePointCloudPanelCommand : ExternalCommand
{
    public override void Execute()
    {
        if (!LicenseCommandGate.Ensure("Point Cloud")) return;
        var pane = UiApplication.GetDockablePane(PointCloudPanelRegistry.PaneId);
        if (pane.IsShown())
            pane.Hide();
        else
            pane.Show();
    }
}
