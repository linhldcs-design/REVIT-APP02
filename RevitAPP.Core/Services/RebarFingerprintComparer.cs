using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Services;

public static class RebarFingerprintComparer
{
    public static bool AreEquivalent(
        RebarStationFingerprint first,
        RebarStationFingerprint second,
        RebarFingerprintTolerance tolerance)
    {
        if (first == null) throw new ArgumentNullException(nameof(first));
        if (second == null) throw new ArgumentNullException(nameof(second));
        if (tolerance == null) throw new ArgumentNullException(nameof(tolerance));
        Validate(tolerance);

        if (first.IsUncertain || second.IsUncertain) return false;
        if (first.HasAdditionalReinforcement != second.HasAdditionalReinforcement) return false;
        if (!Near(first.WidthFeet, second.WidthFeet, tolerance.SectionFeet) ||
            !Near(first.HeightFeet, second.HeightFeet, tolerance.SectionFeet)) return false;

        var firstLayers = first.LongitudinalLayers
            .OrderBy(layer => layer.ElevationFeet).ThenBy(layer => layer.DiameterMm).ThenBy(layer => layer.Quantity)
            .ToList();
        var secondLayers = second.LongitudinalLayers
            .OrderBy(layer => layer.ElevationFeet).ThenBy(layer => layer.DiameterMm).ThenBy(layer => layer.Quantity)
            .ToList();
        if (firstLayers.Count != secondLayers.Count) return false;
        for (var i = 0; i < firstLayers.Count; i++)
        {
            if (!Near(firstLayers[i].ElevationFeet, secondLayers[i].ElevationFeet,
                    tolerance.LayerElevationFeet) ||
                !Near(firstLayers[i].DiameterMm, secondLayers[i].DiameterMm, tolerance.DiameterMm) ||
                firstLayers[i].Quantity != secondLayers[i].Quantity) return false;
        }

        var firstStirrups = first.StirrupZones
            .OrderBy(zone => zone.DiameterMm).ThenBy(zone => zone.SpacingFeet).ToList();
        var secondStirrups = second.StirrupZones
            .OrderBy(zone => zone.DiameterMm).ThenBy(zone => zone.SpacingFeet).ToList();
        if (firstStirrups.Count != secondStirrups.Count) return false;
        for (var i = 0; i < firstStirrups.Count; i++)
        {
            if (!Near(firstStirrups[i].DiameterMm, secondStirrups[i].DiameterMm, tolerance.DiameterMm) ||
                !Near(firstStirrups[i].SpacingFeet, secondStirrups[i].SpacingFeet,
                    tolerance.StirrupSpacingFeet)) return false;
        }

        return true;
    }

    private static bool Near(double first, double second, double tolerance) =>
        Math.Abs(first - second) <= tolerance;

    private static void Validate(RebarFingerprintTolerance tolerance)
    {
        if (tolerance.SectionFeet < 0 || tolerance.LayerElevationFeet < 0 ||
            tolerance.DiameterMm < 0 || tolerance.StirrupSpacingFeet < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance không được âm.");
    }
}
