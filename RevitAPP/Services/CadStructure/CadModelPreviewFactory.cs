using RevitAPP.Core.Models.CadGrid;
using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using RevitAPP.ViewModels;

namespace RevitAPP.Services.CadStructure;

internal static class CadModelPreviewFactory
{
    public static CadModelPreviewData Empty()
    {
        var package = new CadStructureTransferPackage(
            CadStructureTransferPackage.CurrentSchemaVersion,
            string.Empty,
            DateTime.UtcNow,
            string.Empty,
            string.Empty,
            4,
            default,
            Array.Empty<CadStructureSegment>());
        var analysis = new CadStructureAnalysis(
            default,
            default,
            Array.Empty<CadStructureSegment>(),
            Array.Empty<CadColumnCandidate>(),
            Array.Empty<string>(),
            null);
        var gridPreview = new CadGridPreview(
            Array.Empty<CadGridPreviewAxis>(),
            Array.Empty<int>(),
            null);
        return new CadModelPreviewData(
            package,
            analysis,
            gridPreview,
            default,
            Array.Empty<CadColumnCandidate>());
    }

    public static CadModelPreviewData? Build(CadStructureTransferPackage package, out string? error)
    {
        var analysis = CadStructureAnalyzer.Analyze(package);
        if (!analysis.IsValid)
        {
            error = analysis.Error ?? "Không đọc được hình học CAD.";
            return null;
        }

        var gridOrigin = analysis.GridSegmentsMm.Count == 0
            ? new CadStructurePoint2(0, 0)
            : new CadStructurePoint2(
                analysis.GridSegmentsMm.Min(segment => Math.Min(segment.Start.X, segment.End.X)),
                analysis.GridSegmentsMm.Min(segment => Math.Min(segment.Start.Y, segment.End.Y)));

        var gridLines = analysis.GridSegmentsMm.Select(segment => new CadGridTransferLine(
            segment.Id,
            segment.Start.X - gridOrigin.X,
            segment.Start.Y - gridOrigin.Y,
            segment.End.X - gridOrigin.X,
            segment.End.Y - gridOrigin.Y)).ToArray();

        CadGridPreview gridPreview;
        var effectiveAnalysis = analysis;
        if (gridLines.Length == 0)
        {
            gridPreview = new CadGridPreview(
                Array.Empty<CadGridPreviewAxis>(), Array.Empty<int>(), null);
        }
        else
        {
            var gridPackage = new CadGridTransferPackage(
                CadGridTransferPackage.CurrentSchemaVersion,
                package.SelectionId,
                package.CreatedUtc,
                package.SourceDrawing,
                package.AutoCadVersion,
                4,
                gridLines);
            gridPreview = CadGridPreviewBuilder.Build(gridPackage);
            if (!gridPreview.IsValid)
            {
                if (analysis.Columns.Count == 0)
                {
                    error = gridPreview.Error;
                    return null;
                }

                var warning = gridPreview.Error ?? "Cac line con lai khong tao duoc Grid preview.";
                effectiveAnalysis = analysis with
                {
                    Warnings = analysis.Warnings.Append(warning).ToArray()
                };
                gridPreview = new CadGridPreview(
                    Array.Empty<CadGridPreviewAxis>(), Array.Empty<int>(), null);
            }
        }

        var shiftedColumns = analysis.Columns.Select(column => column with
        {
            CenterMm = column.CenterMm - gridOrigin,
            CornersMm = column.CornersMm.Select(point => point - gridOrigin).ToArray()
        }).ToArray();
        var anchor = analysis.SourceAnchorRelativeMm - gridOrigin;

        error = null;
        return new CadModelPreviewData(package, effectiveAnalysis, gridPreview, anchor, shiftedColumns);
    }
}
