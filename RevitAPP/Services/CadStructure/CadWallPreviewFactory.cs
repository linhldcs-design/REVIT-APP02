using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using RevitAPP.ViewModels;

namespace RevitAPP.Services.CadStructure;

internal static class CadWallPreviewFactory
{
    public static CadWallPreviewData? SelectAndBuild(
        CadStructureTransferPackage gridPackage,
        CadWallAnalysisOptions options,
        out string? error)
    {
        var selection = AutoCadModelSelectionService.SelectWall(gridPackage);
        if (!selection.IsValid)
        {
            error = selection.Error;
            return null;
        }

        var analysis = CadWallAnalyzer.Analyze(selection.Package!, options);
        if (analysis.Error is not null)
        {
            error = analysis.Error;
            return null;
        }

        error = null;
        return new CadWallPreviewData(selection.Package!, analysis);
    }

    /// <summary>
    /// Re-runs the analysis on geometry already scanned, so picking a layer or changing a
    /// thickness limit does not require picking the drawing again.
    /// </summary>
    public static CadWallPreviewData Rebuild(
        CadWallPreviewData data,
        CadWallAnalysisOptions options)
    {
        var analysis = CadWallAnalyzer.Analyze(data.Package, options);
        return analysis.Error is not null ? data : data with { Analysis = analysis };
    }
}
