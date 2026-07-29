using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Chat.BeamLongitudinalDrawing;

public static class LongitudinalDrawingPresetFinder
{
    public static IReadOnlyList<LongitudinalDrawingSetting> Find(
        IEnumerable<LongitudinalDrawingSetting> presets, string? query)
    {
        var named = presets.Where(item => !string.IsNullOrWhiteSpace(item.SettingName)).ToList();
        if (string.IsNullOrWhiteSpace(query)) return named;

        var value = query.Trim();
        return named
            .Where(item => (item.SettingName ?? string.Empty)
                .Contains(value, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => string.Equals(
                item.SettingName ?? string.Empty, value, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
