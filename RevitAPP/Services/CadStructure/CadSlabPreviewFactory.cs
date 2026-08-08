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
    /// Picks the marks that say which bays stay open. Returns the geometry in drawing units so the
    /// analyzer converts it the same way it converts the rest of the scan.
    /// </summary>
    public static IReadOnlyList<CadStructureSegment>? SelectOpeningOutlines(
        CadStructureTransferPackage slabPackage,
        out string? error)
    {
        var selection = AutoCadModelSelectionService.SelectOpeningOutlines(slabPackage);
        if (!selection.IsValid)
        {
            error = selection.Error;
            return null;
        }
        error = null;
        return selection.Package!.Segments;
    }

    /// <summary>
    /// The shaded areas the user picks, replacing the ones the slab window happened to cover.
    /// </summary>
    public static IReadOnlyList<CadHatchRegion>? SelectHatchRegions(
        CadStructureTransferPackage slabPackage,
        out string? error)
    {
        var selection = AutoCadModelSelectionService.SelectHatchRegions(slabPackage);
        if (!selection.IsValid)
        {
            error = selection.Error;
            return null;
        }
        error = null;
        return selection.Hatches;
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
