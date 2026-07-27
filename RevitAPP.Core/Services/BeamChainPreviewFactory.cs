using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Services;

public static class BeamChainPreviewFactory
{
    public static BeamChainPreviewModel Create(
        BeamChainModel chain, IReadOnlyList<SectionStation> stations, bool reverse = false)
    {
        if (chain == null) throw new ArgumentNullException(nameof(chain));
        if (stations == null) throw new ArgumentNullException(nameof(stations));
        var starts = new double[chain.Spans.Count];
        var distance = 0d;
        for (var i = 0; i < chain.Spans.Count; i++)
        {
            starts[i] = distance;
            distance += chain.Spans[i].LengthFeet;
        }

        var spans = chain.Spans.Select((span, i) => new BeamChainPreviewSpan(
            span.SourceId,
            reverse ? chain.Spans.Count - i : i + 1,
            Map(starts[i], chain.TotalLengthFeet, reverse),
            Map(starts[i] + span.LengthFeet, chain.TotalLengthFeet, reverse),
            $"Dầm {span.SourceId} · {span.LengthFeet * 304.8:0} mm"))
            .OrderBy(item => Math.Min(item.StartFeet, item.EndFeet)).ToList();
        var previewStations = stations.Select(station => new BeamChainPreviewStation(
                ReverseKind(station.Kind, reverse),
                Map(station.ChainDistanceFeet, chain.TotalLengthFeet, reverse),
                Label(ReverseKind(station.Kind, reverse))))
            .OrderBy(item => item.ChainDistanceFeet).ToList();
        return new BeamChainPreviewModel(spans, previewStations, chain.TotalLengthFeet, reverse, []);
    }

    public static double ProjectX(double chainDistanceFeet, double totalFeet, double width, double padding)
    {
        if (totalFeet <= 0 || width <= padding * 2) return width * 0.5;
        var normalized = MathCompat.Clamp(chainDistanceFeet / totalFeet, 0, 1);
        return padding + normalized * (width - padding * 2);
    }

    private static double Map(double value, double total, bool reverse) => reverse ? total - value : value;
    private static SectionStationKind ReverseKind(SectionStationKind kind, bool reverse) => !reverse ? kind : kind switch
    {
        SectionStationKind.LeftSupport => SectionStationKind.RightSupport,
        SectionStationKind.RightSupport => SectionStationKind.LeftSupport,
        _ => kind
    };
    private static string Label(SectionStationKind kind) => kind switch
    {
        SectionStationKind.LeftSupport => "Gối trái",
        SectionStationKind.RightSupport => "Gối phải",
        SectionStationKind.SharedSupport => "Gối chung",
        _ => "Giữa nhịp"
    };
}
