using Autodesk.Revit.DB;
using RevitAPP.Core.Models.DwgExport;

namespace RevitAPP.Services.DwgExport;

public sealed record DwgExportCatalogResult(
    IReadOnlyList<DwgSetupOption> Setups,
    IReadOnlyList<DwgVersionOption> Versions,
    IReadOnlyList<PrintSetOption> PrintSets);

public static class DwgExportCatalog
{
    public static DwgExportCatalogResult Load(Document document)
    {
        var setups = ExportDWGSettings.ListNames(document)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name =>
            {
                var options = DWGExportOptions.GetPredefinedOptions(document, name);
                return new DwgSetupOption(name, MapVersion(options?.FileVersion ?? ACADVersion.R2018));
            })
            .ToArray();

        var versions = new[]
        {
            new DwgVersionOption(DwgFileVersion.R2007, "AutoCAD 2007"),
            new DwgVersionOption(DwgFileVersion.R2010, "AutoCAD 2010"),
            new DwgVersionOption(DwgFileVersion.R2013, "AutoCAD 2013"),
            new DwgVersionOption(DwgFileVersion.R2018, "AutoCAD 2018")
        };

        var printSets = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheetSet))
            .Cast<ViewSheetSet>()
            .Select(set => ToOption(document, set))
            .OrderBy(set => set.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new DwgExportCatalogResult(setups, versions, printSets);
    }

    private static PrintSetOption ToOption(Document document, ViewSheetSet set)
    {
#if REVIT2023_OR_GREATER
        var views = set.OrderedViewList;
#else
        var views = set.Views.Cast<View>().OrderBy(view => view.Name).ToList();
#endif
        var sheets = new List<DwgSheetPreviewItem>();
        var ordinal = 0;
        foreach (var view in views)
        {
            if (view is not ViewSheet sheet)
            {
                sheets.Add(new DwgSheetPreviewItem(
                    ordinal++, view.Id.ToValue(), "—", view.Name, "—", false, false,
                    "Print Set chứa view rời; v1 chỉ hỗ trợ sheet."));
                continue;
            }

            var scales = sheet.GetAllViewports()
                .Select(id => document.GetElement(id) as Viewport)
                .Where(viewport => viewport is not null)
                .Select(viewport => document.GetElement(viewport!.ViewId) as View)
                .Where(item => item is not null)
                .Select(item => item!.Scale)
                .Where(scale => scale > 0)
                .Distinct()
                .OrderBy(scale => scale)
                .ToArray();
            var valid = !sheet.IsPlaceholder && sheet.CanBePrinted;
            var status = valid
                ? scales.Length > 1 ? "Nhiều tỷ lệ" : "Sẵn sàng"
                : sheet.IsPlaceholder ? "Sheet placeholder" : "Không thể in/xuất";
            sheets.Add(new DwgSheetPreviewItem(
                ordinal++,
                sheet.Id.ToValue(),
                sheet.SheetNumber,
                sheet.Name,
                scales.Length == 0 ? "Không có viewport" : string.Join(", ", scales.Select(scale => $"1:{scale}")),
                scales.Length > 1,
                valid,
                status));
        }

        return new PrintSetOption(set.Id.ToValue(), set.Name, sheets);
    }

    public static ACADVersion MapVersion(DwgFileVersion version) => version switch
    {
        DwgFileVersion.R2007 => ACADVersion.R2007,
        DwgFileVersion.R2010 => ACADVersion.R2010,
        DwgFileVersion.R2013 => ACADVersion.R2013,
        DwgFileVersion.R2018 => ACADVersion.R2018,
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };

    private static DwgFileVersion MapVersion(ACADVersion version) => version switch
    {
        ACADVersion.R2007 => DwgFileVersion.R2007,
        ACADVersion.R2010 => DwgFileVersion.R2010,
        ACADVersion.R2013 => DwgFileVersion.R2013,
        _ => DwgFileVersion.R2018
    };
}
