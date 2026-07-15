using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Serilog;

namespace PointCloudViewer.Addin.Services;

public sealed class PointCloudDirectContext3DServer(PointCloudDirectContext3DService renderer, ILogger logger) : IDirectContext3DServer
{
    private const int FloatsPerPositionColoredVertex = 4;
    private static readonly VertexFormatBits FormatBits = VertexFormatBits.PositionColored;

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        var frame = renderer.CurrentFrame;
        if (frame.PointCount == 0 || !DrawContext.IsAvailable())
        {
            return;
        }

        try
        {
            using var vertexBuffer = new VertexBuffer(frame.PointCount * FloatsPerPositionColoredVertex);
            vertexBuffer.Map(frame.PointCount * FloatsPerPositionColoredVertex);
            var vertexStream = vertexBuffer.GetVertexStreamPositionColored();
            vertexStream.AddVertices(frame.Vertices.ToList());
            vertexBuffer.Unmap();

            using var indexBuffer = new IndexBuffer(frame.PointCount);
            indexBuffer.Map(frame.PointCount);
            var indexStream = indexBuffer.GetIndexStreamPoint();
            for (var index = 0; index < frame.PointCount; index++)
            {
                indexStream.AddPoint(new IndexPoint(index));
            }

            indexBuffer.Unmap();
            var vertexFormat = new VertexFormat(FormatBits);
            var effect = new EffectInstance(FormatBits);
            DrawContext.FlushBuffer(
                vertexBuffer,
                frame.PointCount,
                indexBuffer,
                frame.PointCount,
                vertexFormat,
                effect,
                PrimitiveType.PointList,
                0,
                frame.PointCount);
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "DirectContext3D point cloud render failed");
        }
    }

    public bool UseInTransparentPass(View view)
    {
        return true;
    }

    public Outline GetBoundingBox(View view)
    {
        return renderer.CurrentFrame.Outline ?? new Outline(new XYZ(-100, -100, -100), new XYZ(100, 100, 100));
    }

    public bool UsesHandles()
    {
        return false;
    }

    public string GetSourceId()
    {
        return "PointCloudViewer.DirectContext3D";
    }

    public string GetApplicationId()
    {
        return "PointCloudViewer";
    }

    public bool CanExecute(View view)
    {
        return renderer.CurrentFrame.PointCount > 0;
    }

    public Guid GetServerId()
    {
        return renderer.GetServerId();
    }

    public ExternalServiceId GetServiceId()
    {
        return ExternalServices.BuiltInExternalServices.DirectContext3DService;
    }

    public string GetName()
    {
        return "Point Cloud Viewer DirectContext3D";
    }

    public string GetVendorId()
    {
        return "BSAR";
    }

    public string GetDescription()
    {
        return "Draws sampled Revit point cloud data through a dedicated DirectContext3D server.";
    }
}
