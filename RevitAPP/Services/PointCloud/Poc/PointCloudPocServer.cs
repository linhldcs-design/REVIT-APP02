using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Serilog;

namespace RevitAPP.Services.PointCloud.Poc;

/// <summary>
///     POC server (throwaway): render danh sách điểm bằng billboard quad qua DirectContext3D
///     để verify point size đổi được. KHÔNG dùng cho production — chỉ chứng minh khả thi (Phase 0).
/// </summary>
public sealed class PointCloudPocServer : IDirectContext3DServer
{
    private readonly Guid _guid = Guid.NewGuid();
    private readonly IReadOnlyList<XYZ> _points;
    private readonly double _pointSizeFeet;

    private VertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;
    private VertexFormat? _vertexFormat;
    private EffectInstance? _effect;
    private int _triangleCount;

    public PointCloudPocServer(IReadOnlyList<XYZ> points, double pointSizeFeet)
    {
        _points = points;
        _pointSizeFeet = pointSizeFeet;
    }

    public Guid GetServerId() => _guid;
    public string GetVendorId() => "RevitAPP";
    public string GetName() => "Point Cloud POC Render";
    public string GetDescription() => "POC billboard quad point cloud render";
    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
    public string GetApplicationId() => "RevitAPP";
    public string GetSourceId() => "";
    public bool UsesHandles() => false;
    public bool CanExecute(View view) => view.ViewType == ViewType.ThreeD;
    public bool UseInTransparentPass(View view) => false;

    public Outline GetBoundingBox(View view)
    {
        var min = new XYZ(_points.Min(p => p.X), _points.Min(p => p.Y), _points.Min(p => p.Z));
        var max = new XYZ(_points.Max(p => p.X), _points.Max(p => p.Y), _points.Max(p => p.Z));
        return new Outline(min, max);
    }

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            if (_vertexBuffer == null) BuildBuffers();
            if (_triangleCount == 0) return;

            DrawContext.FlushBuffer(
                _vertexBuffer, _points.Count * 4,
                _indexBuffer, _triangleCount * 3,
                _vertexFormat, _effect,
                PrimitiveType.TriangleList, 0, _triangleCount);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "POC RenderScene thất bại");
        }
    }

    private void BuildBuffers()
    {
        var half = _pointSizeFeet / 2.0;
        var count = _points.Count;
        var vertexCount = count * 4;
        var triCount = count * 2;

        _vertexFormat = new VertexFormat(VertexFormatBits.PositionColored);
        _effect = new EffectInstance(VertexFormatBits.PositionColored);

        _vertexBuffer = new VertexBuffer(vertexCount * VertexPositionColored.GetSizeInFloats());
        _vertexBuffer.Map(vertexCount * VertexPositionColored.GetSizeInFloats());
        var vStream = _vertexBuffer.GetVertexStreamPositionColored();

        var color = new ColorWithTransparency(255, 80, 0, 0); // cam, đục
        // Quad screen-aligned đơn giản (POC): offset theo X/Y model — đủ để thấy point size.
        foreach (var p in _points)
        {
            vStream.AddVertex(new VertexPositionColored(new XYZ(p.X - half, p.Y - half, p.Z), color));
            vStream.AddVertex(new VertexPositionColored(new XYZ(p.X + half, p.Y - half, p.Z), color));
            vStream.AddVertex(new VertexPositionColored(new XYZ(p.X + half, p.Y + half, p.Z), color));
            vStream.AddVertex(new VertexPositionColored(new XYZ(p.X - half, p.Y + half, p.Z), color));
        }

        _vertexBuffer.Unmap();

        _indexBuffer = new IndexBuffer(triCount * IndexTriangle.GetSizeInShortInts());
        _indexBuffer.Map(triCount * IndexTriangle.GetSizeInShortInts());
        var iStream = _indexBuffer.GetIndexStreamTriangle();
        for (var i = 0; i < count; i++)
        {
            var b = i * 4;
            iStream.AddTriangle(new IndexTriangle(b, b + 1, b + 2));
            iStream.AddTriangle(new IndexTriangle(b, b + 2, b + 3));
        }

        _indexBuffer.Unmap();
        _triangleCount = triCount;
    }
}
