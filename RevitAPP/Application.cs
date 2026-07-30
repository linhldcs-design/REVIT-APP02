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
            ChatHost.BindRevitBridge();
            ChatHost.StartMcpServer();
            PointCloudPanelRegistry.Register(Application);
            CreateRibbon();
            UpdateStartupCoordinator.Start(Application);
        }

        public override void OnShutdown()
        {
            ChatHost.Stop();
            Log.CloseAndFlush();
        }

        private void CreateRibbon()
        {
            const string ribbonTabName = "LDL-STRUCTURAL";
            var commandsPanel = Application.CreatePanel("Commands", ribbonTabName);
            var rebarPanel = Application.CreatePanel("Rebar", ribbonTabName);
            var drawingRebarPanel = Application.CreatePanel("Drawing Rebar", ribbonTabName);

            commandsPanel.AddPushButton<LicenseCommand>("License")
                .SetImage("/RevitAPP;component/Resources/Icons/LicenseIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/LicenseIcon32.png");

            commandsPanel.AddPushButton<HelloWorldCommand>("Hello World")
                .SetImage("/RevitAPP;component/Resources/Icons/HelloWorldIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/HelloWorldIcon32.png");

            commandsPanel.AddPushButton<TranslateTextCommand>("Dich Text")
                .SetImage("/RevitAPP;component/Resources/Icons/TranslateTextIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/TranslateTextIcon32.png");

            commandsPanel.AddPushButton<RenumberScheduleCommand>("Danh So Schedule")
                .SetImage("/RevitAPP;component/Resources/Icons/RenumberScheduleIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/RenumberScheduleIcon32.png");

            commandsPanel.AddPushButton<ToggleGridExtentCommand>("Luoi 3D/2D")
                .SetImage("/RevitAPP;component/Resources/Icons/GridExtentIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/GridExtentIcon32.png");

            rebarPanel.AddPushButton<DrawColumnRebarCommand>("Ve Thep Cot")
                .SetImage("/RevitAPP;component/Resources/Icons/ColumnRebarIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/ColumnRebarIcon32.png");

            drawingRebarPanel.AddPushButton<BeamDrawingCommand>("Mat Cat Ngang Dam")
                .SetImage("/RevitAPP;component/Resources/Icons/BeamDrawingIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/BeamDrawingIcon32.png");

            drawingRebarPanel.AddPushButton<BeamLongitudinalDrawingCommand>("Mat Cat Doc Dam")
                .SetImage("/RevitAPP;component/Resources/Icons/BeamLongitudinalIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/BeamLongitudinalIcon32.png");

            rebarPanel.AddPushButton<BeamRebarStartupCommand>("Ve Thep Dam")
                .SetImage("/RevitAPP;component/Resources/Icons/BeamRebarIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/BeamRebarIcon32.png");

            rebarPanel.AddPushButton<FootingRebarStartupCommand>("Ve Mong Don")
                .SetImage("/RevitAPP;component/Resources/Icons/FootingRebarIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/FootingRebarIcon32.png");

            drawingRebarPanel.AddPushButton<FootingDrawingCommand>("Mat Bang Mong")
                .SetImage("/RevitAPP;component/Resources/Icons/FootingDrawingIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/FootingDrawingIcon32.png");

            drawingRebarPanel.AddPushButton<FootingSectionDrawingCommand>("Mat Cat Mong")
                .SetImage("/RevitAPP;component/Resources/Icons/FootingSectionIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/FootingSectionIcon32.png");

            rebarPanel.AddPushButton<WallRebarStartupCommand>("Ve Thep Tuong")
                .SetImage("/RevitAPP;component/Resources/Icons/WallRebarIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/WallRebarIcon32.png");

            commandsPanel.AddPushButton<AlignSheetViewportsCommand>("Can Chinh View")
                .SetImage("/RevitAPP;component/Resources/Icons/AlignViewIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/AlignViewIcon32.png");

            commandsPanel.AddPushButton<TogglePointCloudPanelCommand>("Point Cloud")
                .SetImage("/RevitAPP;component/Resources/Icons/PointCloudIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/PointCloudIcon32.png");

            commandsPanel.AddPushButton<PointCloudPocCommand>("PC POC")
                .SetImage("/RevitAPP;component/Resources/Icons/PointCloudPocIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/PointCloudPocIcon32.png");

            commandsPanel.AddPushButton<ChatCommand>("Chat AI")
                .SetImage("/RevitAPP;component/Resources/Icons/ChatAiIcon16.png")
                .SetLargeImage("/RevitAPP;component/Resources/Icons/ChatAiIcon32.png");
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
