using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;
using RevitAPP.Core.Models;
using Serilog;
using RevitAPP.Helpers;

namespace RevitAPP.Services.PointCloud;

/// <inheritdoc />
public sealed class PointCloudReader : IPointCloudReader
{
    /// <summary>Trần điểm mỗi lần đọc. Octagon = 9 vertex/điểm → 500K điểm ≈ 4.5M vertex (GPU chịu được).
    ///     Đủ dày nhìn như cloud, không treo. Tăng/giảm theo performance thực tế.</summary>
    private const int MaxPoints = 500_000;

    private readonly Dictionary<string, PointCloudReadResult> _cache = new();

    /// <summary>Trần số entry cache — point cloud lớn nên không giữ vô hạn.</summary>
    private const int MaxCacheEntries = 8;

    public PointCloudReadResult Read(PointCloudInstance instance, View view, double density)
    {
        // Key gồm cả trạng thái crop box → đổi crop không lấy điểm cũ stale.
        var cropKey = view.CropBoxActive ? FormatBox(view.CropBox) : "nocrop";
        var key = $"{instance.Id.ToValue()}|{view.Id.ToValue()}|{density:0.###}|{cropKey}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        if (_cache.Count >= MaxCacheEntries) _cache.Clear();

        var result = ReadCore(instance, view, density);
        _cache[key] = result;
        return result;
    }

    private static string FormatBox(BoundingBoxXYZ box) =>
        $"{box.Min.X:0.#},{box.Min.Y:0.#},{box.Min.Z:0.#}|{box.Max.X:0.#},{box.Max.Y:0.#},{box.Max.Z:0.#}";

    public void ClearCache() => _cache.Clear();

    private static PointCloudReadResult ReadCore(PointCloudInstance instance, View view, double density)
    {
        var bbox = GetSearchBox(instance, view);
        var filter = BuildBoxFilter(bbox);
        var averageDistance = Math.Max(0.001, density);

        var transform = instance.GetTotalTransform();
        var origin = transform.OfPoint(new XYZ(0, 0, 0)); // tham chiếu để offset, giữ float chính xác

        var points = new List<RenderPoint>();
        try
        {
            var collection = instance.GetPoints(filter, averageDistance, MaxPoints);
            foreach (CloudPoint cp in collection)
            {
                var model = transform.OfPoint(new XYZ(cp.X, cp.Y, cp.Z));
                points.Add(new RenderPoint(
                    (float)(model.X - origin.X),
                    (float)(model.Y - origin.Y),
                    (float)(model.Z - origin.Z),
                    Win32ToRgba(cp.Color)));
                if (points.Count >= MaxPoints) break;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Đọc điểm point cloud {Id} thất bại", instance.Id.ToValue());
        }

        return new PointCloudReadResult(points, origin);
    }

    /// <summary>Hộp tìm kiếm: ưu tiên crop box của view, fallback bounding box của instance.</summary>
    private static BoundingBoxXYZ GetSearchBox(PointCloudInstance instance, View view)
    {
        if (view.CropBoxActive && view.CropBox != null) return view.CropBox;
        return instance.get_BoundingBox(null) ?? view.get_BoundingBox(null);
    }

    private static PointCloudFilter BuildBoxFilter(BoundingBoxXYZ bbox)
    {
        var planes = new List<Plane>
        {
            Plane.CreateByNormalAndOrigin(XYZ.BasisX, bbox.Min),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisX, bbox.Max),
            Plane.CreateByNormalAndOrigin(XYZ.BasisY, bbox.Min),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisY, bbox.Max),
            Plane.CreateByNormalAndOrigin(XYZ.BasisZ, bbox.Min),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisZ, bbox.Max)
        };
        return PointCloudFilterFactory.CreateMultiPlaneFilter(planes);
    }

    /// <summary>CloudPoint.Color (Win32 0x00BBGGRR) → RGBA 0xAABBGGRR với alpha đầy đủ.</summary>
    private static uint Win32ToRgba(int win32)
    {
        var c = (uint)win32;
        return 0xFF000000u | (c & 0x00FFFFFFu);
    }
}
