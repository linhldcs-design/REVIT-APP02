namespace RevitAPP.Core.Models.DwgExport;

public enum DwgFileVersion
{
    R2007,
    R2010,
    R2013,
    R2018
}

public enum DwgDrawingUnit
{
    Millimetres,
    Centimetres,
    Metres,
    Inches,
    Feet
}

public sealed record DwgViewportPlan(
    long ViewportId,
    long ViewId,
    string ViewName,
    int ScaleDenominator,
    double SheetCenterXFeet,
    double SheetCenterYFeet,
    int Rotation,
    double SheetMinXFeet = 0,
    double SheetMinYFeet = 0,
    double SheetMaxXFeet = 0,
    double SheetMaxYFeet = 0,
    int SourceDimensionCount = 0);

public sealed record DwgSheetPlan(
    int Ordinal,
    long SheetId,
    string SheetNumber,
    string SheetName,
    string StagedFileName,
    IReadOnlyList<DwgViewportPlan> Viewports,
    double SheetMinXFeet = 0,
    double SheetMinYFeet = 0,
    double SheetMaxXFeet = 0,
    double SheetMaxYFeet = 0);

public readonly record struct DwgViewportScaleRegion(
    long ViewportId,
    int ScaleDenominator,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    double GeometryFactor,
    double DimensionLinearFactor);

public sealed record DwgExportJob(
    int SchemaVersion,
    string JobId,
    DateTime CreatedUtc,
    string SourceDocument,
    string ExportSetupName,
    DwgFileVersion FileVersion,
    DwgDrawingUnit DrawingUnit,
    string StagingDirectory,
    string RequestedOutputPath,
    double SheetGapMillimetres,
    IReadOnlyList<DwgSheetPlan> Sheets)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record DwgPostProcessSheetResult(
    int Ordinal,
    string SheetNumber,
    int EntityCount,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY);

public sealed record DwgPostProcessResult(
    int SchemaVersion,
    string JobId,
    bool Succeeded,
    string? TemporaryOutputPath,
    string? Error,
    IReadOnlyList<DwgPostProcessSheetResult> Sheets)
{
    public const int CurrentSchemaVersion = 1;
}

public readonly record struct DwgSheetExtents(
    int Ordinal,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

public readonly record struct DwgSheetPlacement(
    int Ordinal,
    double DisplacementX,
    double DisplacementY);
