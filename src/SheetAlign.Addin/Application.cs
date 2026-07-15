using Nice3point.Revit.Toolkit.External;
using Serilog;
using Serilog.Events;

namespace SheetAlign.Addin;

/// <summary>
///     Application entry point. Tạo ribbon khi add-in nạp bằng manifest (.addin). Khi nạp qua
///     Add-in Manager, OnStartup KHÔNG chạy — command tự khởi tạo logger (self-contained).
/// </summary>
[UsedImplicitly]
public class Application : ExternalApplication
{
    public override void OnStartup()
    {
        LoggerSetup.EnsureConfigured();
        CreateRibbon();
    }

    public override void OnShutdown()
    {
        Log.CloseAndFlush();
    }

    private void CreateRibbon()
    {
        var panel = Application.CreatePanel("Sheet Tools", "SheetAlign");

        panel.AddPushButton<Commands.AlignSheetViewportsCommand>("Can Chinh View")
            .SetImage("/SheetAlign.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/SheetAlign.Addin;component/Resources/Icons/RibbonIcon32.png");
    }
}

/// <summary>Khởi tạo Serilog idempotent — gọi được từ OnStartup lẫn trực tiếp trong command.</summary>
internal static class LoggerSetup
{
    private static bool _configured;

    public static void EnsureConfigured()
    {
        if (_configured) return;
        _configured = true;

        const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Debug(LogEventLevel.Debug, outputTemplate)
            .MinimumLevel.Debug()
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = (Exception)args.ExceptionObject;
            Log.Fatal(exception, "Domain unhandled exception");
        };
    }
}
