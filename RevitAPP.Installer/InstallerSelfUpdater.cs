using System.Diagnostics;
using System.IO;

namespace RevitAPP.Installer;

internal sealed record InstallerSelfUpdateArguments(string TargetPath, int WaitPid);
internal sealed record InstallerReplacement(string? BackupPath, bool TargetExisted);

internal static class InstallerSelfUpdater
{
    private const string UpdateTarget = "--self-update-target";
    private const string WaitPid = "--wait-pid";
    private const string CleanupDirectory = "--cleanup-update-dir";

    public static void LaunchHelper(string candidatePath, string targetPath, int waitPid)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = candidatePath,
            Arguments = $"{UpdateTarget} {Quote(targetPath)} {WaitPid} {waitPid}",
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Không thể khởi động Installer cập nhật.");
    }

    public static bool TryParseHelperArguments(string[] args, out InstallerSelfUpdateArguments result)
    {
        result = new InstallerSelfUpdateArguments(string.Empty, 0);
        var values = Parse(args);
        if (!values.TryGetValue(UpdateTarget, out var target)
            || !values.TryGetValue(WaitPid, out var pidText)
            || !int.TryParse(pidText, out var pid)
            || pid <= 0
            || string.IsNullOrWhiteSpace(target)) return false;
        var fullTarget = Path.GetFullPath(target);
        if (!string.Equals(fullTarget, Path.GetFullPath(SelfInstaller.InstalledPath),
                StringComparison.OrdinalIgnoreCase)) return false;
        result = new InstallerSelfUpdateArguments(fullTarget, pid);
        return true;
    }

    public static async Task<int> RunHelperAsync(InstallerSelfUpdateArguments args)
    {
        try
        {
            try
            {
                using var old = Process.GetProcessById(args.WaitPid);
                await old.WaitForExitAsync();
            }
            catch (ArgumentException) { }

            var source = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Không xác định được Installer nguồn.");
            var replacement = ReplaceFileAtomically(source, args.TargetPath, retainBackup: true);
            try
            {
                var cleanup = Path.GetDirectoryName(source);
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = args.TargetPath,
                    Arguments = cleanup is null
                        ? string.Empty
                        : $"{CleanupDirectory} {Quote(cleanup)} {WaitPid} {Environment.ProcessId}",
                    UseShellExecute = true
                }) ?? throw new InvalidOperationException("Không thể khởi động Installer mới.");
                CompleteReplacement(replacement);
            }
            catch
            {
                RollbackReplacement(args.TargetPath, replacement);
                throw;
            }
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                var directory = Path.GetDirectoryName(args.TargetPath);
                if (directory is not null)
                    File.AppendAllText(Path.Combine(directory, "installer-update.log"),
                        $"[{DateTime.Now:O}] {exception}\n");
                if (File.Exists(args.TargetPath))
                    Process.Start(new ProcessStartInfo { FileName = args.TargetPath, UseShellExecute = true });
            }
            catch { }
            return 4;
        }
    }

    public static void ScheduleCleanup(string[] args)
    {
        var values = Parse(args);
        if (!values.TryGetValue(CleanupDirectory, out var directory)) return;
        directory = Path.GetFullPath(directory);
        if (!IsSafeCleanupDirectory(directory)) return;
        _ = Task.Run(async () =>
        {
            if (values.TryGetValue(WaitPid, out var pidText) && int.TryParse(pidText, out var pid))
            {
                try
                {
                    using var helper = Process.GetProcessById(pid);
                    await helper.WaitForExitAsync();
                }
                catch (ArgumentException) { }
            }
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { Directory.Delete(directory, true); return; }
                catch when (attempt < 4) { await Task.Delay(500); }
            }
        });
    }

    internal static InstallerReplacement ReplaceFileAtomically(
        string source,
        string target,
        bool retainBackup = false)
    {
        var targetDirectory = Path.GetDirectoryName(target)
                              ?? throw new InvalidOperationException("Đường dẫn Installer đích không hợp lệ.");
        Directory.CreateDirectory(targetDirectory);
        var staging = target + ".new-" + Guid.NewGuid().ToString("N");
        var backup = target + ".backup";
        var targetExisted = File.Exists(target);
        try
        {
            File.Copy(source, staging, true);
            if (targetExisted)
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(staging, target, backup, true);
            }
            else
            {
                File.Move(staging, target);
            }
            var replacement = new InstallerReplacement(targetExisted ? backup : null, targetExisted);
            if (!retainBackup) CompleteReplacement(replacement);
            return replacement;
        }
        finally
        {
            if (File.Exists(staging))
            {
                try { File.Delete(staging); } catch { }
            }
        }
    }

    internal static void RollbackReplacement(string target, InstallerReplacement replacement)
    {
        if (replacement.TargetExisted && replacement.BackupPath is not null
                                      && File.Exists(replacement.BackupPath))
        {
            var failed = target + ".failed-" + Guid.NewGuid().ToString("N");
            try
            {
                if (File.Exists(target)) File.Replace(replacement.BackupPath, target, failed, true);
                else File.Move(replacement.BackupPath, target);
            }
            finally
            {
                if (File.Exists(failed))
                {
                    try { File.Delete(failed); } catch { }
                }
            }
            return;
        }

        if (!replacement.TargetExisted && File.Exists(target)) File.Delete(target);
    }

    private static void CompleteReplacement(InstallerReplacement replacement)
    {
        if (replacement.BackupPath is not null && File.Exists(replacement.BackupPath))
        {
            try { File.Delete(replacement.BackupPath); } catch { }
        }
    }

    internal static bool IsSafeCleanupDirectory(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        return fullDirectory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
               && Path.GetFileName(fullDirectory).StartsWith("RevitAPP-Installer-", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < args.Length; index += 2)
            result[args[index]] = args[index + 1];
        return result;
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';
}
