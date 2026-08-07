using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using RevitAPP.ViewModels;

namespace RevitAPP.Services.CadStructure;

internal static class CadSlabPreviewFactory
{
    public static CadSlabPreviewData? SelectAndBuild(
        CadStructureTransferPackage gridPackage,
        CadSlabAnalysisOptions options,
        out string? error)
    {
        var selection = AutoCadModelSelectionService.SelectSlab(gridPackage);
        if (!selection.IsValid)
        {
            error = selection.Error;
            return null;
        }

        var analysis = CadSlabAnalyzer.Analyze(selection.Package!, selection.Hatches, options);
        if (analysis.Error is not null)
        {
            error = analysis.Error;
            return null;
        }

        error = null;
        return new CadSlabPreviewData(selection.Package!, selection.Hatches, analysis);
    }

    /// <summary>
    /// Re-runs the analysis on geometry already scanned, so changing a setting does not require
    /// picking the drawing again.
    /// </summary>
    public static CadSlabPreviewData Rebuild(
        CadSlabPreviewData data,
        CadSlabAnalysisOptions options)
    {
        var analysis = CadSlabAnalyzer.Analyze(data.Package, data.Hatches, options);
        return data with { Analysis = analysis };
    }
}
