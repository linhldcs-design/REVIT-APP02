namespace RevitAPP.Installer;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new InstallerViewModel();
    }
}
