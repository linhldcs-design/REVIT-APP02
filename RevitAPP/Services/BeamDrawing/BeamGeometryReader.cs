using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Đọc hình học dầm thẳng thành <see cref="BeamGeometry"/> thuần (feet): 2 đầu location line,
///     tiết diện (b,h), cao độ mặt trên/dưới. Cao độ ưu tiên parameter kết cấu Top/Bottom;
///     bounding box chỉ là fallback vì có thể chứa control/symbolic geometry. v1 chỉ hỗ trợ dầm thẳng.
/// </summary>
public sealed class BeamGeometryReader
{
    public bool TryRead(Document document, FamilyInstance beam, out BeamGeometry geometry, out string error)
    {
        geometry = null!;
        error = string.Empty;

        if (beam.Location is not LocationCurve { Curve: Line line })
        {
            error = $"Dầm '{beam.Id}' không có trục thẳng — v1 chỉ hỗ trợ dầm thẳng.";
            return false;
        }

        if (!TryReadDimensions(beam, out var widthFeet, out var heightFeet))
        {
            error = $"Không đọc được tiết diện (b,h) của dầm '{beam.Id}'.";
            return false;
        }

        var (topZ, bottomZ) = ReadElevations(beam, line, heightFeet);

        var p0 = line.GetEndPoint(0);
        var p1 = line.GetEndPoint(1);
        geometry = new BeamGeometry(
            Start: new Point3(p0.X, p0.Y, p0.Z),
            End: new Point3(p1.X, p1.Y, p1.Z),
            WidthFeet: widthFeet,
            HeightFeet: heightFeet,
            TopZFeet: topZ,
            BottomZFeet: bottomZ);
        return true;
    }

    private static bool TryReadDimensions(FamilyInstance beam, out double widthFeet, out double heightFeet)
    {
        widthFeet = heightFeet = 0;
        var symbol = beam.Symbol;

        widthFeet = ReadFirstNonZero(symbol,
            BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH, "b", "Width", "Chiều rộng");
        heightFeet = ReadFirstNonZero(symbol,
            BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT, "h", "Height", "Chiều cao", "Depth");

        if (widthFeet > 0 && heightFeet > 0) return true;

        // Fallback: bounding box theo trục thế giới.
        var bbox = beam.get_BoundingBox(null);
        if (bbox == null) return false;
        heightFeet = bbox.Max.Z - bbox.Min.Z;
        widthFeet = Math.Min(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y);
        return widthFeet > 0 && heightFeet > 0;
    }

    private static double ReadFirstNonZero(Element symbol, BuiltInParameter builtIn, params string[] names)
    {
        var p = symbol.get_Parameter(builtIn);
        if (p is { StorageType: StorageType.Double } && p.AsDouble() > 0) return p.AsDouble();

        foreach (var name in names)
        {
            var named = symbol.LookupParameter(name);
            if (named is { StorageType: StorageType.Double } && named.AsDouble() > 0) return named.AsDouble();
        }

        return 0;
    }

    /// <summary>
    ///     Cao độ mặt trên/dưới. Ưu tiên STRUCTURAL_ELEVATION_AT_TOP/BOTTOM; bbox chỉ fallback cuối
    ///     vì family control geometry có thể nhô ra ngoài bê tông thật.
    /// </summary>
    private static (double TopZFeet, double BottomZFeet) ReadElevations(FamilyInstance beam, Line line, double heightFeet)
    {
        var axisZ = Math.Min(line.GetEndPoint(0).Z, line.GetEndPoint(1).Z);
        var top = ReadElevationParameter(beam, BuiltInParameter.STRUCTURAL_ELEVATION_AT_TOP);
        var bottom = ReadElevationParameter(beam, BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM);
        var bbox = beam.get_BoundingBox(null);
        var resolved = BeamElevationMath.Resolve(
            top, bottom, heightFeet,
            bbox?.Max.Z, bbox?.Min.Z, axisZ);
        return (resolved.Top, resolved.Bottom);
    }

    private static double? ReadElevationParameter(Element beam, BuiltInParameter parameter)
    {
        var value = beam.get_Parameter(parameter);
        return value is { StorageType: StorageType.Double } ? value.AsDouble() : null;
    }
}
