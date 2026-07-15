using System.IO;

namespace RevitAPP.Installer;

internal static class SelfInstaller
{
    public static void EnsureInstalled()
    {
        try
        {
            var source = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "RevitAPP Installer");
            var target = Path.Combine(folder, "RevitAPP.Installer.exe");
            Directory.CreateDirectory(folder);
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target, true);

            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "RevitAPP Installer.lnk"), target);
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "RevitAPP Installer.lnk"), target);
        }
        catch
        {
            // Self-install không được phá giao diện chính; user vẫn có thể chạy bản portable.
        }
    }

    private static void CreateShortcut(string shortcutPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target);
        shortcut.IconLocation = target + ",0";
        shortcut.Description = "Cài đặt và cập nhật RevitAPP";
        shortcut.Save();
    }
}
