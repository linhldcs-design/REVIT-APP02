using Autodesk.Revit.DB;

namespace RevitAPP.Services.CadGrid;

internal sealed record CadGridPlannedGrid(
    Line Curve,
    double OffsetMm,
    string SourceAnchorName,
    string? Name);
