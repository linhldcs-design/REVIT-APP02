using System.IO;
using System.Text;
using ExcelDataReader;

namespace RevitAPP.Chat.Services;

public static class ExcelWorkbookReader
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".xls", ".xlsx", ".xlsm", ".xlsb", ".csv" };

    static ExcelWorkbookReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static IReadOnlyList<string> Find(string directory, bool recursive, int limit)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory));
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Không tìm thấy thư mục: {path}");
        return Directory.EnumerateFiles(path, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(file => Extensions.Contains(Path.GetExtension(file)))
            .Take(Math.Clamp(limit, 1, 200)).ToList();
    }

    public static IReadOnlyList<ExcelSheetInfo> Inspect(string filePath)
    {
        using var reader = Open(filePath);
        var sheets = new List<ExcelSheetInfo>();
        do
        {
            sheets.Add(new ExcelSheetInfo(reader.Name, reader.RowCount, reader.FieldCount));
        } while (reader.NextResult());
        return sheets;
    }

    public static ExcelTablePreview Read(string filePath, string? sheetName, int headerRow, int startRow,
        int maxRows, int maxColumns)
    {
        using var reader = Open(filePath);
        var found = false;
        do
        {
            if (string.IsNullOrWhiteSpace(sheetName) || string.Equals(reader.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
        } while (reader.NextResult());
        if (!found) throw new ArgumentException($"Không tìm thấy sheet '{sheetName}'.");

        headerRow = Math.Max(1, headerRow);
        startRow = Math.Max(headerRow + 1, startRow);
        maxRows = Math.Clamp(maxRows, 1, 1000);
        maxColumns = Math.Clamp(maxColumns, 1, 200);
        var headers = new List<string>();
        var rows = new List<IReadOnlyList<object?>>();
        var rowNumber = 0;
        while (reader.Read())
        {
            rowNumber++;
            var count = Math.Min(reader.FieldCount, maxColumns);
            if (rowNumber == headerRow)
            {
                for (var column = 0; column < count; column++)
                    headers.Add(Convert.ToString(reader.GetValue(column))?.Trim() is { Length: > 0 } value
                        ? value : $"Column{column + 1}");
            }
            if (rowNumber < startRow) continue;
            var row = new List<object?>(count);
            for (var column = 0; column < count; column++) row.Add(ToJsonValue(reader.GetValue(column)));
            if (row.Any(value => value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(value)))) rows.Add(row);
            if (rows.Count >= maxRows) break;
        }
        if (headers.Count == 0)
            headers.AddRange(Enumerable.Range(1, Math.Min(reader.FieldCount, maxColumns)).Select(index => $"Column{index}"));
        return new ExcelTablePreview(Path.GetFullPath(filePath), reader.Name, headerRow, startRow, headers, rows,
            reader.RowCount > rowNumber || rows.Count >= maxRows);
    }

    private static IExcelDataReader Open(string filePath)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(filePath));
        if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy file Excel.", path);
        if (!Extensions.Contains(Path.GetExtension(path)))
            throw new NotSupportedException("Chỉ hỗ trợ .xls, .xlsx, .xlsm, .xlsb và .csv.");
        var info = new FileInfo(path);
        if (info.Length > 100L * 1024 * 1024) throw new InvalidOperationException("File Excel vượt giới hạn 100 MB.");
        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            return string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase)
                ? ExcelReaderFactory.CreateCsvReader(stream)
                : ExcelReaderFactory.CreateReader(stream);
        }
        catch { stream.Dispose(); throw; }
    }

    private static object? ToJsonValue(object? value) => value switch
    {
        null or DBNull => null,
        DateTime date => date.ToString("O"),
        TimeSpan time => time.ToString(),
        _ => value
    };
}

public sealed record ExcelSheetInfo(string Name, int RowCount, int ColumnCount);
public sealed record ExcelTablePreview(string FilePath, string SheetName, int HeaderRow, int StartRow,
    IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows, bool Truncated);
