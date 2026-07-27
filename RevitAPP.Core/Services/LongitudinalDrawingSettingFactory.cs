using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Services;

public static class LongitudinalDrawingSettingFactory
{
    public static LongitudinalDrawingSetting CreateDefault() => new(
        null, 25, "", "", "", "", "", "", "", "", null, null, "",
        "KC-001", "CHI TIẾT THÉP DẦM", 200, 5, 10);
}
