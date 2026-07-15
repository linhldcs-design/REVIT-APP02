using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;
using Autodesk.Revit.UI;

namespace PointCloudViewer.Addin.Services;

public sealed class RevitPointCloudViewService
{
    public IReadOnlyList<PointCloudInstance> GetPointClouds(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(PointCloudInstance))
            .Cast<PointCloudInstance>()
            .ToList();
    }

    public IReadOnlyList<PointCloudInstance> GetPointCloudsInView(Document document, View view)
    {
        return new FilteredElementCollector(document, view.Id)
            .OfClass(typeof(PointCloudInstance))
            .Cast<PointCloudInstance>()
            .ToList();
    }

    public int ApplyMode(Document document, View view, PointCloudColorMode mode)
    {
        var pointClouds = GetPointCloudsInView(document, view);
        if (pointClouds.Count == 0)
        {
            pointClouds = GetPointClouds(document);
        }

        if (pointClouds.Count == 0)
        {
            return 0;
        }

        using var transaction = new Transaction(document, $"Point Cloud {mode}");
        transaction.Start();

        var overrides = view.GetPointCloudOverrides();
        foreach (var pointCloud in pointClouds)
        {
            var settings = new PointCloudOverrideSettings
            {
                Visible = true,
                ColorMode = mode
            };

            settings.SetModeOverride(mode, CreateColorSettings(mode));
            overrides.SetPointCloudScanOverrideSettings(pointCloud.Id, settings);
        }

        transaction.Commit();
        return pointClouds.Count;
    }

    public string BuildSummary(Document document, View view)
    {
        var allClouds = GetPointClouds(document);
        var viewClouds = GetPointCloudsInView(document, view);

        if (allClouds.Count == 0)
        {
            return "No point cloud instances were found in this project.";
        }

        var lines = new List<string>
        {
            $"Point clouds in project: {allClouds.Count}",
            $"Point clouds visible in active view: {viewClouds.Count}",
            string.Empty
        };

        foreach (var pointCloud in allClouds.Take(12))
        {
            var type = document.GetElement(pointCloud.GetTypeId()) as PointCloudType;
            var name = pointCloud.Name;
            var path = type is null
                ? "(path unavailable)"
                : ModelPathUtils.ConvertModelPathToUserVisiblePath(type.GetPath());
            var scans = SafeCount(pointCloud.GetScans());
            var regions = SafeCount(pointCloud.GetRegions());
            lines.Add($"{pointCloud.Id.Value}: {name}");
            lines.Add($"  Path: {path}");
            lines.Add($"  Scans: {scans}, Regions: {regions}, Has color: {pointCloud.HasColor()}");
        }

        if (allClouds.Count > 12)
        {
            lines.Add($"... {allClouds.Count - 12} more point cloud instances not shown.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public int HideNativeCloudsInView(Document document, View view, IEnumerable<ElementId> pointCloudIds)
    {
        var ids = pointCloudIds
            .Select(document.GetElement)
            .Where(element => element is not null && element.CanBeHidden(view) && !element.IsHidden(view))
            .Select(element => element!.Id)
            .ToList();

        if (ids.Count == 0)
        {
            return 0;
        }

        using var transaction = new Transaction(document, "Hide native point clouds for DirectContext3D");
        transaction.Start();
        view.HideElements(ids);
        transaction.Commit();
        return ids.Count;
    }

    public void Refresh(UIDocument uiDocument)
    {
        uiDocument.RefreshActiveView();
    }

    private static PointCloudColorSettings CreateColorSettings(PointCloudColorMode mode)
    {
        return mode switch
        {
            PointCloudColorMode.FixedColor => new PointCloudColorSettings(new Color(215, 235, 255), new Color(255, 255, 255)),
            PointCloudColorMode.Elevation => new PointCloudColorSettings(new Color(0, 80, 255), new Color(255, 70, 40)),
            PointCloudColorMode.Intensity => new PointCloudColorSettings(new Color(30, 30, 30), new Color(255, 255, 255)),
            _ => new PointCloudColorSettings(mode)
        };
    }

    private static int SafeCount(ICollection<string> values)
    {
        return values.Count;
    }
}
