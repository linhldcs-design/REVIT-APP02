using Newtonsoft.Json;
using RevitAPP.Core.Models.DwgExport;

namespace RevitAPP.Core.Services;

public static class DwgExportJobStore
{
    private const int MaximumManifestBytes = 5 * 1024 * 1024;

    public static string CreateStagingDirectory(string? root = null)
    {
        root ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitAPP",
            "DwgExportJobs");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void WriteJobAtomic(DwgExportJob job, string path)
    {
        ValidateJob(job);
        WriteAtomic(path, JsonConvert.SerializeObject(job, Formatting.Indented));
    }

    public static DwgExportJob ReadJob(string path)
    {
        var job = Read<DwgExportJob>(path, "DWG export job");
        ValidateJob(job);
        return job;
    }

    public static void WriteResultAtomic(DwgPostProcessResult result, string path)
    {
        ValidateResult(result);
        WriteAtomic(path, JsonConvert.SerializeObject(result, Formatting.Indented));
    }

    public static DwgPostProcessResult ReadResult(string path, string expectedJobId)
    {
        var result = Read<DwgPostProcessResult>(path, "DWG worker result");
        ValidateResult(result);
        if (!string.Equals(result.JobId, expectedJobId, StringComparison.Ordinal))
            throw new InvalidDataException("Worker result không thuộc job hiện tại.");
        return result;
    }

    public static void ValidateJob(DwgExportJob job)
    {
        if (job.SchemaVersion != DwgExportJob.CurrentSchemaVersion)
            throw new InvalidDataException($"DWG job schema không hỗ trợ: {job.SchemaVersion}.");
        if (!Guid.TryParseExact(job.JobId, "N", out _))
            throw new InvalidDataException("DWG job id không hợp lệ.");
        if (string.IsNullOrWhiteSpace(job.ExportSetupName))
            throw new InvalidDataException("Export DWG setup bị thiếu.");
        if (job.SheetGapMillimetres < 0 || double.IsNaN(job.SheetGapMillimetres)
            || double.IsInfinity(job.SheetGapMillimetres))
            throw new InvalidDataException("Khoảng cách sheet không hợp lệ.");
        if (job.Sheets is null || job.Sheets.Count == 0 || job.Sheets.Count > 1000)
            throw new InvalidDataException("Job phải chứa từ 1 đến 1000 sheet.");
        if (job.Sheets.Select(sheet => sheet.Ordinal).Distinct().Count() != job.Sheets.Count)
            throw new InvalidDataException("Ordinal sheet bị trùng.");

        var staging = CanonicalDirectory(job.StagingDirectory);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(job.RequestedOutputPath)))
            throw new InvalidDataException("Đường dẫn DWG đầu ra không hợp lệ.");

        foreach (var sheet in job.Sheets)
        {
            if (sheet.Ordinal < 0 || string.IsNullOrWhiteSpace(sheet.SheetNumber))
                throw new InvalidDataException("Metadata sheet không hợp lệ.");
            if (Path.GetFileName(sheet.StagedFileName) != sheet.StagedFileName
                || !sheet.StagedFileName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Tên DWG staging không hợp lệ.");
            EnsureWithin(staging, Path.Combine(staging, sheet.StagedFileName));
            if (sheet.Viewports is null || sheet.Viewports.Any(viewport => viewport.ScaleDenominator <= 0))
                throw new InvalidDataException($"Viewport của sheet {sheet.SheetNumber} không hợp lệ.");
        }
    }

    private static void ValidateResult(DwgPostProcessResult result)
    {
        if (result.SchemaVersion != DwgPostProcessResult.CurrentSchemaVersion)
            throw new InvalidDataException($"DWG result schema không hỗ trợ: {result.SchemaVersion}.");
        if (!Guid.TryParseExact(result.JobId, "N", out _))
            throw new InvalidDataException("DWG result job id không hợp lệ.");
        if (result.Succeeded && string.IsNullOrWhiteSpace(result.TemporaryOutputPath))
            throw new InvalidDataException("Worker thành công nhưng thiếu file đầu ra.");
        if (!result.Succeeded && string.IsNullOrWhiteSpace(result.Error))
            throw new InvalidDataException("Worker thất bại nhưng thiếu thông báo lỗi.");
    }

    private static T Read<T>(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Không tìm thấy {label}.", path);
        var info = new FileInfo(path);
        if (info.Length > MaximumManifestBytes)
            throw new InvalidDataException($"{label} vượt giới hạn 5 MB.");
        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"{label} rỗng.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{label} bị hỏng.", exception);
        }
    }

    private static void WriteAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException("Đường dẫn manifest không hợp lệ.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string CanonicalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Staging directory bị thiếu.");
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static void EnsureWithin(string root, string candidate)
    {
        var full = Path.GetFullPath(candidate);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Đường dẫn staging vượt khỏi thư mục job.");
    }
}
