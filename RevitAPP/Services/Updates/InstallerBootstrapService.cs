using System.Diagnostics;
using System.IO;
using RevitAPP.Core.Services;
using Serilog;

namespace RevitAPP.Services.Updates;

internal static class InstallerBootstrapService
{
    private const string InstallerFileName = "RevitAPP.Installer.exe";

    public static void EnsureBundledInstallerInstalled()
    {
        try
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(InstallerBootstrapService).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory)) return;
            var source = Path.Combine(assemblyDirectory, InstallerFileName);
            if (!File.Exists(source)) return;

            var targetDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "RevitAPP Installer");
            var target = Path.Combine(targetDirectory, InstallerFileName);
            Directory.CreateDirectory(targetDirectory);
            if (File.Exists(target) && !ShouldReplace(source, target)) return;

            var staging = target + ".new-" + Guid.NewGuid().ToString("N");
            var backup = target + ".backup";
            try
            {
                File.Copy(source, staging, true);
                if (File.Exists(target))
                {
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Replace(staging, target, backup, true);
                }
                else
                {
                    File.Move(staging, target);
                }
                if (File.Exists(backup))
                {
                    try { File.Delete(backup); } catch { }
                }
                Log.Information("Updated installed RevitAPP Installer from bundled payload");
            }
            finally
            {
                if (File.Exists(staging))
                {
                    try { File.Delete(staging); } catch { }
                }
            }
        }
        catch (Exception exception)
        {
            // A locked installer can be retried at the next Revit startup. Never break add-in startup.
            Log.Warning(exception, "Could not update the installed RevitAPP Installer from bundled payload");
        }
    }

    private static bool ShouldReplace(string source, string target)
    {
        var sourceVersion = FileVersionInfo.GetVersionInfo(source).FileVersion;
        var targetVersion = FileVersionInfo.GetVersionInfo(target).FileVersion;
        if (string.IsNullOrWhiteSpace(sourceVersion)) return false;
        if (string.IsNullOrWhiteSpace(targetVersion)) return true;
        return UpdatePackageVerifier.IsNewer(sourceVersion, targetVersion);
    }
}
