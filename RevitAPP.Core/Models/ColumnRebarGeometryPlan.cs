namespace RevitAPP.Core.Models;

/// <summary>A 3D point in the column preview coordinate system. All values are millimetres.</summary>
public readonly record struct GeometryPoint3D(double Xmm, double Ymm, double Zmm);

/// <summary>Pure stack location extracted from Revit. XY is the column centre in one shared coordinate system.</summary>
public sealed record ColumnRebarStackContext(
    ColumnStorey Storey,
    double CenterXmm,
    double CenterYmm,
    double RotationRad = 0);

public enum ColumnRebarPathKind
{
    MainBar,
    DistributionBar,
    Stirrup,
    Crosstie,
    FoundationStarter
}

public enum ColumnRebarContextKind
{
    Column,
    Beam,
    Foundation
}

/// <summary>A Revit-ready bar centreline. Consecutive points form the straight curve segments used by the builder.</summary>
public sealed record ColumnRebarPath(
    int StoreyIndex,
    string LevelName,
    ColumnRebarPathKind Kind,
    double DiameterMm,
    IReadOnlyList<GeometryPoint3D> Points,
    string? Zone = null,
    IReadOnlyList<GeometryPoint3D>? FallbackPoints = null,
    bool UsesDistributionBarType = false);

/// <summary>Neutral context volume for preview. Width/height follow the storey's rotated local X/Y axes.</summary>
public sealed record ColumnRebarContextVolume(
    int StoreyIndex,
    ColumnRebarContextKind Kind,
    double CenterXmm,
    double CenterYmm,
    double RotationRad,
    double BaseElevationMm,
    double TopElevationMm,
    double WidthMm,
    double HeightMm);

/// <summary>
/// Complete, Revit-independent description of a column stack. Paths are explicit so renderers never need to
/// reinterpret spacing, lap, crank, transition or starter settings.
/// </summary>
public sealed record ColumnRebarGeometryPlan(
    IReadOnlyList<ColumnRebarStackContext> Stack,
    IReadOnlyList<ColumnRebarContextVolume> Context,
    IReadOnlyList<ColumnRebarPath> Paths)
{
    /// <summary>Ordered level metadata for 2D level lines/labels and 3D level planes.</summary>
    public IEnumerable<ColumnStorey> Storeys => Stack.Select(s => s.Storey);

    public IEnumerable<ColumnRebarPath> MainBars => Paths.Where(p =>
        p.Kind is ColumnRebarPathKind.MainBar or ColumnRebarPathKind.DistributionBar);

    public IEnumerable<ColumnRebarPath> Stirrups => Paths.Where(p =>
        p.Kind is ColumnRebarPathKind.Stirrup or ColumnRebarPathKind.Crosstie);

    public IEnumerable<ColumnRebarPath> Starters => Paths.Where(p =>
        p.Kind == ColumnRebarPathKind.FoundationStarter);
}
