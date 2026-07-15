using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.DB.PointClouds;
using Autodesk.Revit.UI;
using PointCloudViewer.Core.Models;
using Serilog;

namespace PointCloudViewer.Addin.Services;

public sealed class PointCloudDirectContext3DService(
    PointCloudSettingsStore settingsStore,
    RevitPointCloudViewService pointCloudViewService,
    ILogger logger)
{
    private const int MaxPointCount = 60000;
    private const double AverageDistanceFeet = 0.05;
    private static readonly Guid ServerId = new("9d6eea53-6d1c-4bb7-a840-6a4ea24ec650");
    private PointCloudDirectContext3DServer? _server;
    private bool _isRegistered;

    public DirectContextPointCloudFrame CurrentFrame { get; private set; } = DirectContextPointCloudFrame.Empty;

    public string Status { get; private set; } = "DirectContext3D server is not registered.";

    public Guid GetServerId()
    {
        return ServerId;
    }

    public void Register()
    {
        if (_isRegistered)
        {
            return;
        }

        _server ??= new PointCloudDirectContext3DServer(this, logger);
        var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
            as MultiServerService;
        if (service is null)
        {
            Status = "DirectContext3D service is unavailable.";
            logger.Warning("DirectContext3D external service is unavailable");
            return;
        }

        service.AddServer(_server);
        var activeServerIds = service.GetActiveServerIds().ToList();
        if (!activeServerIds.Contains(ServerId))
        {
            activeServerIds.Add(ServerId);
            service.SetActiveServers(activeServerIds);
        }

        Status = "DirectContext3D server registered.";
        _isRegistered = true;
        logger.Information("Point cloud DirectContext3D server registered");
    }

    public void Unregister()
    {
        try
        {
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
                as MultiServerService;
            service?.RemoveServer(ServerId);
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Unable to unregister DirectContext3D server");
        }

        CurrentFrame = DirectContextPointCloudFrame.Empty;
        _server = null;
        _isRegistered = false;
        Status = "DirectContext3D server stopped.";
    }

    public int RebuildFromActiveView(UIDocument uiDocument, bool hideNativePointClouds)
    {
        Register();

        var document = uiDocument.Document;
        var view = uiDocument.ActiveView;
        var settings = settingsStore.Current;
        var pointClouds = pointCloudViewService.GetPointCloudsInView(document, view);
        if (pointClouds.Count == 0)
        {
            pointClouds = pointCloudViewService.GetPointClouds(document);
        }

        CurrentFrame = BuildFrame(pointClouds, settings);

        if (hideNativePointClouds && pointClouds.Count > 0)
        {
            pointCloudViewService.HideNativeCloudsInView(document, view, pointClouds.Select(pointCloud => pointCloud.Id));
        }

        uiDocument.RefreshActiveView();
        Status = $"DirectContext3D frame rebuilt: {CurrentFrame.PointCount} sampled point(s), {pointClouds.Count} source instance(s).";
        logger.Information(
            "DirectContext3D frame rebuilt with {PointCount} points from {InstanceCount} point cloud instances",
            CurrentFrame.PointCount,
            pointClouds.Count);

        return CurrentFrame.PointCount;
    }

    private static DirectContextPointCloudFrame BuildFrame(IReadOnlyList<PointCloudInstance> pointClouds, PointCloudRenderSettings settings)
    {
        if (pointClouds.Count == 0)
        {
            return DirectContextPointCloudFrame.Empty;
        }

        var vertices = new List<VertexPositionColored>(MaxPointCount);
        Outline? outline = null;
        var remaining = MaxPointCount;
        var filter = CreateLargeFilter();

        foreach (var pointCloud in pointClouds)
        {
            if (remaining <= 0)
            {
                break;
            }

            PointCollection points;
            try
            {
                points = pointCloud.GetPoints(filter, AverageDistanceFeet, remaining);
            }
            catch
            {
                continue;
            }

            var transform = pointCloud.GetTransform();
            foreach (var cloudPoint in points)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var position = transform.OfPoint(cloudPoint);
                var color = CreateColor(cloudPoint, position, settings);
                vertices.Add(new VertexPositionColored(position, color));
                outline = ExpandOutline(outline, position);
                remaining--;
            }
        }

        return new DirectContextPointCloudFrame(vertices, outline, pointClouds.Count);
    }

    private static PointCloudFilter CreateLargeFilter()
    {
        const double size = 10_000;
        var planes = new List<Plane>
        {
            Plane.CreateByNormalAndOrigin(XYZ.BasisX, new XYZ(-size, 0, 0)),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisX, new XYZ(size, 0, 0)),
            Plane.CreateByNormalAndOrigin(XYZ.BasisY, new XYZ(0, -size, 0)),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisY, new XYZ(0, size, 0)),
            Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, -size)),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisZ, new XYZ(0, 0, size))
        };

        return PointCloudFilterFactory.CreateMultiPlaneFilter(planes);
    }

    private static ColorWithTransparency CreateColor(CloudPoint cloudPoint, XYZ position, PointCloudRenderSettings settings)
    {
        var (red, green, blue) = DecodeColor(cloudPoint.Color);
        (red, green, blue) = settings.Mode switch
        {
            VisualizationMode.Normal => CreateNormalColor(position),
            VisualizationMode.XRay => (190, 225, 255),
            VisualizationMode.ColorMap => CreateElevationColor(position.Z),
            _ => ApplyBrightnessContrast(red, green, blue, settings.Brightness, settings.Contrast)
        };

        var transparency = (uint)Math.Round(Math.Clamp(settings.Transparency, 0, 100) * 2.55);
        return new ColorWithTransparency((uint)red, (uint)green, (uint)blue, transparency);
    }

    private static (int Red, int Green, int Blue) DecodeColor(int color)
    {
        var red = color & 0xFF;
        var green = (color >> 8) & 0xFF;
        var blue = (color >> 16) & 0xFF;
        return (red, green, blue);
    }

    private static (int Red, int Green, int Blue) ApplyBrightnessContrast(int red, int green, int blue, double brightness, double contrast)
    {
        var factor = (259 * (contrast + 255)) / (255 * (259 - contrast));
        return (
            ClampColor(factor * (red - 128) + 128 + brightness),
            ClampColor(factor * (green - 128) + 128 + brightness),
            ClampColor(factor * (blue - 128) + 128 + brightness));
    }

    private static (int Red, int Green, int Blue) CreateNormalColor(XYZ position)
    {
        return (
            ClampColor(Math.Abs(position.X % 1) * 255),
            ClampColor(Math.Abs(position.Y % 1) * 255),
            ClampColor(Math.Abs(position.Z % 1) * 255));
    }

    private static (int Red, int Green, int Blue) CreateElevationColor(double z)
    {
        var value = Math.Abs(z % 60) / 60;
        return (
            ClampColor(255 * value),
            ClampColor(160 * (1 - Math.Abs(value - 0.5) * 2)),
            ClampColor(255 * (1 - value)));
    }

    private static int ClampColor(double value)
    {
        return (int)Math.Round(Math.Clamp(value, 0, 255));
    }

    private static Outline ExpandOutline(Outline? outline, XYZ position)
    {
        if (outline is null)
        {
            return new Outline(position, position);
        }

        outline.AddPoint(position);
        return outline;
    }
}
