using System.IO;
using Autodesk.Revit.DB;
using RevitAPP.Core.Models.DwgExport;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.DwgExport;

public sealed class RevitDwgExportService
{
    public string Export(Document document, DwgExportRequest request)
    {
        if (!DwgOutputPathPolicy.TryValidateOutputPath(request.OutputPath, out var pathError))
            throw new InvalidOperationException(pathError);
        var predefined = DWGExportOptions.GetPredefinedOptions(document, request.SetupName)
            ?? throw new InvalidOperationException($"Không tìm thấy Export DWG Setup '{request.SetupName}'.");
        var options = new DWGExportOptions(predefined)
        {
            FileVersion = DwgExportCatalog.MapVersion(request.FileVersion),
            MergedViews = true
        };

        var staging = DwgExportJobStore.CreateStagingDirectory();
        var jobId = Guid.NewGuid().ToString("N");
        try
        {
            var sheetPlans = new List<DwgSheetPlan>();
            foreach (var preview in request.PrintSet.Sheets.OrderBy(sheet => sheet.Ordinal))
            {
                if (document.GetElement(ElementIdHelper.Create(preview.SheetId)) is not ViewSheet sheet)
                    throw new InvalidOperationException($"Không tìm thấy sheet '{preview.SheetNumber}'.");
                var stagedName = $"sheet-{preview.Ordinal:0000}.dwg";
                ExportSheet(document, sheet, options, staging, stagedName, preview.Ordinal);
                sheetPlans.Add(ToPlan(document, sheet, preview.Ordinal, stagedName));
            }

            var job = new DwgExportJob(
                DwgExportJob.CurrentSchemaVersion,
                jobId,
                DateTime.UtcNow,
                document.PathName ?? document.Title,
                request.SetupName,
                request.FileVersion,
                MapUnit(options.TargetUnit),
                staging,
                request.OutputPath,
                100,
                sheetPlans);
            var manifest = Path.Combine(staging, "job.json");
            DwgExportJobStore.WriteJobAtomic(job, manifest);
            var output = DwgPostProcessWorkerRunner.Run(job, manifest);
            Directory.Delete(staging, true);
            return output;
        }
        catch
        {
            // Retain staging for diagnosis; the exception includes the job path in logs.
            Serilog.Log.Error("DWG export job {JobId} failed. Staging retained at {Staging}", jobId, staging);
            throw;
        }
    }

    private static void ExportSheet(
        Document document,
        ViewSheet sheet,
        DWGExportOptions options,
        string staging,
        string stagedName,
        int ordinal)
    {
        var folder = Path.Combine(staging, $"export-{ordinal:0000}");
        Directory.CreateDirectory(folder);
        var succeeded = document.Export(folder, $"sheet-{ordinal:0000}", new[] { sheet.Id }, options);
        if (!succeeded) throw new InvalidOperationException($"Revit không xuất được sheet {sheet.SheetNumber}.");
        var files = Directory.GetFiles(folder, "*.dwg", SearchOption.TopDirectoryOnly);
        if (files.Length != 1)
            throw new InvalidOperationException(
                $"Sheet {sheet.SheetNumber} tạo {files.Length} DWG chính; yêu cầu đúng một file staging khi MergedViews bật.");
        File.Move(files[0], Path.Combine(staging, stagedName));
        Directory.Delete(folder, true);
    }

    private static DwgSheetPlan ToPlan(Document document, ViewSheet sheet, int ordinal, string stagedName)
    {
        var viewports = sheet.GetAllViewports()
            .Select(id => document.GetElement(id) as Viewport)
            .Where(viewport => viewport is not null)
            .Select(viewport =>
            {
                var view = (View)document.GetElement(viewport!.ViewId);
                var center = viewport.GetBoxCenter();
                var outline = viewport.GetBoxOutline();
                return new DwgViewportPlan(
                    viewport.Id.ToValue(),
                    view.Id.ToValue(),
                    view.Name,
                    view.Scale,
                    center.X,
                    center.Y,
                    (int)viewport.Rotation,
                    outline.MinimumPoint.X,
                    outline.MinimumPoint.Y,
                    outline.MaximumPoint.X,
                    outline.MaximumPoint.Y,
                    new FilteredElementCollector(document, view.Id)
                        .OfCategory(BuiltInCategory.OST_Dimensions)
                        .WhereElementIsNotElementType()
                        .Cast<Dimension>()
                        .Count(dimension => dimension.DimensionShape is
                            DimensionShape.Linear or
                            DimensionShape.Diameter or
                            DimensionShape.ArcLength or
                            DimensionShape.Radial));
            })
            .ToArray();
        var sheetOutline = sheet.Outline;
        return new DwgSheetPlan(
            ordinal,
            sheet.Id.ToValue(),
            sheet.SheetNumber,
            sheet.Name,
            stagedName,
            viewports,
            sheetOutline.Min.U,
            sheetOutline.Min.V,
            sheetOutline.Max.U,
            sheetOutline.Max.V);
    }

    private static DwgDrawingUnit MapUnit(ExportUnit unit) => unit switch
    {
        ExportUnit.Meter => DwgDrawingUnit.Metres,
        ExportUnit.Centimeter => DwgDrawingUnit.Centimetres,
        ExportUnit.Inch => DwgDrawingUnit.Inches,
        ExportUnit.Foot => DwgDrawingUnit.Feet,
        _ => DwgDrawingUnit.Millimetres
    };
}
