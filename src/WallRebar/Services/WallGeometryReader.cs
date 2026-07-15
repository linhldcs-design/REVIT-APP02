using Autodesk.Revit.DB;
using WallRebar.Models;

namespace WallRebar.Services;

/// <summary>
///     Reads a straight wall into a local frame used by the rebar creators.
///     LocationCurve is used only for the longitudinal axis; the real wall face and base are read from geometry extents.
/// </summary>
public sealed class WallGeometryReader
{
    public bool TryRead(Wall wall, out WallGeometry geometry, out string error)
    {
        geometry = null!;
        error = string.Empty;

        if (wall.Location is not LocationCurve { Curve: Line line })
        {
            error = "Chi ho tro tuong thang (LocationCurve la Line).";
            return false;
        }

        var start = line.GetEndPoint(0);
        var end = line.GetEndPoint(1);
        var lengthFeet = start.DistanceTo(end);
        if (lengthFeet <= 1e-6)
        {
            error = "Tuong co chieu dai bang 0.";
            return false;
        }

        var dirAlong = (end - start).Normalize();
        var dirUp = XYZ.BasisZ;
        var dirThickness = dirAlong.CrossProduct(dirUp).Normalize();

        if (!TryReadProjectionExtents(wall, dirThickness, out var minThickness, out var maxThickness,
                out var minZ, out var maxZ))
        {
            error = "Khong doc duoc bien hinh hoc tuong.";
            return false;
        }

        var thicknessFeet = maxThickness - minThickness;
        if (thicknessFeet <= 1e-6)
        {
            error = "Khong doc duoc be day tuong.";
            return false;
        }

        var heightFeet = maxZ - minZ;
        if (heightFeet <= 1e-6)
        {
            error = "Khong doc duoc chieu cao tuong.";
            return false;
        }

        // Revit wall Location Line can be centerline, core face, or finish face. Do not assume it is centered.
        // Keep only its along-axis coordinate; derive face A and base Z from actual wall geometry.
        var origin = dirAlong * start.DotProduct(dirAlong)
                     + dirThickness * minThickness
                     + dirUp * minZ;

        geometry = new WallGeometry
        {
            Origin = origin,
            DirAlong = dirAlong,
            DirUp = dirUp,
            DirThickness = dirThickness,
            LengthFeet = lengthFeet,
            HeightFeet = heightFeet,
            ThicknessFeet = thicknessFeet
        };
        return true;
    }

    private static bool TryReadProjectionExtents(Wall wall, XYZ dirThickness,
        out double minThickness, out double maxThickness, out double minZ, out double maxZ)
    {
        minThickness = 0;
        maxThickness = 0;
        minZ = 0;
        maxZ = 0;

        var localMinThickness = double.PositiveInfinity;
        var localMaxThickness = double.NegativeInfinity;
        var localMinZ = double.PositiveInfinity;
        var localMaxZ = double.NegativeInfinity;

        foreach (var point in ReadSolidPoints(wall))
            Include(point);

        if (double.IsInfinity(localMinThickness))
        {
            var box = wall.get_BoundingBox(null);
            if (box is null) return false;

            foreach (var point in BoundingBoxCorners(box))
                Include(point);
        }

        minThickness = localMinThickness;
        maxThickness = localMaxThickness;
        minZ = localMinZ;
        maxZ = localMaxZ;

        return !double.IsInfinity(localMinThickness) &&
               localMaxThickness - localMinThickness > 1e-6 &&
               localMaxZ - localMinZ > 1e-6;

        void Include(XYZ point)
        {
            var thickness = point.DotProduct(dirThickness);
            localMinThickness = Math.Min(localMinThickness, thickness);
            localMaxThickness = Math.Max(localMaxThickness, thickness);
            localMinZ = Math.Min(localMinZ, point.Z);
            localMaxZ = Math.Max(localMaxZ, point.Z);
        }
    }

    private static IEnumerable<XYZ> ReadSolidPoints(Wall wall)
    {
        var geometry = wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Medium });
        if (geometry is null) yield break;

        foreach (var obj in geometry)
        {
            if (obj is Solid solid)
            {
                foreach (var point in ReadSolidPoints(solid))
                    yield return point;
            }
            else if (obj is GeometryInstance instance)
            {
                foreach (var nested in instance.GetInstanceGeometry())
                {
                    if (nested is not Solid nestedSolid) continue;
                    foreach (var point in ReadSolidPoints(nestedSolid))
                        yield return point;
                }
            }
        }
    }

    private static IEnumerable<XYZ> ReadSolidPoints(Solid solid)
    {
        if (solid.Volume <= 1e-9) yield break;

        foreach (Edge edge in solid.Edges)
        {
            var curve = edge.AsCurve();
            yield return curve.GetEndPoint(0);
            yield return curve.GetEndPoint(1);
        }
    }

    private static IEnumerable<XYZ> BoundingBoxCorners(BoundingBoxXYZ box)
    {
        yield return new XYZ(box.Min.X, box.Min.Y, box.Min.Z);
        yield return new XYZ(box.Min.X, box.Min.Y, box.Max.Z);
        yield return new XYZ(box.Min.X, box.Max.Y, box.Min.Z);
        yield return new XYZ(box.Min.X, box.Max.Y, box.Max.Z);
        yield return new XYZ(box.Max.X, box.Min.Y, box.Min.Z);
        yield return new XYZ(box.Max.X, box.Min.Y, box.Max.Z);
        yield return new XYZ(box.Max.X, box.Max.Y, box.Min.Z);
        yield return new XYZ(box.Max.X, box.Max.Y, box.Max.Z);
    }
}
