using FootingDrawing.Addin.Commands;
using Nice3point.Revit.Toolkit.External;

namespace FootingDrawing.Addin;

/// <summary>Application entry point — Revit gọi khi load add-in.</summary>
[UsedImplicitly]
public class Application : ExternalApplication
{
    public override void OnStartup()
    {
        Host.Start();
        CreateRibbon();
    }

    private void CreateRibbon()
    {
        var panel = Application.CreatePanel("Bản Vẽ Móng", "Triển Khai Thép Móng");

        var button = panel.AddPushButton<FootingDrawingCommand>("Bản Vẽ\nMóng");
        // Icon optional — chỉ set nếu resource tồn tại (tránh crash khi chưa có PNG).
        button.SetImage("/FootingDrawing.Addin;component/Resources/Icons/RibbonIcon16.png");
        button.SetLargeImage("/FootingDrawing.Addin;component/Resources/Icons/RibbonIcon32.png");
    }
}
