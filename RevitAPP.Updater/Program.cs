using System.Diagnostics;
using System.Text.Json;

var argsMap = Args(args);
if (!argsMap.TryGetValue("--pending", out var pendingPath) || !File.Exists(pendingPath)) return 2;
if (argsMap.TryGetValue("--wait-pid", out var pidText) && int.TryParse(pidText, out var pid))
{
    try { Process.GetProcessById(pid).WaitForExit(); } catch (ArgumentException) { }
}

var pending = JsonSerializer.Deserialize<PendingUpdate>(await File.ReadAllTextAsync(pendingPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (pending == null || !Directory.Exists(pending.PayloadDirectory)) return 3;

var backup = pending.InstallDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss");
try
{
    Directory.CreateDirectory(backup);
    Directory.CreateDirectory(pending.InstallDirectory);
    foreach (var source in Directory.EnumerateFiles(pending.InstallDirectory))
        File.Copy(source, Path.Combine(backup, Path.GetFileName(source)), true);
    foreach (var source in Directory.EnumerateFiles(pending.PayloadDirectory, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(pending.PayloadDirectory, source);
        var destination = Path.Combine(pending.InstallDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }
    File.WriteAllText(Path.Combine(pending.InstallDirectory, "installed-version.txt"), pending.Version);
    File.Delete(pendingPath);
    return 0;
}
catch
{
    foreach (var source in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(backup, source);
        var destination = Path.Combine(pending.InstallDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }
    return 4;
}

static Dictionary<string, string> Args(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < values.Length; i += 2) result[values[i]] = values[i + 1];
    return result;
}

internal sealed record PendingUpdate(string Version, string PayloadDirectory, string InstallDirectory, string RevitYear);
