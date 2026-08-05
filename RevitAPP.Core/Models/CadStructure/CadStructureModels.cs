namespace RevitAPP.Core.Models.CadStructure;

public readonly record struct CadStructurePoint2(double X, double Y)
{
    public double DistanceTo(CadStructurePoint2 other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static CadStructurePoint2 operator +(CadStructurePoint2 a, CadStructurePoint2 b) =>
        new(a.X + b.X, a.Y + b.Y);

    public static CadStructurePoint2 operator -(CadStructurePoint2 a, CadStructurePoint2 b) =>
        new(a.X - b.X, a.Y - b.Y);

    public static CadStructurePoint2 operator *(CadStructurePoint2 point, double scale) =>
        new(point.X * scale, point.Y * scale);
}

public sealed record CadStructureSegment(
    int Id,
    CadStructurePoint2 Start,
    CadStructurePoint2 End,
    string Layer,
    string SourcePath,
    string? SourceText = null);

public sealed record CadStructureTransferPackage(
    int SchemaVersion,
    string SelectionId,
    DateTime CreatedUtc,
    string SourceDrawing,
    string AutoCadVersion,
    int InsUnits,
    CadStructurePoint2 SourceAnchor,
    IReadOnlyList<CadStructureSegment> Segments)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record CadColumnCandidate(
    int Id,
    IReadOnlyList<CadStructurePoint2> CornersMm,
    CadStructurePoint2 CenterMm,
    double WidthMm,
    double HeightMm,
    double AngleDegrees,
    string Layer,
    string SourcePath,
    string? SourceText,
    IReadOnlyList<int> SourceSegmentIds);

public sealed record CadStructureAnalysis(
    CadStructurePoint2 SourceOriginMm,
    CadStructurePoint2 SourceAnchorRelativeMm,
    IReadOnlyList<CadStructureSegment> GridSegmentsMm,
    IReadOnlyList<CadColumnCandidate> Columns,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public bool IsValid => GridSegmentsMm.Count > 0 || Columns.Count > 0;
}
