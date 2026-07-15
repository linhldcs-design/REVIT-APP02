using Nice3point.Revit.Toolkit.External;
using IsolatedFootingRebar.Commands;

namespace IsolatedFootingRebar;

/// <summary>
///     Application entry point.
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
        var panel = Application.CreatePanel("Thép Móng", "Vẽ Móng Đơn");

        panel.AddPushButton<StartupCommand>("Vẽ Móng Đơn")
            .SetImage("/IsolatedFootingRebar;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/IsolatedFootingRebar;component/Resources/Icons/RibbonIcon32.png");
    }
}
