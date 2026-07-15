using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using PointCloudViewer.Addin.Services;
using PointCloudViewer.Core.Models;

namespace PointCloudViewer.Addin.Commands;

public abstract class VisualizationModeCommand(VisualizationMode mode, string label) : ExternalCommand
{
    public override void Execute()
    {
        try
        {
            var store = Host.GetService<PointCloudSettingsStore>();
            var renderer = Host.GetService<PointCloudDirectContext3DService>();
            var uiDocument = Application.ActiveUIDocument;

            store.Update(settings => settings.Mode = mode);
            var count = renderer.RebuildFromActiveView(uiDocument, hideNativePointClouds: true);

            var message = count == 0
                ? "No point cloud points were sampled. Check that a point cloud instance exists in this project."
                : $"{label} DirectContext3D mode rebuilt with {count} sampled point(s). Native point cloud display is hidden in this view.";

            TaskDialog.Show("Point Cloud", message);
        }
        catch (Exception exception)
        {
            TaskDialog.Show("Point Cloud", $"DirectContext3D command failed:\n{exception.Message}");
        }
    }
}

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class RgbModeCommand() : VisualizationModeCommand(VisualizationMode.Rgb, "RGB");

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class NormalModeCommand() : VisualizationModeCommand(VisualizationMode.Normal, "Normal");

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class XRayModeCommand() : VisualizationModeCommand(VisualizationMode.XRay, "X-ray");

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class ColorMapModeCommand() : VisualizationModeCommand(VisualizationMode.ColorMap, "Color Map");
