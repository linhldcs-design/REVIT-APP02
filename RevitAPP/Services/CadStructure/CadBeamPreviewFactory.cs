using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using RevitAPP.ViewModels;

namespace RevitAPP.Services.CadStructure;

internal static class CadBeamPreviewFactory
{
    public static CadBeamPreviewData? SelectAndBuild(
        CadStructureTransferPackage gridPackage,
        CadBeamAnalysisOptions options,
        out string? error)
    {
        var selection = AutoCadModelSelectionService.SelectBeam(gridPackage);
        if (!selection.IsValid)
        {
            error = selection.Error;
            return null;
        }

        var analysis = CadBeamAnalyzer.Analyze(
            selection.Package!, gridPackage.Segments, options);
        if (analysis.Error is not null)
        {
            error = analysis.Error;
            return null;
        }

        error = null;
        return new CadBeamPreviewData(selection.Package!, analysis);
    }
}
