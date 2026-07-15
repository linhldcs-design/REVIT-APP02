namespace RevitAPP.Installer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        Dispatcher.BeginInvoke(SelfInstaller.EnsureInstalled,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
