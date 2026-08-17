using BeamRebarPro.Models;
using RevitAPP.Core.Models;
using RevitAPP.Core.Services;

namespace BeamRebarPro.Services;

/// <summary>
/// Dịch cấu hình người dùng nhập thành bản mô tả hình học thép để xem trước.
/// Chuyển đổi feet sang mm xảy ra tại đây; lớp hình học bên dưới chỉ làm việc với mm.
/// </summary>
public static class BeamRebarPreviewService
{
    private const double MmPerFoot = 304.8;

    /// <summary>Tiết diện giả định khi chưa chọn dầm, để khung xem trước vẫn có nội dung.</summary>
    private const double FallbackWidthMm = 300;
    private const double FallbackHeightMm = 600;
    private const double FallbackSpanLengthMm = 6000;

    /// <summary>
    /// Dựng bản xem trước từ cấu hình hiện tại và các nhịp đã đọc được từ dầm đang chọn.
    /// Trả về bản rỗng nếu cấu hình chưa dựng được hình.
    /// </summary>
    public static BeamRebarGeometryPlan Build(
        QuickSettingModel model,
        IReadOnlyList<SpanInfo> spans,
        IReadOnlyList<SecondaryBeamInfo>? secondaryBeams = null)
    {
        var effectiveSpans = spans.Count > 0
            ? spans
            : [new SpanInfo(0, FallbackSpanLengthMm)];

        var cover = new BeamCoverMm(model.Cover.TopMm, model.Cover.BottomMm, model.Cover.SideMm);
        var requests = new List<BeamSpanRequest>();
        var frames = new List<PureSpanFrame>();
        var supports = new List<BeamSupportMarker>();
        var crossBeams = new List<BeamSupportMarker>();
        var runOffset = 0d;

        foreach (var span in effectiveSpans)
        {
            var frame = SpanFrameFor(span, runOffset);
            frames.Add(frame);

            var secondaryInSpan = SecondaryStations(secondaryBeams, frame);

            // Mỗi nhịp mang đai và thép gia cường dưới của chính nó. Thép chủ và thép gia cường trên
            // vắt qua gối nên dựng trên khung toàn tuyến bên dưới — đúng như thép được tạo thật.
            requests.Add(new BeamSpanRequest(
                frame, cover,
                BottomAdditionalRequests(model, span),
                StirrupRequest(model, secondaryInSpan)));

            supports.Add(new BeamSupportMarker(runOffset, span.LeftColumnHalfWidthMm));
            foreach (var (stationInSpan, halfWidth) in secondaryInSpan)
                crossBeams.Add(new BeamSupportMarker(runOffset + stationInSpan, halfWidth));

            runOffset += frame.LengthMm;
        }

        if (effectiveSpans.Count > 0)
            supports.Add(new BeamSupportMarker(runOffset, effectiveSpans[^1].RightColumnHalfWidthMm));

        // Thanh chủ liền mạch từ đầu tới cuối tuyến, và thép gia cường trên vắt qua từng gối. Vẫn khai
        // báo cốt đai để thanh lùi vào nằm trong đai, nhưng không sinh thêm đai lần nữa.
        var runFrame = FullRunFrame(frames);
        if (runFrame is not null)
        {
            var runBars = new List<BeamLongitudinalRequest>();
            AddMainBars(runBars, model);
            AddTopAdditionalAtSupports(runBars, model, effectiveSpans, runFrame.LengthMm);

            requests.Add(new BeamSpanRequest(
                runFrame, cover, runBars,
                StirrupRequest(model, [], generateStirrups: false)));
        }

        try
        {
            return BeamRebarPlanBuilder.Build(requests, supports, crossBeams, frames);
        }
        catch (ArgumentException)
        {
            // Cấu hình sinh quá nhiều thanh để vẽ. Khung xem trước để trống thay vì làm treo giao diện.
            return BeamRebarGeometryPlan.Empty;
        }
        catch (InvalidOperationException)
        {
            // Nhịp suy biến (dài 0 hoặc dựng đứng) — chưa dựng được hình.
            return BeamRebarGeometryPlan.Empty;
        }
    }

    /// <summary>
    /// Hệ trục của nhịp. Khi đã chọn dầm thì dùng thẳng toạ độ, tiết diện và cao độ thật đọc từ mô
    /// hình, để bản xem trước trùng khít với thép sẽ được tạo. Chỉ khi chưa chọn dầm mới dựng một
    /// nhịp mẫu để khung xem trước có nội dung.
    /// </summary>
    private static PureSpanFrame SpanFrameFor(SpanInfo span, double runOffsetMm)
    {
        if (span.HasRealGeometry)
        {
            return new PureSpanFrame(
                new GeometryPoint3D(span.StartXMm, span.StartYMm, span.TopElevationMm - span.SectionHeightMm),
                new GeometryPoint3D(span.EndXMm, span.EndYMm, span.TopElevationMm - span.SectionHeightMm),
                span.SectionWidthMm,
                span.SectionHeightMm,
                span.TopElevationMm,
                span.LateralOffsetMm,
                span.Index);
        }

        // Chưa có hình học thật: xếp các nhịp nối tiếp nhau dọc trục X để tuyến dầm vẫn liền mạch.
        var length = span.LengthMm > 1 ? span.LengthMm : FallbackSpanLengthMm;
        return new PureSpanFrame(
            new GeometryPoint3D(runOffsetMm, 0, 0),
            new GeometryPoint3D(runOffsetMm + length, 0, 0),
            FallbackWidthMm, FallbackHeightMm,
            topElevationMm: FallbackHeightMm, spanIndex: span.Index);
    }

    /// <summary>
    /// Khung chạy suốt tuyến dầm, ghép từ đầu nhịp đầu tới cuối nhịp cuối. Dùng cho thép chủ vì thanh
    /// chủ không đứt tại gối giữa.
    /// </summary>
    private static PureSpanFrame? FullRunFrame(IReadOnlyList<PureSpanFrame> frames)
    {
        if (frames.Count == 0) return null;
        if (frames.Count == 1) return frames[0];

        var first = frames[0];
        var last = frames[^1];
        var start = first.PointAtStation(0, 0, -first.HeightMm);
        var end = last.PointAtStation(last.LengthMm, 0, -last.HeightMm);

        try
        {
            return new PureSpanFrame(
                new GeometryPoint3D(start.Xmm, start.Ymm, first.TopElevationMm - first.HeightMm),
                new GeometryPoint3D(end.Xmm, end.Ymm, first.TopElevationMm - first.HeightMm),
                first.WidthMm, first.HeightMm, first.TopElevationMm);
        }
        catch (InvalidOperationException)
        {
            // Các nhịp gập lại thành chiều dài 0 — giữ nguyên nhịp đầu thay vì bỏ hẳn thép chủ.
            return first;
        }
    }

    /// <summary>Thép gia cường dưới của một nhịp, nằm giữa nhịp.</summary>
    private static IReadOnlyList<BeamLongitudinalRequest> BottomAdditionalRequests(
        QuickSettingModel model, SpanInfo span)
    {
        var requests = new List<BeamLongitudinalRequest>();
        AddBottomAdditional(requests, model, span);
        return requests;
    }

    private static void AddMainBars(List<BeamLongitudinalRequest> requests, QuickSettingModel model)
    {
        if (model.MainTop.Count > 0)
        {
            // Thép trên: đoạn neo hai đầu ưu tiên hơn chiều dài bẻ chung, và bẻ quặp xuống dưới.
            var leftBend = MainBarBendMm(model.MainTop.AnchorLeftMm, model.MainTop.TopEndBendDownLengthMm);
            var rightBend = MainBarBendMm(model.MainTop.AnchorRightMm, model.MainTop.TopEndBendDownLengthMm);

            requests.Add(new BeamLongitudinalRequest(
                BeamRebarPathKind.MainTop, model.MainTop.Count, model.MainTop.Diameter.Millimeters,
                StartT: 0, EndT: 1,
                StartBendMm: leftBend, EndBendMm: rightBend,
                BendDirection: leftBend + rightBend > 0 ? BarBendDirection.Down : BarBendDirection.None));
        }

        if (model.MainBottom.Count > 0)
        {
            // Thép dưới chỉ nhận đoạn neo, và bẻ quặp ngược lên trên.
            var leftBend = Math.Max(0, model.MainBottom.AnchorLeftMm);
            var rightBend = Math.Max(0, model.MainBottom.AnchorRightMm);

            requests.Add(new BeamLongitudinalRequest(
                BeamRebarPathKind.MainBottom, model.MainBottom.Count,
                model.MainBottom.Diameter.Millimeters, StartT: 0, EndT: 1,
                StartBendMm: leftBend, EndBendMm: rightBend,
                BendDirection: leftBend + rightBend > 0 ? BarBendDirection.Up : BarBendDirection.None));
        }
    }

    /// <summary>Đoạn bẻ đầu thép chủ trên: đoạn neo nhập riêng thắng chiều dài bẻ chung.</summary>
    private static double MainBarBendMm(double anchorMm, double bendDownMm) =>
        Math.Max(0, anchorMm > 0 ? anchorMm : bendDownMm);

    /// <summary>
    /// Thép gia cường trên: mỗi gối một thanh vắt qua, kéo dài về hai phía. Tại cột giữa thanh chạy
    /// xuyên liền; chỉ hai gối biên mới bẻ móc xuống.
    /// </summary>
    private static void AddTopAdditionalAtSupports(
        List<BeamLongitudinalRequest> requests, QuickSettingModel model,
        IReadOnlyList<SpanInfo> spans, double runLengthMm)
    {
        if (spans.Count == 0 || runLengthMm <= 1e-6) return;

        // Gối nằm ở hai đầu mỗi nhịp: n nhịp cho n+1 gối, tính bằng khoảng cách dồn dọc tuyến.
        var supportStations = new List<double> { 0 };
        var offset = 0d;
        foreach (var span in spans)
        {
            offset += span.LengthMm > 1 ? span.LengthMm : FallbackSpanLengthMm;
            supportStations.Add(offset);
        }

        var lastSupport = supportStations.Count - 1;

        // Màn chi tiết quản lý từng cây theo từng gối; khi có danh sách đó thì nó là nguồn duy nhất.
        // Chỉ khi trống mới dùng cấu hình gộp của màn ngoài — đúng như lệnh tạo thép.
        var configs = model.TopAdditionalItems.Count > 0
            ? model.TopAdditionalItems
            : [model.TopAdditional, model.TopAdditionalLayer2];

        foreach (var config in configs)
        {
            if (!config.Enabled || config.Count <= 0) continue;

            // Item chi tiết chỉ đích danh một gối; cấu hình gộp trải đều mọi gối.
            var firstIndex = model.TopAdditionalItems.Count > 0
                ? Math.Clamp(config.StartPointIndex, 0, lastSupport)
                : 0;
            var finalIndex = model.TopAdditionalItems.Count > 0
                ? Math.Clamp(config.EndPointIndex == int.MaxValue ? lastSupport : config.EndPointIndex, firstIndex, lastSupport)
                : lastSupport;

            for (var supportIndex = firstIndex; supportIndex <= finalIndex; supportIndex++)
            {
                var station = supportStations[supportIndex];
                var halfWidth = HalfWidthAtSupport(spans, supportIndex);
                var leftSpanLength = supportIndex > 0 ? SpanLength(spans, supportIndex - 1) : 0;
                var rightSpanLength = supportIndex < spans.Count ? SpanLength(spans, supportIndex) : 0;

                var leftExtend = ResolveExtendMm(config, config.LeftLengthMm, config.LeftRatio, leftSpanLength);
                var rightExtend = ResolveExtendMm(config, config.RightLengthMm, config.RightRatio, rightSpanLength);

                var startMm = Math.Max(0, station - halfWidth - (supportIndex > 0 ? leftExtend : 0));
                var endMm = Math.Min(runLengthMm, station + halfWidth + (supportIndex < spans.Count ? rightExtend : 0));
                if (endMm - startMm <= 60) continue;

                // Móc bẻ chỉ có ở gối đầu và gối cuối tuyến; qua cột giữa thanh chạy thẳng xuyên qua.
                // Ô "Edge hook" ở màn ngoài chính là chiều dài móc này khi chưa nhập riêng từng bên.
                var hookMm = config.DLeftMm > 0 || config.DRightMm > 0
                    ? 0
                    : config.EdgeHookDownLengthMm;
                var bendStart = supportIndex == 0 ? Math.Max(config.DLeftMm, hookMm) : 0;
                var bendEnd = supportIndex == lastSupport ? Math.Max(config.DRightMm, hookMm) : 0;

                requests.Add(new BeamLongitudinalRequest(
                    BeamRebarPathKind.AdditionalTop, config.Count, config.Diameter.Millimeters,
                    startMm / runLengthMm, endMm / runLengthMm, config.Layer,
                    bendStart, bendEnd,
                    bendStart + bendEnd > 0 ? BarBendDirection.Down : BarBendDirection.None,
                    config.PositionInSection, model.MainTop.Count,
                    SpreadAcrossFullWidth: config.Layer >= 2));
            }
        }
    }

    private static double SpanLength(IReadOnlyList<SpanInfo> spans, int index) =>
        spans[index].LengthMm > 1 ? spans[index].LengthMm : FallbackSpanLengthMm;

    /// <summary>Nửa bề rộng gối: gối trong lấy theo mép trái của nhịp bên phải nó.</summary>
    private static double HalfWidthAtSupport(IReadOnlyList<SpanInfo> spans, int supportIndex) =>
        supportIndex < spans.Count
            ? spans[supportIndex].LeftColumnHalfWidthMm
            : spans[^1].RightColumnHalfWidthMm;

    /// <summary>
    /// Đoạn vươn mỗi bên gối. Thứ tự ưu tiên giữ đúng như lệnh tạo thép: chiều dài nhập riêng cho bên
    /// đó, rồi tỉ lệ nhịp, rồi chiều dài chung; chỉ khi không có số nào mới lấy 1/4 nhịp.
    /// </summary>
    private static double ResolveExtendMm(
        AdditionalBarConfig config, double sideLengthMm, double sideRatio, double spanLengthMm)
    {
        if (sideLengthMm > 0) return sideLengthMm;
        if (sideRatio > 0 && spanLengthMm > 0) return spanLengthMm * sideRatio;
        if (config.LengthMm > 0) return config.LengthMm;
        return spanLengthMm * 0.25;
    }

    /// <summary>Thép gia cường dưới nằm giữa nhịp, đo trên khoảng thông thủy giữa hai mép cột.</summary>
    private static void AddBottomAdditional(
        List<BeamLongitudinalRequest> requests, QuickSettingModel model, SpanInfo span)
    {
        var spanLength = span.LengthMm > 1 ? span.LengthMm : FallbackSpanLengthMm;
        var clearStartT = Math.Clamp(span.LeftColumnHalfWidthMm / spanLength, 0, .45);
        var clearEndT = Math.Clamp(1 - span.RightColumnHalfWidthMm / spanLength, .55, 1);
        var clearFraction = clearEndT - clearStartT;
        if (clearFraction <= 1e-6) return;

        // Màn chi tiết quản lý từng cây theo từng nhịp; khi có danh sách đó thì nó là nguồn duy nhất.
        var configs = model.BottomAdditionalItems.Count > 0
            ? model.BottomAdditionalItems.Where(c => c.StartPointIndex == span.Index && c.EndPointIndex == span.Index + 1).ToList()
            : [model.BottomAdditional, model.BottomAdditionalLayer2];

        foreach (var config in configs)
        {
            if (!config.Enabled || config.Count <= 0) continue;

            var startT = clearStartT + clearFraction / 8;
            var endT = clearStartT + clearFraction * 7 / 8;

            if (config.LengthMm > 0)
            {
                var fraction = Math.Min(clearFraction, config.LengthMm / spanLength);
                var midT = (clearStartT + clearEndT) / 2;
                startT = midT - fraction / 2;
                endT = midT + fraction / 2;
            }

            requests.Add(new BeamLongitudinalRequest(
                BeamRebarPathKind.AdditionalBottom, config.Count, config.Diameter.Millimeters,
                startT, endT, config.Layer,
                PositionInSection: config.PositionInSection,
                MainBarCount: model.MainBottom.Count,
                SpreadAcrossFullWidth: config.Layer >= 2));
        }
    }

    private static BeamStirrupRequest StirrupRequest(
        QuickSettingModel model,
        IReadOnlyList<(double StationMm, double HalfWidthMm)> secondaryInSpan,
        bool generateStirrups = true)
    {
        var stirrup = model.Stirrup;
        return new BeamStirrupRequest(
            stirrup.Diameter.Millimeters,
            stirrup.SpacingEndMm,
            stirrup.SpacingMidMm,
            stirrup.Mode == StirrupMode.TwoEnds,
            stirrup.EndZoneLengthMm,
            stirrup.EndZoneStartMm,
            stirrup.EndZoneEndMm,
            secondaryInSpan,
            stirrup.AdditionalStirrups
                .Where(a => a.Enabled)
                .Select(a => new BeamAdditionalStirrupRequest(
                    a.Diameter.Millimeters,
                    a.Type == AdditionalStirrupType.Closed,
                    a.StartBar,
                    a.EndBar))
                .ToList(),
            model.MainTop.Count,
            model.MainTop.Diameter.Millimeters,
            generateStirrups);
    }

    /// <summary>
    /// Vị trí dầm phụ quy về khoảng cách dọc trục nhịp, bằng cách chiếu điểm giao lên trục dầm thật.
    /// Dầm có thể nằm xiên trong mặt bằng nên không thể lấy riêng một trục toạ độ.
    /// </summary>
    private static IReadOnlyList<(double StationMm, double HalfWidthMm)> SecondaryStations(
        IReadOnlyList<SecondaryBeamInfo>? secondaryBeams, PureSpanFrame frame)
    {
        if (secondaryBeams is not { Count: > 0 }) return [];

        var stations = new List<(double, double)>();
        foreach (var beam in secondaryBeams)
        {
            var dx = beam.Location.X * MmPerFoot - frame.StartMm.Xmm;
            var dy = beam.Location.Y * MmPerFoot - frame.StartMm.Ymm;
            var station = dx * frame.Along.X + dy * frame.Along.Y;
            if (station <= 0 || station >= frame.LengthMm) continue;
            stations.Add((station, beam.HalfWidthFeet * MmPerFoot));
        }
        return stations;
    }
}
