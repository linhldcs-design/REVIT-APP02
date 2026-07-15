using Nice3point.Revit.Toolkit.External;
using BeamRebarPro.Commands;

namespace BeamRebarPro;

/// <summary>
///     Application entry point
/// </summary>
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
        var panel = Application.CreatePanel("Thép Dầm", "Vẽ Dầm");

        panel.AddPushButton<StartupCommand>("Vẽ Dầm")
            .SetImage("/BeamRebarPro;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/BeamRebarPro;component/Resources/Icons/RibbonIcon32.png");
    }
}