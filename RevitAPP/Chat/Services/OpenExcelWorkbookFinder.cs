using System.Runtime.InteropServices;
using System.IO;

namespace RevitAPP.Chat.Services;

public static class OpenExcelWorkbookFinder
{
    public static IReadOnlyList<string> Find(int timeoutMs = 5000)
    {
        IReadOnlyList<string>? result = null;
        Exception? error = null;
        using var done = new ManualResetEvent(false);
        var thread = new Thread(() =>
        {
            try { result = FindOnStaThread(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        }) { IsBackground = true, Name = "RevitAPP Excel COM discovery" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!done.WaitOne(timeoutMs)) throw new TimeoutException("Excel không phản hồi trong 5 giây.");
        if (error is not null) throw new InvalidOperationException(error.Message, error);
        return result ?? Array.Empty<string>();
    }

    private static IReadOnlyList<string> FindOnStaThread()
    {
        var clsid = Guid.Empty;
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("Excel.Application", out clsid));
        var hr = GetActiveObject(ref clsid, IntPtr.Zero, out var applicationObject);
        if (hr < 0 || applicationObject is null) return Array.Empty<string>();

        var paths = new List<string>();
        object? workbooks = null;
        try
        {
            dynamic application = applicationObject;
            workbooks = application.Workbooks;
            var count = (int)((dynamic)workbooks).Count;
            for (var index = 1; index <= count; index++)
            {
                object? workbook = null;
                try
                {
                    workbook = ((dynamic)workbooks)[index];
                    var path = Convert.ToString(((dynamic)workbook).FullName);
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) paths.Add(Path.GetFullPath(path));
                }
                finally { Release(workbook); }
            }
        }
        finally
        {
            Release(workbooks);
            Release(applicationObject);
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
