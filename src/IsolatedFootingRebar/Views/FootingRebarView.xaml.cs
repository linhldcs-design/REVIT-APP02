using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IsolatedFootingRebar.ViewModels;

namespace IsolatedFootingRebar.Views;

public sealed partial class FootingRebarView
{
    public FootingRebarView(FootingRebarViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.RequestClose += Close;
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;
        Closed += (_, _) =>
        {
            viewModel.RequestClose -= Close;
            Dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IsolatedFootingRebar");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "ui-errors.log"),
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        e.Handled = true;
    }

    private void SelectTabOnHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TabItem tabItem)
            tabItem.IsSelected = true;
    }
}
