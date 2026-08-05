namespace RevitAPP.Core.Models.CadGrid;

public sealed record CadGridTransferLine(
    int Id,
    double StartX,
    double StartY,
    double EndX,
    double EndY);

public sealed record CadGridTransferPackage(
    int SchemaVersion,
    string SelectionId,
    DateTime CreatedUtc,
    string SourceDrawing,
    string AutoCadVersion,
    int InsUnits,
    IReadOnlyList<CadGridTransferLine> Lines)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record CadGridRelativeFamily(
    CadGridPoint2 Direction,
    IReadOnlyList<double> OffsetsMm,
    IReadOnlyList<int> OrderedSegmentIds);

public sealed record CadGridRelativeLayout(
    CadGridRelativeFamily FirstFamily,
    CadGridRelativeFamily SecondFamily,
    CadGridPoint2 SourceCorner,
    CadGridNetwork Network);

public sealed record CadGridRelativeLayoutResult(
    CadGridRelativeLayout? Layout,
    string? Error)
{
    public bool IsValid => Layout is not null && string.IsNullOrWhiteSpace(Error);
    public static CadGridRelativeLayoutResult Invalid(string error) => new(null, error);
    public static CadGridRelativeLayoutResult Valid(CadGridRelativeLayout layout) => new(layout, null);
}
