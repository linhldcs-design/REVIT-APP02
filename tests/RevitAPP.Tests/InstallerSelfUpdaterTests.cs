using RevitAPP.Installer;
using Xunit;

namespace RevitAPP.Tests;

public sealed class InstallerSelfUpdaterTests
{
    [Fact]
    public void HelperArguments_AcceptOnlyInstalledInstallerTarget()
    {
        Assert.True(InstallerSelfUpdater.TryParseHelperArguments(new[]
        {
            "--self-update-target", SelfInstaller.InstalledPath,
            "--wait-pid", "123"
        }, out var accepted));
        Assert.Equal(Path.GetFullPath(SelfInstaller.InstalledPath), accepted.TargetPath);

        Assert.False(InstallerSelfUpdater.TryParseHelperArguments(new[]
        {
            "--self-update-target", Path.Combine(Path.GetTempPath(), "other.exe"),
            "--wait-pid", "123"
        }, out _));

        Assert.False(InstallerSelfUpdater.TryParseHelperArguments(new[]
        {
            "--self-update-target", SelfInstaller.InstalledPath,
            "--wait-pid", "0"
        }, out _));
    }

    [Fact]
    public void CleanupDirectory_AcceptsOnlyOwnedTempDirectory()
    {
        Assert.True(InstallerSelfUpdater.IsSafeCleanupDirectory(
            Path.Combine(Path.GetTempPath(), "RevitAPP-Installer-abc")));
        Assert.False(InstallerSelfUpdater.IsSafeCleanupDirectory(
            Path.Combine(Path.GetTempPath(), "unrelated")));
        Assert.False(InstallerSelfUpdater.IsSafeCleanupDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    }

    [Fact]
    public void ReplaceFileAtomically_ReplacesTargetAndRemovesWorkingFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RevitAPP-Installer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.exe");
            var target = Path.Combine(directory, "target.exe");
            File.WriteAllText(source, "new-installer");
            File.WriteAllText(target, "old-installer");

            InstallerSelfUpdater.ReplaceFileAtomically(source, target);

            Assert.Equal("new-installer", File.ReadAllText(target));
            Assert.False(File.Exists(target + ".backup"));
            Assert.Empty(Directory.EnumerateFiles(directory, "target.exe.new-*"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RetainedReplacement_RestoresOldInstallerWhenRelaunchFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RevitAPP-Installer-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.exe");
            var target = Path.Combine(directory, "target.exe");
            File.WriteAllText(source, "new-installer");
            File.WriteAllText(target, "old-installer");

            var replacement = InstallerSelfUpdater.ReplaceFileAtomically(source, target, retainBackup: true);
            Assert.Equal("new-installer", File.ReadAllText(target));
            Assert.True(File.Exists(target + ".backup"));

            InstallerSelfUpdater.RollbackReplacement(target, replacement);

            Assert.Equal("old-installer", File.ReadAllText(target));
            Assert.False(File.Exists(target + ".backup"));
            Assert.Empty(Directory.EnumerateFiles(directory, "target.exe.failed-*"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
