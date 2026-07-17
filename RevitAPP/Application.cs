using Nice3point.Revit.Toolkit.External;
using RevitAPP.Chat;
using RevitAPP.Commands;
using BeamRebarStartupCommand = BeamRebarPro.Commands.StartupCommand;
using FootingDrawingCommand = FootingDrawing.Addin.Commands.FootingDrawingCommand;
using FootingRebarStartupCommand = IsolatedFootingRebar.Commands.StartupCommand;
using WallRebarStartupCommand = WallRebar.Commands.StartupCommand;
using RevitAPP.Services.PointCloud;
using RevitAPP.Services.Updates;
using Serilog;
using Serilog.Events;

namespace RevitAPP
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        public override void OnStartup()
        {
            CreateLogger();
            BeamRebarPro.Host.Start();
            IsolatedFootingRebar.Host.Start();
            WallRebar.Host.Start();
            ChatHost.Start();
            PointCloudPanelRegistry.Register(Application);
            CreateRibbon();
            UpdateStartupCoordinator.Start(Application);
        }

        public override void OnShutdown()
        {
            Log.CloseAndFlush();
        }

        private void CreateRibbon()
        {
            var panel = Application.CreatePanel("Commands", "RevitAPP");

            panel.AddPushButton<LicenseCommand>("License")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<HelloWorldCommand>("Hello World")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<TranslateTextCommand>("Dich Text")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<RenumberScheduleCommand>("Danh So Schedule")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<DrawColumnRebarCommand>("Ve Thep Cot")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<BeamDrawingCommand>("Ban Ve Dam")
                .SetImage("/RevitAPP;component/Resources/Icons/BeamDrawingIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/BeamDrawingIcon32.png");

            panel.AddPushButton<BeamRebarStartupCommand>("Ve Thep Dam")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<FootingRebarStartupCommand>("Ve Mong Don")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<FootingDrawingCommand>("Ban Ve Mong")
                .SetImage("/RevitAPP;component/Resources/Icons/FootingDrawingIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/FootingDrawingIcon32.png");

            panel.AddPushButton<FootingSectionDrawingCommand>("Mat Cat Mong")
                .SetImage("/RevitAPP;component/Resources/Icons/FootingDrawingIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/FootingDrawingIcon32.png");

            panel.AddPushButton<WallRebarStartupCommand>("Ve Thep Tuong")
                .SetImage("/RevitAPP;component/Resources/Icons/WallRebarIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/WallRebarIcon32.png");

            panel.AddPushButton<AlignSheetViewportsCommand>("Can Chinh View")
                .SetImage("/RevitAPP;component/Resources/Icons/AlignViewIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/AlignViewIcon32.png");

            panel.AddPushButton<TogglePointCloudPanelCommand>("Point Cloud")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<PointCloudPocCommand>("PC POC")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");

            panel.AddPushButton<ChatCommand>("Chat AI")
                .SetImage("/RevitAPP;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RibbonIcon32.png");
        }

        private static void CreateLogger()
        {
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
}
