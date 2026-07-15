using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using RevitAPP.Core.Models;
using RevitAPP.Core.Services;
using Serilog;
using System.Diagnostics;

namespace RevitAPP.Services.PointCloud;

/// <summary>
///     Render point cloud bằng billboard quad camera-facing qua DirectContext3D.
///     Point size / brightness / transparency / color lấy từ <see cref="PointCloudRenderState" />.
///     Chia điểm thành chunk ≤ giới hạn index 16-bit của Revit; dispose buffer cũ khi rebuild.
/// </summary>
public sealed class PointCloudRenderServer : IDirectContext3DServer
{
    /// <summary>Số cạnh đa giác mỗi điểm (octagon ~ tròn). 8 đỉnh + 1 tâm = 9 vertex.</summary>
    private const int Sides = 8;
    private const int VertsPerPoint = Sides + 1; // tâm + viền
    private const int TrisPerPoint = Sides * 2;  // 2-sided: mỗi cạnh 2 tam giác (front+back) → không bị back-face cull

    /// <summary>Index buffer Revit dùng 16-bit (max 65535 vertex/buffer).
    ///     9 vertex/điểm → giới hạn an toàn 7000 điểm/chunk (7000*9 = 63000 < 65535).</summary>
    private const int MaxPointsPerChunk = 7_000;

    private readonly Guid _guid = Guid.NewGuid();
    private readonly List<RenderChunk> _chunks = new();

    private IReadOnlyList<RenderPoint> _points = Array.Empty<RenderPoint>();
    private XYZ _origin = XYZ.Zero;
    private PointCloudRenderState _state = PointCloudRenderState.Default;
    private Outline? _cachedBounds;

    private bool _dirty = true;

    public Guid GetServerId() => _guid;
    public string GetVendorId() => "RevitAPP";
    public string GetName() => "Point Cloud Render";
    public string GetDescription() => "Custom point cloud render (DirectContext3D billboard).";
    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
    public string GetApplicationId() => "RevitAPP";
    public string GetSourceId() => "";
    public bool UsesHandles() => false;
    public bool CanExecute(View view) => _points.Count > 0 && view.ViewType == ViewType.ThreeD;
    public bool UseInTransparentPass(View view) => _state.Transparency > 0;

    /// <summary>Nạp điểm mới (đổi instance / density / view).</summary>
    public void SetPoints(IReadOnlyList<RenderPoint> points, XYZ origin)
    {
        _points = points;
        _origin = origin;
        _cachedBounds = null;
        _dirty = true;
    }

    /// <summary>Cập nhật state từ slider; đánh dấu dirty nếu giá trị đổi.</summary>
    public void UpdateState(PointCloudRenderState state)
    {
        if (Equals(state, _state)) return;

        var previous = _state;
        _state = state;

        if (state.RequiresGeometryRebuild(previous) || state.RequiresColorRebuild(previous))
        {
            _dirty = true;
            return;
        }

        if (state.RequiresEffectUpdate(previous))
        {
            foreach (var chunk in _chunks)
                chunk.SetTransparency(state.Transparency);
        }
    }

    /// <summary>Giải phóng mọi GPU buffer (gọi khi tắt render / dispose).</summary>
    public void ReleaseBuffers()
    {
        foreach (var chunk in _chunks) chunk.Dispose();
        _chunks.Clear();
        _dirty = true;
    }

    public Outline GetBoundingBox(View view)
    {
        if (_points.Count == 0) return new Outline(XYZ.Zero, XYZ.Zero);
        return _cachedBounds ??= ComputeBounds();
    }

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            if (_points.Count == 0) return;


            // Octagon đối xứng tròn → KHÔNG cần rebuild khi camera xoay (lợi thế hình tròn).
            // Chỉ rebuild khi điểm/state đổi → tránh rebuild hàng triệu vertex mỗi frame (killer performance).
            if (_dirty || _chunks.Count == 0)
            {
                var camera = DrawContext.GetCamera();
                var (right, up) = CameraAxes(camera);
                RebuildChunks(right, up);
                _dirty = false;
            }

            // Điểm trong suốt phải vẽ ở transparent pass; điểm đục ở opaque pass.
            // Transparency đồng nhất (1 slider) → tất cả chunk cùng loại.
            var transparent = _state.Transparency > 0;
            if (transparent != DrawContext.IsTransparentPass()) return;

            foreach (var chunk in _chunks)
                chunk.Flush();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "PointCloudRenderServer.RenderScene thất bại");
        }
    }

    private void RebuildChunks(XYZ right, XYZ up)
    {
        var stopwatch = Stopwatch.StartNew();
        ReleaseBuffers(); // dispose buffer cũ trước khi tạo mới — tránh leak native

        var half = _state.PointSizeFeet / 2.0;
        var r = (right.X, right.Y, right.Z);
        var u = (up.X, up.Y, up.Z);

        for (var start = 0; start < _points.Count; start += MaxPointsPerChunk)
        {
            var count = Math.Min(MaxPointsPerChunk, _points.Count - start);
            _chunks.Add(BuildChunk(start, count, r, u, half));
        }

        _dirty = false; // RebuildChunks set lại ở RenderScene; giữ ở đây cho an toàn
        Log.Information("Custom render: rebuilt {ChunkCount} chunks for {PointCount} points in {ElapsedMs} ms",
            _chunks.Count, _points.Count, stopwatch.ElapsedMilliseconds);
    }

    private RenderChunk BuildChunk(int start, int count,
        (double, double, double) right, (double, double, double) up, double half)
    {
        var vertexCount = count * VertsPerPoint;
        var triCount = count * TrisPerPoint;

        var vertexFormat = new VertexFormat(VertexFormatBits.PositionColored);
        var effect = new EffectInstance(VertexFormatBits.PositionColored);

        // Transparency phải set qua EffectInstance để DirectContext3D blend thật (per-vertex alpha không đủ).
        // SetTransparency nhận 0..1; slider 0..100 → /100. Chỉ set transparency (KHÔNG set color để giữ màu per-vertex).
        if (_state.Transparency > 0)
            effect.SetTransparency(_state.Transparency / 100.0);

        var floats = vertexCount * VertexPositionColored.GetSizeInFloats();
        var vertexBuffer = new VertexBuffer(floats);
        vertexBuffer.Map(floats);
        var vStream = vertexBuffer.GetVertexStreamPositionColored();

        // Mỗi điểm = octagon (tâm + 8 đỉnh viền) → trông tròn, vẫn điều khiển được size.
        for (var i = 0; i < count; i++)
        {
            var p = _points[start + i];
            var cx = p.X + _origin.X;
            var cy = p.Y + _origin.Y;
            var cz = p.Z + _origin.Z;
            var fan = BillboardMath.PolygonFan(cx, cy, cz, right, up, half, Sides);
            var color = RgbaToRevit(_state.ResolveOpaqueVertexColor(p.Color));

            foreach (var c in fan)
                vStream.AddVertex(new VertexPositionColored(new XYZ(c.Item1, c.Item2, c.Item3), color));
        }

        vertexBuffer.Unmap();

        var shorts = triCount * IndexTriangle.GetSizeInShortInts();
        var indexBuffer = new IndexBuffer(shorts);
        indexBuffer.Map(shorts);
        var iStream = indexBuffer.GetIndexStreamTriangle();
        for (var i = 0; i < count; i++)
        {
            var b = i * VertsPerPoint;   // index cục bộ: tâm=b, viền=b+1..b+Sides
            for (var s = 0; s < Sides; s++)
            {
                var v1 = b + 1 + s;
                var v2 = b + 1 + (s + 1) % Sides; // đỉnh kế (vòng lại đỉnh đầu)
                iStream.AddTriangle(new IndexTriangle(b, v1, v2)); // mặt trước
                iStream.AddTriangle(new IndexTriangle(b, v2, v1)); // mặt sau (đảo winding) → 2-sided
            }
        }

        indexBuffer.Unmap();

        return new RenderChunk(vertexBuffer, indexBuffer, vertexFormat, effect, vertexCount, triCount);
    }

    private Outline ComputeBounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var p in _points)
        {
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
        }
        return new Outline(
            new XYZ(minX + _origin.X, minY + _origin.Y, minZ + _origin.Z),
            new XYZ(maxX + _origin.X, maxY + _origin.Y, maxZ + _origin.Z));
    }

    /// <summary>RGBA 0xAABBGGRR → ColorWithTransparency(r,g,b, transparency). Revit: transparency 0=đục.</summary>
    private static (XYZ right, XYZ up) CameraAxes(Camera camera)
    {
        var view = camera.ViewDirection;
        var up = camera.UpDirection;
        var right = view.CrossProduct(up).Normalize();
        return (right, up.Normalize());
    }

    private static ColorWithTransparency RgbaToRevit(uint rgba)
    {
        var r = rgba & 0xFF;
        var g = (rgba >> 8) & 0xFF;
        var b = (rgba >> 16) & 0xFF;
        var alpha = (rgba >> 24) & 0xFF;
        var transparency = 255 - alpha; // alpha 255 (đục) → transparency 0
        return new ColorWithTransparency(r, g, b, transparency);
    }

    /// <summary>Một chunk GPU buffer (≤16K điểm). Tự dispose buffer native.</summary>
    private sealed class RenderChunk : IDisposable
    {
        private readonly VertexBuffer _vertexBuffer;
        private readonly IndexBuffer _indexBuffer;
        private readonly VertexFormat _vertexFormat;
        private readonly EffectInstance _effect;
        private readonly int _vertexCount;
        private readonly int _triCount;

        public RenderChunk(VertexBuffer vb, IndexBuffer ib, VertexFormat vf, EffectInstance fx, int vertexCount, int triCount)
        {
            _vertexBuffer = vb;
            _indexBuffer = ib;
            _vertexFormat = vf;
            _effect = fx;
            _vertexCount = vertexCount;
            _triCount = triCount;
        }

        public void Flush()
        {
            if (!_vertexBuffer.IsValid() || !_indexBuffer.IsValid()) return;
            DrawContext.FlushBuffer(
                _vertexBuffer, _vertexCount,
                _indexBuffer, _triCount * 3,
                _vertexFormat, _effect,
                PrimitiveType.TriangleList, 0, _triCount);
        }

        public void SetTransparency(int transparency)
        {
            _effect.SetTransparency(Math.Clamp(transparency, 0, 100) / 100.0);
        }

        public void Dispose()
        {
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _vertexFormat.Dispose();
            _effect.Dispose();
        }
    }
}
