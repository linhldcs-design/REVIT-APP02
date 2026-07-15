using Autodesk.Revit.DB;
using FootingDrawing.Core.Models;

namespace FootingDrawing.Addin.Services;

/// <summary>
///     Đọc hình học một móng đơn thành <see cref="FootingGeometry"/>: quét solid bê tông lấy đáy/đỉnh
///     THẬT theo Z, bề rộng đế theo 2 phương DirX/DirY. Phát hiện cổ móng (móng cốc) bằng cách so footprint
///     dải Z trên với dải Z dưới — dải trên hẹp hơn rõ rệt → đó là cổ móng, mặt trên đế = đáy cổ.
/// </summary>
public sealed class FootingGeometryReader
{
    private const double MinFeet = 1e-6;

    /// <param name="dirXOverride">Hướng X do người dùng chỉ định (đã chuẩn hóa, mặt phẳng XY). null →
    ///     lấy theo hướng family móng.</param>
    public bool TryRead(Element foundation, XYZ? dirXOverride, out FootingGeometry geometry, out string error)
    {
        geometry = null!;
        error = string.Empty;

        var vertices = CollectSolidVertices(foundation);
        if (vertices.Count == 0)
        {
            error = $"Móng id {foundation.Id.ToLong()}: không lấy được solid bê tông để đọc hình học.";
            return false;
        }

        var dirX = ResolveDirX(foundation, dirXOverride);
        var dirY = XYZ.BasisZ.CrossProduct(dirX).Normalize();

        var bottomZ = vertices.Min(p => p.Z);
        var topZ = vertices.Max(p => p.Z);
        if (topZ - bottomZ < MinFeet)
        {
            error = $"Móng id {foundation.Id.ToLong()}: chiều cao móng bằng 0.";
            return false;
        }

        var (centerXY, fullWx, fullWy) = PlanExtent(vertices, dirX, dirY);
        if (fullWx < MinFeet || fullWy < MinFeet)
        {
            error = $"Móng id {foundation.Id.ToLong()}: bề rộng đế bằng 0.";
            return false;
        }

        var pedestal = DetectPedestal(vertices, dirX, dirY, centerXY, bottomZ, topZ, fullWx, fullWy,
            out var baseTopZ);

        geometry = new FootingGeometry
        {
            BaseCenter = new Point3(centerXY.X, centerXY.Y, bottomZ),
            DirX = new Point3(dirX.X, dirX.Y, dirX.Z),
            DirY = new Point3(dirY.X, dirY.Y, dirY.Z),
            WidthXFeet = fullWx,
            WidthYFeet = fullWy,
            BottomZFeet = bottomZ,
            BaseTopZFeet = baseTopZ,
            Pedestal = pedestal
        };
        return true;
    }

    private static XYZ ResolveDirX(Element foundation, XYZ? dirXOverride)
    {
        if (dirXOverride is not null)
        {
            var projected = new XYZ(dirXOverride.X, dirXOverride.Y, 0);
            if (projected.GetLength() > MinFeet) return projected.Normalize();
        }

        if (foundation is FamilyInstance fi)
        {
            var basisX = fi.GetTransform().BasisX;
            var projected = new XYZ(basisX.X, basisX.Y, 0);
            if (projected.GetLength() > MinFeet) return projected.Normalize();
        }

        return XYZ.BasisX;
    }

    private static (XYZ Center, double WidthX, double WidthY) PlanExtent(
        IReadOnlyList<XYZ> points, XYZ dirX, XYZ dirY)
    {
        double minU = double.MaxValue, maxU = double.MinValue;
        double minV = double.MaxValue, maxV = double.MinValue;
        foreach (var p in points)
        {
            var u = p.DotProduct(dirX);
            var v = p.DotProduct(dirY);
            if (u < minU) minU = u;
            if (u > maxU) maxU = u;
            if (v < minV) minV = v;
            if (v > maxV) maxV = v;
        }

        var midU = (minU + maxU) / 2;
        var midV = (minV + maxV) / 2;
        var center = dirX * midU + dirY * midV;
        return (center, maxU - minU, maxV - minV);
    }

    private static PedestalBox? DetectPedestal(
        IReadOnlyList<XYZ> vertices, XYZ dirX, XYZ dirY,
        XYZ baseCenter, double bottomZ, double topZ, double fullWx, double fullWy, out double baseTopZ)
    {
        baseTopZ = topZ;

        var height = topZ - bottomZ;
        var topBand = vertices.Where(p => p.Z >= topZ - height * 0.1).ToList();
        if (topBand.Count == 0) return null;

        var (pedestalCenter, pedWx, pedWy) = PlanExtent(topBand, dirX, dirY);
        var isPedestal = pedWx < fullWx * 0.85 && pedWy < fullWy * 0.85;
        if (!isPedestal) return null;

        var halfX = pedWx / 2 + MmToFeet(50);
        var halfY = pedWy / 2 + MmToFeet(50);
        var centerU = pedestalCenter.DotProduct(dirX);
        var centerV = pedestalCenter.DotProduct(dirY);
        var bottomGuard = bottomZ + MmToFeet(20);
        var pedestalColumn = vertices
            .Where(p => p.Z > bottomGuard
                        && Math.Abs(p.DotProduct(dirX) - centerU) <= halfX
                        && Math.Abs(p.DotProduct(dirY) - centerV) <= halfY)
            .ToList();
        if (pedestalColumn.Count == 0) return null;

        baseTopZ = pedestalColumn.Min(p => p.Z);

        return new PedestalBox
        {
            CenterU = 0.5 + (pedestalCenter - baseCenter).DotProduct(dirX) / fullWx,
            CenterV = 0.5 + (pedestalCenter - baseCenter).DotProduct(dirY) / fullWy,
            WidthXFeet = pedWx,
            WidthYFeet = pedWy,
            TopZFeet = topZ
        };
    }

    private static double MmToFeet(double mm) => mm / 304.8;

    private static List<XYZ> CollectSolidVertices(Element element)
    {
        var points = new List<XYZ>();
        var options = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
        var geom = element.get_Geometry(options);
        if (geom is null) return points;

        foreach (var solid in EnumerateSolids(geom))
            foreach (Edge edge in solid.Edges)
                points.AddRange(edge.Tessellate());

        return points;
    }

    private static IEnumerable<Solid> EnumerateSolids(GeometryElement geom)
    {
        foreach (var obj in geom)
        {
            switch (obj)
            {
                case Solid { Volume: > 1e-9 } solid:
                    yield return solid;
                    break;
                case GeometryInstance gi:
                    foreach (var inner in EnumerateSolids(gi.GetInstanceGeometry()))
                        yield return inner;
                    break;
            }
        }
    }
}
