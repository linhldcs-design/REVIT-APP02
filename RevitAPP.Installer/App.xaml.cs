namespace RevitAPP.Installer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        if (InstallerSelfUpdater.TryParseHelperArguments(e.Args, out var helper))
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            _ = RunHelperAsync(helper);
            return;
        }

        InstallerSelfUpdater.ScheduleCleanup(e.Args);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        Dispatcher.BeginInvoke(SelfInstaller.EnsureInstalled,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private async Task RunHelperAsync(InstallerSelfUpdateArguments helper)
    {
        var exitCode = await InstallerSelfUpdater.RunHelperAsync(helper);
        Shutdown(exitCode);
    }
}
