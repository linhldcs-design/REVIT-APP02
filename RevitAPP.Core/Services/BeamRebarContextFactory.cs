using RevitAPP.Core.Models;

namespace RevitAPP.Core.Services;

/// <summary>Gối đỡ trên tuyến dầm, đo bằng khoảng cách từ đầu tuyến.</summary>
public readonly record struct BeamSupportMarker(double StationMm, double HalfWidthMm);

/// <summary>
/// Khối bê tông vẽ nền cho thép: bản thân dầm, cột đỡ và dầm giao.
/// Cột và dầm giao chỉ biết nửa bề rộng theo phương dầm, nên khối của chúng là xấp xỉ đủ để định vị
/// thị giác — chúng được vẽ dạng khung rỗng nên sai lệch không che mất thép.
/// </summary>
public static class BeamRebarContextFactory
{
    /// <summary>Chiều cao khối cột vẽ minh hoạ, tính theo bội số chiều cao dầm.</summary>
    private const double ColumnHeightFactor = 2.0;

    public static IReadOnlyList<BeamRebarContextVolume> Build(
        IReadOnlyList<PureSpanFrame> spans,
        IReadOnlyList<BeamSupportMarker>? supports = null,
        IReadOnlyList<BeamSupportMarker>? crossBeams = null)
    {
        var volumes = new List<BeamRebarContextVolume>();
        if (spans.Count == 0) return volumes;

        foreach (var span in spans)
        {
            volumes.Add(new BeamRebarContextVolume(
                BeamRebarContextKind.Beam,
                CentreOfSection(span, 0),
                CentreOfSection(span, span.LengthMm),
                span.WidthMm,
                span.HeightMm));
        }

        var reference = spans[0];
        var totalLength = spans.Sum(s => s.LengthMm);

        foreach (var support in supports ?? [])
            volumes.Add(ColumnVolume(spans, support, totalLength, reference));

        foreach (var crossBeam in crossBeams ?? [])
            volumes.Add(CrossBeamVolume(spans, crossBeam, totalLength, reference));

        return volumes;
    }

    /// <summary>Cột đỡ: khối đứng xuyên qua dầm, đủ cao để thấy dầm tựa lên đâu.</summary>
    private static BeamRebarContextVolume ColumnVolume(
        IReadOnlyList<PureSpanFrame> spans, BeamSupportMarker support, double totalLength, PureSpanFrame reference)
    {
        var centre = PointOnRun(spans, support.StationMm, totalLength);
        var height = reference.HeightMm * ColumnHeightFactor;
        var width = Math.Max(support.HalfWidthMm * 2, reference.WidthMm);

        return new BeamRebarContextVolume(
            BeamRebarContextKind.Column,
            new GeometryPoint3D(centre.Xmm, centre.Ymm, centre.Zmm - height / 2),
            new GeometryPoint3D(centre.Xmm, centre.Ymm, centre.Zmm + height / 2),
            width,
            width);
    }

    /// <summary>Dầm giao: khối ngang cắt qua dầm chính, vẽ vuông góc tuyến dầm.</summary>
    private static BeamRebarContextVolume CrossBeamVolume(
        IReadOnlyList<PureSpanFrame> spans, BeamSupportMarker crossBeam, double totalLength, PureSpanFrame reference)
    {
        var centre = PointOnRun(spans, crossBeam.StationMm, totalLength);
        var reach = Math.Max(reference.WidthMm, reference.HeightMm);
        var across = reference.Across;

        return new BeamRebarContextVolume(
            BeamRebarContextKind.CrossBeam,
            new GeometryPoint3D(centre.Xmm - across.X * reach, centre.Ymm - across.Y * reach, centre.Zmm),
            new GeometryPoint3D(centre.Xmm + across.X * reach, centre.Ymm + across.Y * reach, centre.Zmm),
            Math.Max(crossBeam.HalfWidthMm * 2, reference.WidthMm / 2),
            reference.HeightMm);
    }

    /// <summary>Tâm tiết diện tại một vị trí dọc nhịp, ở giữa chiều cao dầm.</summary>
    private static GeometryPoint3D CentreOfSection(PureSpanFrame span, double stationMm) =>
        span.PointAtStation(stationMm, 0, -span.HeightMm / 2);

    /// <summary>Điểm trên tuyến dầm nhiều nhịp, tính từ đầu tuyến.</summary>
    private static GeometryPoint3D PointOnRun(
        IReadOnlyList<PureSpanFrame> spans, double stationMm, double totalLength)
    {
        var remaining = MathCompat.Clamp(stationMm, 0, totalLength);
        foreach (var span in spans)
        {
            if (remaining <= span.LengthMm)
                return CentreOfSection(span, remaining);
            remaining -= span.LengthMm;
        }

        var last = spans[^1];
        return CentreOfSection(last, last.LengthMm);
    }
}
