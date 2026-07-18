using System.Runtime.InteropServices;
using System.IO;

namespace RevitAPP.Chat.Services;

/// <summary>Đọc trực tiếp workbook Excel đang mở, gồm cả thay đổi chưa Save.</summary>
public static class OpenExcelTableReader
{
    private static int _readInProgress;

    public static ExcelTablePreview Read(string filePath, string? sheetName, int headerRow, int timeoutMs = 5000)
    {
        if (Interlocked.CompareExchange(ref _readInProgress, 1, 0) != 0)
            throw new InvalidOperationException("Excel đang xử lý yêu cầu đọc trước đó. Vui lòng chờ rồi thử lại.");

        var completion = new TaskCompletionSource<ExcelTablePreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(ReadOnStaThread(filePath, sheetName, headerRow)); }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally { Interlocked.Exchange(ref _readInProgress, 0); }
        }) { IsBackground = true, Name = "RevitAPP Excel live table reader" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!completion.Task.Wait(timeoutMs))
            throw new TimeoutException($"Excel không phản hồi trong {Math.Max(1, timeoutMs / 1000)} giây.");
        try { return completion.Task.GetAwaiter().GetResult(); }
        catch (Exception error) { throw new InvalidOperationException(error.Message, error); }
    }

    private static ExcelTablePreview ReadOnStaThread(string filePath, string? sheetName, int headerRow)
    {
        var clsid = Guid.Empty;
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("Excel.Application", out clsid));
        Marshal.ThrowExceptionForHR(GetActiveObject(ref clsid, IntPtr.Zero, out var applicationObject));
        object? workbooks = null, workbook = null, worksheets = null, worksheet = null, usedRange = null;
        try
        {
            dynamic application = applicationObject!;
            workbooks = application.Workbooks;
            foreach (dynamic candidate in (dynamic)workbooks)
            {
                var candidatePath = Convert.ToString(candidate.FullName);
                if (string.Equals(Path.GetFullPath(candidatePath ?? string.Empty), Path.GetFullPath(filePath),
                        StringComparison.OrdinalIgnoreCase)) { workbook = candidate; break; }
                Release(candidate);
            }
            if (workbook is null) throw new InvalidOperationException("Workbook không còn mở trong Excel.");
            worksheets = ((dynamic)workbook).Worksheets;
            if (string.IsNullOrWhiteSpace(sheetName)) worksheet = ((dynamic)worksheets)[1];
            else
            {
                foreach (dynamic candidate in (dynamic)worksheets)
                {
                    if (string.Equals(Convert.ToString(candidate.Name), sheetName, StringComparison.OrdinalIgnoreCase))
                    { worksheet = candidate; break; }
                    Release(candidate);
                }
            }
            if (worksheet is null) throw new ArgumentException($"Không tìm thấy sheet '{sheetName}'.");
            usedRange = ((dynamic)worksheet).UsedRange;
            var firstRow = (int)((dynamic)usedRange).Row;
            var firstColumn = (int)((dynamic)usedRange).Column;
            var rowCount = (int)((dynamic)usedRange).Rows.Count;
            var columnCount = (int)((dynamic)usedRange).Columns.Count;
            var lastRow = firstRow + rowCount - 1;
            var lastColumn = firstColumn + columnCount - 1;
            headerRow = Math.Max(firstRow, headerRow);
            if (headerRow > lastRow)
                throw new ArgumentOutOfRangeException(nameof(headerRow),
                    $"Dòng tiêu đề {headerRow} nằm ngoài vùng dữ liệu Excel ({firstRow}-{lastRow}).");
            var headers = new List<string>(columnCount);
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var value = Convert.ToString(((dynamic)worksheet).Cells[headerRow, column].Text)?.Trim();
                headers.Add(string.IsNullOrWhiteSpace(value) ? $"Column{column - firstColumn + 1}" : value);
            }
            var rows = new List<IReadOnlyList<object?>>();
            for (var row = headerRow + 1; row <= lastRow; row++)
            {
                var values = new List<object?>(columnCount);
                for (var column = firstColumn; column <= lastColumn; column++)
                    values.Add(Convert.ToString(((dynamic)worksheet).Cells[row, column].Text));
                if (values.Any(value => !string.IsNullOrWhiteSpace(Convert.ToString(value)))) rows.Add(values);
            }
            return new ExcelTablePreview(Path.GetFullPath(filePath), Convert.ToString(((dynamic)worksheet).Name) ?? "",
                headerRow, headerRow + 1, headers, rows, false);
        }
        finally
        {
            Release(usedRange); Release(worksheet); Release(worksheets); Release(workbook);
            Release(workbooks); Release(applicationObject);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);
    [DllImport("oleaut32.dll")]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? value);
}
