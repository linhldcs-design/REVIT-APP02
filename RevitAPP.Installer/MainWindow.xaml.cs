namespace RevitAPP.Installer;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new InstallerViewModel();
        viewModel.RestartInstallerRequested += (_, _) => System.Windows.Application.Current.Shutdown();
        DataContext = viewModel;
    }
}
