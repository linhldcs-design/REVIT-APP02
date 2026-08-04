using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using RevitAPP.Core.Models.DwgExport;
using RevitAPP.Core.Services;
using Serilog;

namespace RevitAPP.Services.DwgExport;

/// <summary>
/// Runs full AutoCAD through Windows Automation. EXPORTLAYOUT is not available reliably
/// in Core Console, so the flatten step must execute in a dedicated full AutoCAD instance.
/// No AutoCAD managed assembly is loaded into the Revit process.
/// </summary>
public static class AutoCadDwgPostProcessor
{
    public const string OwnedProcessLeaseFileName = "autocad-worker.lease";
    public const string ProgressFileName = "worker-progress.txt";

    private const string AutoCad2024ProgId = "AutoCAD.Application.24.3";

    public static bool IsAvailable() => Type.GetTypeFromProgID(AutoCad2024ProgId, false) is not null;

    public static string Compose(DwgExportJob job, TimeSpan? commandTimeout = null)
    {
        DwgExportJobStore.ValidateJob(job);
        // A cold full-AutoCAD startup can take more than three minutes when another
        // AutoCAD release is already active. Keep a per-command ceiling, but allow
        // enough time for the first EXPORTLAYOUT to finish and stabilize its DWG.
        var timeout = commandTimeout ?? TimeSpan.FromMinutes(10);
        object? application = null;
        var openedDocuments = new List<object>();
        var ownsApplication = false;
        var applicationProcessId = 0;
        var applicationStartTicks = 0L;
        try
        {
            (application, applicationProcessId, applicationStartTicks) = CreateApplication();
            ownsApplication = true;
            WriteOwnedProcessLease(job.StagingDirectory, applicationProcessId, applicationStartTicks);
            Set(application, "Visible", false);
            var documents = Get(application, "Documents")
                ?? throw new InvalidOperationException("Không truy cập được AutoCAD Documents.");

            var flattened = new List<string>();
            var orderedSheets = job.Sheets.OrderBy(sheet => sheet.Ordinal).ToArray();
            for (var sheetIndex = 0; sheetIndex < orderedSheets.Length; sheetIndex++)
            {
                var sheet = orderedSheets[sheetIndex];
                WriteProgress(job.StagingDirectory, sheetIndex, orderedSheets.Length, sheet.SheetNumber, "flattening");
                var source = Path.Combine(job.StagingDirectory, sheet.StagedFileName);
                var flat = Path.Combine(
                    job.StagingDirectory,
                    $"flat-{job.JobId}-{sheet.Ordinal:0000}.dwg");
                FlattenLayout(
                    documents,
                    applicationProcessId,
                    source,
                    flat,
                    sheet,
                    job.DrawingUnit,
                    timeout,
                    openedDocuments);
                flattened.Add(flat);
                WriteProgress(job.StagingDirectory, sheetIndex + 1, orderedSheets.Length, sheet.SheetNumber, "flattened");
            }

            if (flattened.Count == 0)
                throw new InvalidOperationException("Job không có sheet để ghép.");

            var finalDocument = Call(documents, "Add")
                ?? throw new InvalidOperationException("Không tạo được DWG tổng trong AutoCAD.");
            openedDocuments.Add(finalDocument);
            WriteProgress(job.StagingDirectory, orderedSheets.Length, orderedSheets.Length, string.Empty, "composing");
            ComposeInDocument(finalDocument, flattened, job, timeout);

            var outputDirectory = Path.GetDirectoryName(job.RequestedOutputPath)
                ?? throw new InvalidOperationException("Đường dẫn output không hợp lệ.");
            Directory.CreateDirectory(outputDirectory);
            var temporaryOutput = Path.Combine(
                outputDirectory,
                $".{Path.GetFileNameWithoutExtension(job.RequestedOutputPath)}.{job.JobId}.partial.dwg");
            if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
            Call(finalDocument, "SaveAs", temporaryOutput, SaveAsType(job.FileVersion));
            Call(finalDocument, "Close", false);
            openedDocuments.Remove(finalDocument);
            Release(finalDocument);
            WaitForStableFile(temporaryOutput, timeout);
            PublishAtomic(temporaryOutput, job.RequestedOutputPath);
            WriteProgress(job.StagingDirectory, orderedSheets.Length, orderedSheets.Length, string.Empty, "completed");
            return job.RequestedOutputPath;
        }
        finally
        {
            foreach (var document in openedDocuments.Distinct().Reverse())
            {
                try { Call(document, "Close", false); }
                catch (Exception exception) { Log.Debug(exception, "Could not close AutoCAD staging document"); }
                Release(document);
            }
            if (application is not null && ownsApplication)
            {
                try { Call(application, "Quit"); }
                catch (Exception exception) { Log.Debug(exception, "Could not quit AutoCAD automation instance"); }
            }
            if (application is not null) Release(application);
            if (ownsApplication && applicationProcessId > 0)
                EnsureOwnedApplicationExited(applicationProcessId, applicationStartTicks);
            TryDelete(Path.Combine(job.StagingDirectory, OwnedProcessLeaseFileName));
        }
    }

    private static void WriteOwnedProcessLease(string stagingDirectory, int processId, long startTicks)
    {
        WriteAtomicText(
            Path.Combine(stagingDirectory, OwnedProcessLeaseFileName),
            $"{processId}|{startTicks}");
    }

    private static void WriteProgress(
        string stagingDirectory,
        int completed,
        int total,
        string sheetNumber,
        string stage) =>
        WriteAtomicText(
            Path.Combine(stagingDirectory, ProgressFileName),
            $"{DateTime.UtcNow:O}|{completed}|{total}|{stage}|{sheetNumber}");

    private static void WriteAtomicText(string path, string contents)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) { Log.Debug(exception, "Could not remove worker marker {Path}", path); }
    }

    private static void EnsureOwnedApplicationExited(int processId, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!MatchesOwnedAutoCad(process, startTicks)) return;
            if (process.WaitForExit(10_000)) return;
            if (!MatchesOwnedAutoCad(process, startTicks)) return;
            process.Kill();
            if (!process.WaitForExit(5_000))
                Log.Warning("Owned AutoCAD process {ProcessId} did not exit after termination request", processId);
        }
        catch (ArgumentException)
        {
            // The dedicated process already exited.
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not confirm exit of owned AutoCAD process {ProcessId}", processId);
        }
    }

    private static bool MatchesOwnedAutoCad(Process process, long startTicks) =>
        string.Equals(process.ProcessName, "acad", StringComparison.OrdinalIgnoreCase)
        && process.StartTime.ToUniversalTime().Ticks == startTicks;

    private static (object Application, int ProcessId, long StartTicks) CreateApplication()
    {
        var existingProcesses = Process.GetProcessesByName("acad");
        HashSet<int> existingProcessIds;
        try { existingProcessIds = existingProcesses.Select(process => process.Id).ToHashSet(); }
        finally
        {
            foreach (var process in existingProcesses) process.Dispose();
        }
        Exception? last = null;
        var type = Type.GetTypeFromProgID(AutoCad2024ProgId, false)
                   ?? throw new InvalidOperationException("Máy chưa cài AutoCAD 2024 Automation.");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            object? application = null;
            try
            {
                application = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException($"Không khởi động được {AutoCad2024ProgId}.");
                var processId = GetApplicationProcessId(application);
                if (processId <= 0)
                {
                    var newProcesses = Process.GetProcessesByName("acad")
                        .Where(process => !existingProcessIds.Contains(process.Id))
                        .ToArray();
                    try
                    {
                        if (newProcesses.Length == 1) processId = newProcesses[0].Id;
                    }
                    finally
                    {
                        foreach (var candidate in newProcesses) candidate.Dispose();
                    }
                }
                if (processId <= 0 || existingProcessIds.Contains(processId))
                {
                    throw new InvalidOperationException(
                        $"Không xác nhận được phiên {AutoCad2024ProgId} là AutoCAD riêng do RevitAPP tạo. " +
                        "Đã dừng để không ảnh hưởng bản vẽ AutoCAD đang mở.");
                }
                using var process = Process.GetProcessById(processId);
                if (!string.Equals(process.ProcessName, "acad", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Process Automation không phải AutoCAD.");
                return (application, processId, process.StartTime.ToUniversalTime().Ticks);
            }
            catch (COMException exception) when (exception.HResult == unchecked((int)0x80010001))
            {
                last = exception;
                CleanupFailedApplication(application, existingProcessIds);
                System.Threading.Thread.Sleep(500);
            }
            catch
            {
                CleanupFailedApplication(application, existingProcessIds);
                throw;
            }
        }
        throw new InvalidOperationException("Không khởi động được AutoCAD Automation.", last);
    }

    private static void CleanupFailedApplication(object? application, ISet<int> existingProcessIds)
    {
        if (application is null) return;
        var processId = GetApplicationProcessId(application);
        long startTicks = 0;
        if (processId <= 0)
        {
            var newProcesses = Process.GetProcessesByName("acad")
                .Where(process => !existingProcessIds.Contains(process.Id))
                .ToArray();
            try
            {
                if (newProcesses.Length == 1)
                {
                    processId = newProcesses[0].Id;
                    startTicks = newProcesses[0].StartTime.ToUniversalTime().Ticks;
                }
            }
            finally
            {
                foreach (var candidate in newProcesses) candidate.Dispose();
            }
        }
        var confirmedNewProcess = processId > 0 && !existingProcessIds.Contains(processId);
        if (!confirmedNewProcess)
        {
            Release(application);
            return;
        }
        if (startTicks == 0)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (string.Equals(process.ProcessName, "acad", StringComparison.OrdinalIgnoreCase))
                    startTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (ArgumentException) { }
        }
        try { Call(application, "Quit"); }
        catch (Exception exception) { Log.Warning(exception, "Could not quit failed owned AutoCAD instance"); }
        finally { Release(application); }
        if (startTicks > 0) EnsureOwnedApplicationExited(processId, startTicks);
    }

    private static int GetApplicationProcessId(object application)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var handle = Safe(() => new IntPtr(Convert.ToInt64(Get(application, "HWND"))));
            if (handle != IntPtr.Zero)
            {
                GetWindowThreadProcessId(handle, out var processId);
                if (processId != 0) return unchecked((int)processId);
            }
            System.Threading.Thread.Sleep(250);
        }
        return 0;
    }

    private static void FlattenLayout(
        object documents,
        int applicationProcessId,
        string sourcePath,
        string outputPath,
        DwgSheetPlan sheet,
        DwgDrawingUnit drawingUnit,
        TimeSpan timeout,
        ICollection<object> openedDocuments)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Thiếu DWG staging.", sourcePath);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var document = OpenDocument(documents, sourcePath);
        openedDocuments.Add(document);
        var layouts = Get(document, "Layouts")
            ?? throw new InvalidOperationException("DWG không có collection Layouts.");
        var layout = WaitForPaperLayout(layouts, sourcePath, timeout);
        var fileDialog = Call(document, "GetVariable", "FILEDIA");
        var commandDialog = Call(document, "GetVariable", "CMDDIA");
        var backgroundPlot = Call(document, "GetVariable", "BACKGROUNDPLOT");
        try
        {
            Set(document, "ActiveLayout", layout);
            Call(document, "SetVariable", "FILEDIA", 0);
            Call(document, "SetVariable", "CMDDIA", 0);
            Call(document, "SetVariable", "BACKGROUNDPLOT", 0);

            var safePath = outputPath.Replace("\"", string.Empty);
            ExecuteExportLayout(
                document,
                $"_.EXPORTLAYOUT\r\"{safePath}\"\r",
                outputPath,
                applicationProcessId,
                timeout);
            if (sheet.Viewports.Count > 0)
            {
                NormalizeAndCloseGeneratedDocument(
                    documents,
                    outputPath,
                    sheet,
                    drawingUnit,
                    openedDocuments);
            }
        }
        finally
        {
            TrySetVariable(document, "FILEDIA", fileDialog);
            TrySetVariable(document, "CMDDIA", commandDialog);
            TrySetVariable(document, "BACKGROUNDPLOT", backgroundPlot);
            Release(layout);
            Release(layouts);
        }
        Call(document, "Close", false);
        openedDocuments.Remove(document);
        Release(document);
    }

    private static void NormalizeAndCloseGeneratedDocument(
        object documents,
        string outputPath,
        DwgSheetPlan sheet,
        DwgDrawingUnit drawingUnit,
        ICollection<object> openedDocuments)
    {
        var candidate = OpenDocument(documents, outputPath);
        openedDocuments.Add(candidate);
        var closed = false;
        try
        {
            NormalizeViewportDimensions(candidate, sheet, drawingUnit);
            Call(candidate, "Save");
            Call(candidate, "Close", false);
            openedDocuments.Remove(candidate);
            closed = true;
        }
        finally
        {
            // Keep the RCW alive for the outer cleanup if normalize/save/close fails.
            if (closed) Release(candidate);
        }
    }

    private static void NormalizeViewportDimensions(
        object document,
        DwgSheetPlan sheet,
        DwgDrawingUnit drawingUnit)
    {
        var modelSpace = Get(document, "ModelSpace")
            ?? throw new InvalidOperationException("Không truy cập được Model Space sau EXPORTLAYOUT.");
        var entities = Enumerate(modelSpace).ToArray();
        try
        {
            // Revit DWG export preserves paper-space coordinates after converting internal
            // feet to the configured target unit. Printer media may be A4 even when the
            // exported title block is A2, so GetPaperSize is not a geometry source.
            var regions = DwgViewportScaleRegionPlanner.MapToDrawingUnits(sheet, drawingUnit);
            // EXPORTLAYOUT already creates the paper-layout visual representation in Model Space,
            // including the relative geometry factor between viewport scales. Scaling again here
            // would apply the factor twice (1:25 vs 1:75 would become 9x instead of 3x).
            var normalizedByViewport = regions.ToDictionary(region => region.ViewportId, _ => 0);
            var candidatesByViewport = regions.ToDictionary(region => region.ViewportId, _ => 0);

            foreach (var entity in entities)
            {
                var objectName = Safe(() => Get(entity, "ObjectName")?.ToString());
                if (!SupportsLinearScaleFactor(objectName))
                    continue;

                var textPosition = Safe(() => ToPoint(Get(entity, "TextPosition")));
                var entityBounds = textPosition is null ? SafeBounds(entity) : null;
                if (textPosition is null && entityBounds is null) continue;
                var centerX = textPosition?[0]
                              ?? (entityBounds!.Value.MinX + entityBounds.Value.MaxX) / 2d;
                var centerY = textPosition?[1]
                              ?? (entityBounds!.Value.MinY + entityBounds.Value.MaxY) / 2d;
                var matchingRegions = regions
                    .Where(item => centerX >= item.MinX && centerX <= item.MaxX
                                   && centerY >= item.MinY && centerY <= item.MaxY)
                    .OrderBy(item => (item.MaxX - item.MinX) * (item.MaxY - item.MinY))
                    .ToArray();
                if (matchingRegions.Length == 0) continue;
                if (matchingRegions.Select(item => item.DimensionLinearFactor).Distinct().Count() > 1)
                    throw new InvalidOperationException(
                        $"Dimension trên sheet {sheet.SheetNumber} nằm trong nhiều viewport chồng nhau; " +
                        "không thể xác định DIMLFAC an toàn.");
                var region = matchingRegions[0];
                candidatesByViewport[region.ViewportId]++;

                try { Set(entity, "LinearScaleFactor", region.DimensionLinearFactor); }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Không đặt được DIMLFAC cho viewport {region.ViewportId} trên sheet {sheet.SheetNumber}.",
                        exception);
                }
                normalizedByViewport[region.ViewportId]++;
            }

            foreach (var viewport in sheet.Viewports.Where(viewport => viewport.SourceDimensionCount > 0))
            {
                var normalizedCount = normalizedByViewport.GetValueOrDefault(viewport.ViewportId);
                var candidateCount = candidatesByViewport.GetValueOrDefault(viewport.ViewportId);
                DwgDimensionNormalizationValidator.EnsureCadCoverage(
                    sheet.SheetNumber,
                    viewport.ViewName,
                    viewport.SourceDimensionCount,
                    candidateCount,
                    normalizedCount);
                if (candidateCount != viewport.SourceDimensionCount)
                    Log.Debug(
                        "Dimension counts differ across Revit and EXPORTLAYOUT on sheet {Sheet}, view {View}: Revit={RevitCount}, CAD={CadCount}",
                        sheet.SheetNumber,
                        viewport.ViewName,
                        viewport.SourceDimensionCount,
                        candidateCount);
            }
        }
        finally
        {
            foreach (var entity in entities) Release(entity);
            Release(modelSpace);
        }
    }

    private static void ComposeInDocument(
        object finalDocument,
        IReadOnlyList<string> flattened,
        DwgExportJob job,
        TimeSpan commandTimeout)
    {
        var modelSpace = Get(finalDocument, "ModelSpace")
            ?? throw new InvalidOperationException("Không truy cập được Model Space.");
        var gap = DwgSheetLayoutPlanner.MillimetresToDrawingUnits(job.SheetGapMillimetres, job.DrawingUnit);
        Call(finalDocument, "SetVariable", "INSUNITS", AutoCadInsertionUnit(job.DrawingUnit));
        var sheets = job.Sheets.OrderBy(sheet => sheet.Ordinal).ToArray();
        var annotationPlans = new List<DwgSheetDimensionAnnotationPlan>(sheets.Length);
        var nextX = 0d;

        for (var index = 0; index < flattened.Count; index++)
        {
            var factor = sheets[index].Viewports.Count == 0
                ? 1d
                : DwgSheetLayoutPlanner.ReferenceScale(sheets[index].Viewports);
            var block = Call(
                modelSpace,
                "InsertBlock",
                new[] { 0d, 0d, 0d },
                flattened[index],
                factor,
                factor,
                factor,
                0d) ?? throw new InvalidOperationException("Không insert được sheet DWG vào file tổng.");
            var bounds = Bounds(block);
            var dx = nextX - bounds.MinX;
            var dy = -bounds.MinY;
            Call(block, "Move", new[] { 0d, 0d, 0d }, new[] { dx, dy, 0d });
            object[] exploded;
            try { exploded = ToObjectArray(Call(block, "Explode")); }
            finally { Call(block, "Delete"); }
            var dimensions = new List<DwgDimensionAnnotationTarget>();
            try
            {
                foreach (var entity in exploded)
                {
                    var objectName = Safe(() => Get(entity, "ObjectName")?.ToString());
                    if (!SupportsLinearScaleFactor(objectName)) continue;

                    var handle = Get(entity, "Handle")?.ToString()
                        ?? throw new InvalidOperationException(
                            $"AutoCAD không trả về handle DIM trên sheet {sheets[index].SheetNumber}.");
                    var styleName = Get(entity, "StyleName")?.ToString()
                        ?? throw new InvalidOperationException(
                            $"AutoCAD không trả về dimstyle trên sheet {sheets[index].SheetNumber}.");
                    var linearScaleFactor = Convert.ToDouble(
                        Get(entity, "LinearScaleFactor"),
                        CultureInfo.InvariantCulture);
                    dimensions.Add(new DwgDimensionAnnotationTarget(handle, styleName, linearScaleFactor));
                }
            }
            finally
            {
                foreach (var entity in exploded) Release(entity);
            }
            annotationPlans.Add(new DwgSheetDimensionAnnotationPlan(
                sheets[index].SheetNumber,
                Convert.ToInt32(factor),
                dimensions));
            nextX += bounds.Width + gap;
            Release(block);
        }

        ApplyDimensionAnnotationScales(
            finalDocument,
            annotationPlans,
            job.DrawingUnit,
            job.StagingDirectory,
            commandTimeout);
        Call(finalDocument, "SetVariable", "TILEMODE", 1);
        Call(finalDocument, "Regen", 1); // acAllViewports
        Release(modelSpace);
    }

    private static void ApplyDimensionAnnotationScales(
        object document,
        IReadOnlyList<DwgSheetDimensionAnnotationPlan> plans,
        DwgDrawingUnit drawingUnit,
        string stagingDirectory,
        TimeSpan commandTimeout)
    {
        var dimensions = plans.SelectMany(plan => plan.Dimensions).ToArray();
        if (dimensions.Length == 0) return;

        var marker = $"RA_DONE_{Guid.NewGuid():N}";
        var scriptPath = Path.Combine(stagingDirectory, $"dimension-annotation-{Guid.NewGuid():N}.scr");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            File.WriteAllText(
                scriptPath,
                DwgDimensionAnnotationScriptBuilder.Build(
                    plans,
                    marker,
                    DwgSheetLayoutPlanner.InchesToDrawingUnits(1d, drawingUnit)),
                Encoding.ASCII);
            Call(document, "SetVariable", "USERS5", string.Empty);
            Call(document, "SetVariable", "USERR1", 0d);
            Call(document, "SetVariable", "USERR2", 0d);
            Call(document, "SendCommand", $"_.SCRIPT\r\"{scriptPath.Replace("\"", string.Empty)}\"\r");

            // ANNOUPDATE/-OBJECTSCALE is a single native batch and COM does not yield while
            // AutoCAD regenerates a large drawing. A 1,500+ DIM print set can legitimately
            // exceed two minutes, so retain the worker's four-minute command ceiling.
            var timeout = commandTimeout < TimeSpan.FromMinutes(4)
                ? commandTimeout
                : TimeSpan.FromMinutes(4);
            WaitForScriptCompletion(document, marker, timeout);

            var selectedCount = Convert.ToInt32(
                Convert.ToDouble(Call(document, "GetVariable", "USERR1"), CultureInfo.InvariantCulture));
            var annotativeCount = Convert.ToInt32(
                Convert.ToDouble(Call(document, "GetVariable", "USERR2"), CultureInfo.InvariantCulture));
            if (selectedCount != dimensions.Length || annotativeCount != dimensions.Length)
                throw new InvalidOperationException(
                    $"AutoCAD chỉ cập nhật annotative {annotativeCount}/{dimensions.Length} DIM " +
                    $"(tìm thấy {selectedCount}/{dimensions.Length}).");

            VerifyDimensionLinearFactors(document, plans);
            Log.Information(
                "Updated {DimensionCount} dimensions to sheet annotation scales in {ElapsedMilliseconds} ms",
                dimensions.Length,
                stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    private static void WaitForScriptCompletion(object document, string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var currentMarker = Safe(() => Call(document, "GetVariable", "USERS5")?.ToString());
            var commandActive = Safe(() => Convert.ToInt32(
                Call(document, "GetVariable", "CMDACTIVE"),
                CultureInfo.InvariantCulture));
            if (string.Equals(currentMarker, marker, StringComparison.Ordinal) && commandActive == 0)
                return;
            System.Threading.Thread.Sleep(100);
        }
        throw new TimeoutException("AutoCAD không hoàn tất cập nhật annotation scale cho DIM.");
    }

    private static void VerifyDimensionLinearFactors(
        object document,
        IReadOnlyList<DwgSheetDimensionAnnotationPlan> plans)
    {
        foreach (var plan in plans)
        foreach (var expected in plan.Dimensions)
        {
            var entity = Call(document, "HandleToObject", expected.Handle)
                ?? throw new InvalidOperationException(
                    $"Không tìm lại được DIM {expected.Handle} sau khi cập nhật sheet {plan.SheetNumber}.");
            try
            {
                var actual = Convert.ToDouble(
                    Get(entity, "LinearScaleFactor"),
                    CultureInfo.InvariantCulture);
                var tolerance = 1e-9 * Math.Max(1d, Math.Abs(expected.LinearScaleFactor));
                if (Math.Abs(actual - expected.LinearScaleFactor) > tolerance)
                    throw new InvalidOperationException(
                        $"DIMLFAC bị thay đổi trên sheet {plan.SheetNumber}, DIM {expected.Handle}: " +
                        $"trước={expected.LinearScaleFactor.ToString("G17", CultureInfo.InvariantCulture)}, " +
                        $"sau={actual.ToString("G17", CultureInfo.InvariantCulture)}.");
            }
            finally
            {
                Release(entity);
            }
        }
    }

    private static object[] ToObjectArray(object? value) => value switch
    {
        object[] items => items,
        Array array => array.Cast<object>().ToArray(),
        null => Array.Empty<object>(),
        _ => throw new InvalidOperationException("AutoCAD trả về kết quả Explode không hợp lệ.")
    };

    private static int AutoCadInsertionUnit(DwgDrawingUnit unit) => unit switch
    {
        DwgDrawingUnit.Inches => 1,
        DwgDrawingUnit.Feet => 2,
        DwgDrawingUnit.Millimetres => 4,
        DwgDrawingUnit.Centimetres => 5,
        DwgDrawingUnit.Metres => 6,
        _ => 0
    };

    private static void WaitForStableFile(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long previousLength = -1;
        var stableCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            var exists = File.Exists(path);
            var length = exists ? new FileInfo(path).Length : 0;
            stableCount = exists && length > 0 && length == previousLength ? stableCount + 1 : 0;
            if (stableCount >= 2) return;
            previousLength = length;
            System.Threading.Thread.Sleep(250);
        }
        throw new TimeoutException($"AutoCAD không hoàn tất EXPORTLAYOUT cho '{Path.GetFileName(path)}'.");
    }

    private static void WaitForExportLayout(string path, int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long previousLength = -1;
        var stableCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            var completionDialogWasPresent = DismissExportLayoutCompletionDialog(processId);
            var exists = File.Exists(path);
            var length = exists ? new FileInfo(path).Length : 0;
            stableCount = exists && length > 0 && length == previousLength ? stableCount + 1 : 0;
            if (stableCount >= 2 && !completionDialogWasPresent)
            {
                return;
            }
            previousLength = length;
            System.Threading.Thread.Sleep(200);
        }
        throw new TimeoutException($"AutoCAD không hoàn tất EXPORTLAYOUT cho '{Path.GetFileName(path)}'.");
    }

    private static void ExecuteExportLayout(
        object document,
        string command,
        string outputPath,
        int processId,
        TimeSpan timeout)
    {
        Exception? commandError = null;
        using var commandCompleted = new System.Threading.ManualResetEventSlim(false);
        var commandThread = new System.Threading.Thread(() =>
        {
            try { Call(document, "SendCommand", command); }
            catch (Exception exception) { commandError = exception; }
            finally { commandCompleted.Set(); }
        })
        {
            IsBackground = true,
            Name = "RevitAPP AutoCAD EXPORTLAYOUT"
        };
        commandThread.SetApartmentState(System.Threading.ApartmentState.STA);
        commandThread.Start();

        WaitForExportLayout(outputPath, processId, timeout);
        if (!commandCompleted.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("AutoCAD đã tạo DWG nhưng lệnh EXPORTLAYOUT không kết thúc.");
        if (commandError is not null)
            throw new InvalidOperationException("AutoCAD không thực thi được EXPORTLAYOUT.", commandError);
    }

    private static bool DismissExportLayoutCompletionDialog(int processId)
    {
        var found = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var ownerProcessId);
            if (ownerProcessId != unchecked((uint)processId)) return true;
            var title = WindowText(window);
            if (!title.Contains("Export Layout to Model Space Drawing", StringComparison.OrdinalIgnoreCase))
                return true;
            found = true;

            var buttons = new List<IntPtr>();
            EnumChildWindows(window, (child, _) =>
            {
                var className = WindowClass(child);
                if (string.Equals(className, "Button", StringComparison.OrdinalIgnoreCase))
                    buttons.Add(child);
                return true;
            }, IntPtr.Zero);

            var dontOpen = buttons.FirstOrDefault(button =>
            {
                var text = WindowText(button).Replace("&", string.Empty);
                return text.Contains("Don't open", StringComparison.OrdinalIgnoreCase)
                       || text.Contains("Do not open", StringComparison.OrdinalIgnoreCase);
            });
            if (dontOpen == IntPtr.Zero && buttons.Count >= 2)
            {
                dontOpen = buttons
                    .Select(button => (Button: button, Bounds: GetBounds(button)))
                    .OrderByDescending(item => item.Bounds.Left)
                    .First().Button;
            }
            if (dontOpen != IntPtr.Zero)
                SendMessage(dontOpen, 0x00F5, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string WindowText(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        var text = new StringBuilder(length + 1);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static string WindowClass(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    private static NativeRect GetBounds(IntPtr window)
    {
        GetWindowRect(window, out var bounds);
        return bounds;
    }

    private static void WaitForFile(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) return;
            System.Threading.Thread.Sleep(200);
        }
        throw new TimeoutException($"AutoCAD không tạo file '{path}'.");
    }

    private static void PublishAtomic(string temporary, string output)
    {
        if (File.Exists(output)) File.Replace(temporary, output, null);
        else File.Move(temporary, output);
    }

    private static int SaveAsType(DwgFileVersion version) => version switch
    {
        DwgFileVersion.R2007 => 36,
        DwgFileVersion.R2010 => 48,
        DwgFileVersion.R2013 => 60,
        DwgFileVersion.R2018 => 64,
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };

    private static object OpenDocument(object documents, string path) =>
        Call(documents, "Open", path, false)
        ?? throw new InvalidOperationException($"AutoCAD không mở được '{path}'.");

    private static void TrySetVariable(object document, string name, object? value)
    {
        if (value is null) return;
        try { Call(document, "SetVariable", name, value); }
        catch (Exception exception) { Log.Debug(exception, "Could not restore AutoCAD variable {Variable}", name); }
    }

    private static bool SupportsLinearScaleFactor(string? objectName) => objectName is
        "AcDbAlignedDimension" or
        "AcDbRotatedDimension" or
        "AcDbRadialDimension" or
        "AcDbDiametricDimension" or
        "AcDbOrdinateDimension" or
        "AcDbArcDimension" or
        "AcDbRadialDimensionLarge";

    private static (double MinX, double MinY, double MaxX, double MaxY, double Width)? SafeBounds(object entity)
    {
        try { return Bounds(entity); }
        catch { return null; }
    }

    private static (double MinX, double MinY, double MaxX, double MaxY, double Width) Bounds(object entity)
    {
        var arguments = new object?[] { null, null };
        var modifier = new ParameterModifier(arguments.Length);
        modifier[0] = true;
        modifier[1] = true;
        InvokeWithRetry<object?>(() => entity.GetType().InvokeMember(
            "GetBoundingBox",
            BindingFlags.InvokeMethod,
            null,
            entity,
            arguments,
            new[] { modifier },
            CultureInfo.InvariantCulture,
            null));
        var min = ToPoint(arguments[0]);
        var max = ToPoint(arguments[1]);
        return (min[0], min[1], max[0], max[1], max[0] - min[0]);
    }

    private static double[] ToPoint(object? value) => value switch
    {
        double[] point when point.Length >= 2 => point,
        Array array when array.Length >= 2 => array.Cast<object>().Select(Convert.ToDouble).ToArray(),
        _ => throw new InvalidOperationException("AutoCAD trả về point không hợp lệ.")
    };

    private static object WaitForPaperLayout(object layouts, string sourcePath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var layout = Enumerate(layouts).FirstOrDefault(item =>
                !string.Equals(Get(item, "Name")?.ToString(), "Model", StringComparison.OrdinalIgnoreCase));
            if (layout is not null) return layout;
            System.Threading.Thread.Sleep(250);
        }

        throw new InvalidOperationException($"DWG '{Path.GetFileName(sourcePath)}' không có paper layout.");
    }

    private static IReadOnlyList<object> Enumerate(object collection)
    {
        var count = Convert.ToInt32(Get(collection, "Count"));
        var items = new List<object>(count);
        for (var index = 0; index < count; index++)
        {
            var item = Call(collection, "Item", index);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    private static object? Get(object target, string name) => InvokeWithRetry(() =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null));

    private static void Set(object target, string name, object value) => InvokeWithRetry(() =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value }));

    private static object? Call(object target, string name, params object[] arguments) => InvokeWithRetry(() =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments));

    private static T? InvokeWithRetry<T>(Func<T> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return action(); }
            catch (TargetInvocationException exception) when (
                attempt < 240 && exception.InnerException is COMException com
                && com.HResult is unchecked((int)0x80010001) or unchecked((int)0x8001010A))
            {
                System.Threading.Thread.Sleep(250);
            }
            catch (COMException exception) when (
                attempt < 240
                && exception.HResult is unchecked((int)0x80010001) or unchecked((int)0x8001010A))
            {
                System.Threading.Thread.Sleep(250);
            }
        }
    }

    private static T? Safe<T>(Func<T> read)
    {
        try { return read(); }
        catch { return default; }
    }

    private static void Release(object value)
    {
        try { if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        catch { /* best effort */ }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    private delegate bool EnumWindowCallback(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
