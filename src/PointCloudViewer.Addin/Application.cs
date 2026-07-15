using Nice3point.Revit.Toolkit.External;
using PointCloudViewer.Addin.Commands;
using PointCloudViewer.Addin.Services;
using Serilog;

namespace PointCloudViewer.Addin;

/// <summary>
///     Application entry point.
/// </summary>
[UsedImplicitly]
public class Application : ExternalApplication
{
    public override void OnStartup()
    {
        Host.Start();
        Host.GetService<PointCloudRenderCoordinator>().Register();
        TryRegisterDirectContext3D();
        CreateRibbon();
    }

    public override void OnShutdown()
    {
        try
        {
            Host.GetService<PointCloudRenderCoordinator>().Unregister();
            Host.GetService<PointCloudDirectContext3DService>().Unregister();
        }
        catch
        {
            // Revit is shutting down; do not block unload on cleanup failures.
        }
    }

    private static void TryRegisterDirectContext3D()
    {
        try
        {
            Host.GetService<PointCloudDirectContext3DService>().Register();
        }
        catch (Exception exception)
        {
            Host.GetService<ILogger>().Warning(exception, "DirectContext3D registration failed during startup");
        }
    }

    private void CreateRibbon()
    {
        var panel = Application.CreatePanel("Visualization", "Point Cloud");

        panel.AddPushButton<ShowSettingsCommand>("Settings")
            .SetImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon32.png");

        panel.AddPushButton<RgbModeCommand>("RGB")
            .SetImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon32.png");

        panel.AddPushButton<NormalModeCommand>("Normal")
            .SetImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon32.png");

        panel.AddPushButton<XRayModeCommand>("X-ray")
            .SetImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon32.png");

        panel.AddPushButton<ColorMapModeCommand>("Color Map")
            .SetImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon32.png");

        panel.AddPushButton<UpdateRenderCommand>("Update")
            .SetImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon32.png");

        panel.AddPushButton<ManagePointCloudsCommand>("Manage")
            .SetImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/PointCloudViewer.Addin;component/Resources/Icons/RibbonIcon32.png");
    }
}
