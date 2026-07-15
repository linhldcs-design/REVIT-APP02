using Nice3point.Revit.Toolkit.External;
using Serilog;
using Serilog.Events;

namespace BeamRebar.Addin;

/// <summary>
///     Application entry point. Chỉ chạy khi add-in được nạp bằng manifest (.addin) — giữ cho
///     bản release chính thức về sau. KHI nạp qua Add-in Manager, OnStartup KHÔNG chạy: command
///     phải tự khởi tạo (self-contained), không dựa vào state set ở đây.
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
        var panel = Application.CreatePanel("BIM Tools", "BeamRebar");

        panel.AddPushButton<Commands.BeamRebarCommand>("Beam Rebar")
            .SetImage("/BeamRebar.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/BeamRebar.Addin;component/Resources/Icons/RibbonIcon32.png");
    }
}

/// <summary>
///     Khởi tạo Serilog idempotent — gọi được từ cả OnStartup lẫn trực tiếp trong command
///     (khi nạp qua Add-in Manager, Application không chạy nên command tự gọi).
/// </summary>
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
