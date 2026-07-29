namespace RevitAPP.Core.Chat.BeamLongitudinalDrawing;

public sealed record LongitudinalBatchAssignment(long BeamId, string SheetNumber, int SheetIndex);

public static class LongitudinalBatchAssignmentPlanner
{
    public static IReadOnlyList<LongitudinalBatchAssignment> Plan(
        IReadOnlyList<long> beamIds,
        int beamsPerSheet,
        IReadOnlyList<string> sheetNumbers)
    {
        if (beamIds.Count == 0) throw new ArgumentException("Phải chọn ít nhất một dầm.", nameof(beamIds));
        if (beamIds.Any(id => id <= 0)) throw new ArgumentException("beamIds phải là số dương.", nameof(beamIds));
        if (beamIds.Distinct().Count() != beamIds.Count)
            throw new ArgumentException("beamIds không được trùng.", nameof(beamIds));
        if (beamsPerSheet <= 0) throw new ArgumentOutOfRangeException(nameof(beamsPerSheet));
        if (sheetNumbers.Count == 0)
            throw new ArgumentException("Phải cung cấp ít nhất một sheet.", nameof(sheetNumbers));

        var normalizedSheets = sheetNumbers.Select(value => value?.Trim() ?? string.Empty).ToList();
        if (normalizedSheets.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Số sheet không được để trống.", nameof(sheetNumbers));
        if (normalizedSheets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedSheets.Count)
            throw new ArgumentException("Số sheet không được trùng.", nameof(sheetNumbers));
        if ((long)beamsPerSheet * normalizedSheets.Count < beamIds.Count)
            throw new ArgumentException("Các sheet đã chọn không đủ sức chứa số dầm.");

        return beamIds.Select((beamId, index) =>
        {
            var sheetIndex = index / beamsPerSheet;
            return new LongitudinalBatchAssignment(beamId, normalizedSheets[sheetIndex], sheetIndex);
        }).ToList();
    }
}
