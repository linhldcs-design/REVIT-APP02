using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using RevitAPP.Core.Models.DwgExport;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.DwgExport;

internal static class DwgPostProcessWorkerRunner
{
    private static readonly TimeSpan NoProgressTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(12);

    public static string Run(DwgExportJob job, string manifestPath)
    {
        var addinDirectory = Path.GetDirectoryName(typeof(DwgPostProcessWorkerRunner).Assembly.Location)
            ?? throw new InvalidOperationException("Không xác định được thư mục RevitAPP.");
        var workerPath = Path.Combine(addinDirectory, "RevitAPP.DwgExportWorker.exe");
        if (!File.Exists(workerPath))
            throw new FileNotFoundException("Thiếu DWG worker đi kèm RevitAPP.", workerPath);

        var resultPath = Path.Combine(job.StagingDirectory, "result.json");
        var progressPath = Path.Combine(job.StagingDirectory, AutoCadDwgPostProcessor.ProgressFileName);
        DeleteIfExists(resultPath);
        DeleteIfExists(progressPath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(workerPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = job.StagingDirectory
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add(manifestPath);
        process.StartInfo.ArgumentList.Add(resultPath);
        if (!process.Start())
            throw new InvalidOperationException("Không khởi động được DWG worker.");

        var outcome = ShowProgress(process, job, progressPath);
        if (outcome is WorkerOutcome.Cancelled or WorkerOutcome.TimedOut)
        {
            TerminateWorkerAndOwnedAutoCad(process, job.StagingDirectory);
            throw outcome == WorkerOutcome.Cancelled
                ? new OperationCanceledException("Người dùng đã hủy hậu xử lý DWG.")
                : new TimeoutException("DWG worker không tạo tiến độ trong 5 phút và đã được dừng an toàn.");
        }

        if (!File.Exists(resultPath))
            throw new InvalidOperationException($"DWG worker kết thúc với mã {process.ExitCode} nhưng không trả result.json.");
        var result = DwgExportJobStore.ReadResult(resultPath, job.JobId);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.Error ?? "DWG worker thất bại.");
        if (string.IsNullOrWhiteSpace(result.TemporaryOutputPath) || !File.Exists(result.TemporaryOutputPath))
            throw new FileNotFoundException("DWG worker báo thành công nhưng file đầu ra không tồn tại.", result.TemporaryOutputPath);
        return result.TemporaryOutputPath;
    }

    private static WorkerOutcome ShowProgress(Process process, DwgExportJob job, string progressPath)
    {
        var title = new TextBlock
        {
            Text = $"Đang ghép {job.Sheets.Count} sheet vào một DWG...",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var status = new TextBlock
        {
            Text = "Đang khởi động AutoCAD riêng...",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var progress = new ProgressBar { Minimum = 0, Maximum = job.Sheets.Count, Height = 18 };
        var cancel = new Button
        {
            Content = "Hủy",
            Width = 90,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(title);
        panel.Children.Add(status);
        panel.Children.Add(progress);
        panel.Children.Add(cancel);
        var window = new Window
        {
            Title = "RevitAPP - Xuất DWG",
            Width = 520,
            Height = 210,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel,
            ShowInTaskbar = false
        };
        var owner = Process.GetCurrentProcess().MainWindowHandle;
        if (owner != IntPtr.Zero) new WindowInteropHelper(window).Owner = owner;

        var outcome = WorkerOutcome.Completed;
        var startedUtc = DateTime.UtcNow;
        var lastProgressUtc = startedUtc;
        string? lastProgress = null;
        var allowClose = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        cancel.Click += (_, _) =>
        {
            outcome = WorkerOutcome.Cancelled;
            allowClose = true;
            window.Close();
        };
        window.Closing += (_, e) =>
        {
            if (allowClose) return;
            outcome = WorkerOutcome.Cancelled;
            allowClose = true;
        };
        timer.Tick += (_, _) =>
        {
            if (process.HasExited)
            {
                timer.Stop();
                allowClose = true;
                window.Close();
                return;
            }

            var current = ReadAllTextSafe(progressPath);
            if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, lastProgress, StringComparison.Ordinal))
            {
                lastProgress = current;
                lastProgressUtc = DateTime.UtcNow;
                UpdateProgress(current, status, progress);
            }
            var now = DateTime.UtcNow;
            if (now - lastProgressUtc <= NoProgressTimeout && now - startedUtc <= TotalTimeout) return;
            outcome = WorkerOutcome.TimedOut;
            timer.Stop();
            allowClose = true;
            window.Close();
        };
        timer.Start();
        window.ShowDialog();
        timer.Stop();
        return outcome;
    }

    private static void UpdateProgress(string value, TextBlock status, ProgressBar progress)
    {
        var fields = value.Split('|');
        if (fields.Length < 5 || !int.TryParse(fields[1], out var completed) || !int.TryParse(fields[2], out var total))
            return;
        progress.Maximum = Math.Max(1, total);
        progress.Value = Math.Min(completed, total);
        status.Text = fields[3] switch
        {
            "flattening" => $"Đang xử lý sheet {completed + 1}/{total}: {fields[4]}",
            "flattened" => $"Đã xử lý {completed}/{total} sheet",
            "composing" => "Đang ghép và lưu file DWG duy nhất...",
            "completed" => "Đã hoàn tất file DWG.",
            _ => status.Text
        };
    }

    private static void TerminateWorkerAndOwnedAutoCad(Process worker, string stagingDirectory)
    {
        try { if (!worker.HasExited) worker.Kill(true); }
        catch { /* best effort; exact AutoCAD lease is handled below */ }

        var leasePath = Path.Combine(stagingDirectory, AutoCadDwgPostProcessor.OwnedProcessLeaseFileName);
        var fields = ReadAllTextSafe(leasePath)?.Split('|');
        if (fields is not { Length: 2 }
            || !int.TryParse(fields[0], out var processId)
            || !long.TryParse(fields[1], out var startTicks)) return;
        try
        {
            using var autoCad = Process.GetProcessById(processId);
            if (!string.Equals(autoCad.ProcessName, "acad", StringComparison.OrdinalIgnoreCase)
                || autoCad.StartTime.ToUniversalTime().Ticks != startTicks) return;
            autoCad.Kill(true);
            autoCad.WaitForExit(5_000);
        }
        catch (ArgumentException) { }
        catch { /* never broaden cleanup to another acad process */ }
    }

    private static string? ReadAllTextSafe(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private enum WorkerOutcome
    {
        Completed,
        Cancelled,
        TimedOut
    }
}
