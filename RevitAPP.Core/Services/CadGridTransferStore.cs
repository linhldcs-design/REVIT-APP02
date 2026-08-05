using Newtonsoft.Json;
using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

public static class CadGridTransferStore
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LDL",
            "CadToRevit",
            "latest-grid-selection.json");

    public static void WriteAtomic(CadGridTransferPackage package, string? path = null)
    {
        Validate(package, TimeSpan.MaxValue);
        path ??= DefaultPath;
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Đường dẫn transfer không hợp lệ.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonConvert.SerializeObject(package, Formatting.Indented));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static CadGridTransferPackage ReadLatest(
        string? path = null,
        TimeSpan? maximumAge = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "Chưa có dữ liệu quét từ AutoCAD. Hãy chạy TAOLUOIREVIT trong AutoCAD trước.",
                path);

        var info = new FileInfo(path);
        if (info.Length > 10 * 1024 * 1024)
            throw new InvalidDataException("Gói dữ liệu AutoCAD vượt giới hạn 10 MB.");

        CadGridTransferPackage package;
        try
        {
            package = JsonConvert.DeserializeObject<CadGridTransferPackage>(
                    File.ReadAllText(path))
                ?? throw new InvalidDataException("Gói dữ liệu AutoCAD rỗng.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Gói dữ liệu AutoCAD bị hỏng.", exception);
        }

        Validate(package, maximumAge ?? TimeSpan.FromMinutes(30));
        return package;
    }

    public static void Validate(CadGridTransferPackage package, TimeSpan maximumAge)
    {
        if (package.SchemaVersion != CadGridTransferPackage.CurrentSchemaVersion)
            throw new InvalidDataException($"Schema AutoCAD không hỗ trợ: {package.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(package.SelectionId))
            throw new InvalidDataException("SelectionId bị thiếu.");
        if (package.Lines is null || package.Lines.Count < 2 || package.Lines.Count > 10000)
            throw new InvalidDataException("Số lượng LINE phải từ 2 đến 10000.");
        if (maximumAge != TimeSpan.MaxValue
            && DateTime.UtcNow - package.CreatedUtc.ToUniversalTime() > maximumAge)
            throw new InvalidDataException(
                $"Dữ liệu quét AutoCAD đã quá {maximumAge.TotalMinutes:0} phút.");
        if (package.Lines.Any(line =>
                !IsFinite(line.StartX) || !IsFinite(line.StartY)
                || !IsFinite(line.EndX) || !IsFinite(line.EndY)))
            throw new InvalidDataException("Gói AutoCAD chứa tọa độ không hữu hạn.");
        _ = CadGridUnitConverter.MillimetresPerDrawingUnit(package.InsUnits);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
