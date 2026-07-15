using PointCloudViewer.Core.Models;

namespace PointCloudViewer.Core.Caching;

public enum RenderSettingsChangeImpact
{
    None,
    RedrawOnly,
    RebuildBatches
}

public sealed class RenderSettingsChangeClassifier
{
    public RenderSettingsChangeImpact Classify(PointCloudRenderSettings before, PointCloudRenderSettings after)
    {
        if (before.Mode == after.Mode &&
            before.PointSize == after.PointSize &&
            before.Brightness == after.Brightness &&
            before.Contrast == after.Contrast &&
            before.Transparency == after.Transparency &&
            before.XRayContrast == after.XRayContrast)
        {
            return RenderSettingsChangeImpact.None;
        }

        if (before.PointSize != after.PointSize)
        {
            return RenderSettingsChangeImpact.RebuildBatches;
        }

        return RenderSettingsChangeImpact.RedrawOnly;
    }
}
