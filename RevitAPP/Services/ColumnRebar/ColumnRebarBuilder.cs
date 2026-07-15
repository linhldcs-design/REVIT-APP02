using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models;
using RevitAPP.Core.Services;
using RevitAPP.Helpers;
using RevitAPP.Models;

namespace RevitAPP.Services.ColumnRebar;

public sealed record RebarBuildResult(int MainBarCount, int StirrupSetCount, int StarterBarCount, IReadOnlyList<string> Warnings);

/// <summary>
///     Một thanh thép chủ đã tính xong hình học, chờ tạo. Anchor = vị trí (X,Y mm) trong mặt phẳng
///     tiết diện dùng để gom các thanh thẳng hàng. Primary = đường uốn (null nếu thanh thẳng);
///     Straight = đường thẳng fallback.
/// </summary>
internal sealed record MainBarPlacement(RebarBarType BarType, Point2D Anchor, List<Curve>? Primary, List<Curve> Straight);

/// <summary>
///     Sinh Rebar thật cho hệ cột: thép đai 3 vùng (móc 135°) + thép chủ chạy dọc có nối chồng tại sàn.
///     Mỗi rebar được tạo trong một SubTransaction riêng — lỗi một thanh không phá cả lệnh.
///     Gọi bên trong một Transaction đang mở (do command quản lý).
/// </summary>
public sealed class ColumnRebarBuilder
{
    private const double Hook135Radians = 135d * Math.PI / 180d;
    private const double Hook180Radians = 180d * Math.PI / 180d;
    private const int MaxErrorsReported = 6;

    private readonly List<string> _errors = new();
    private readonly List<(ElementId Id, string Partition)> _created = new();
    private bool _addPartition;
    private string _currentPartition = "";

    public RebarBuildResult Build(Document document, IReadOnlyList<ColumnStackItem> stack,
        IReadOnlyList<StoreyRebarPlan> plans, RebarLapOptions lap, FoundationStarterOptions? starter = null,
        StirrupSpreadOptions? spread = null, ColumnEndOptions? ends = null, bool addPartition = false,
        SectionTransitionOptions? transition = null)
    {
        spread ??= new StirrupSpreadOptions();
        ends ??= new ColumnEndOptions();
        transition ??= new SectionTransitionOptions();
        _addPartition = addPartition;
        var hook135 = FindHook135(document);
        if (hook135 == null)
            _errors.Add("Không tìm thấy loại móc 135° (RebarHookType) — đai tạo không móc.");

        var mainBarCount = 0;
        var stirrupSetCount = 0;
        var starterBarCount = 0;

        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var item = stack[i];
            if (document.GetElement(item.ColumnId) is not FamilyInstance host)
            {
                _errors.Add($"Tầng {plan.Storey.LevelName}: không tìm thấy cột host — bỏ qua.");
                continue;
            }

            var mainType = document.GetElement(ElementIdHelper.Create(plan.MainBar.BarTypeId)) as RebarBarType;
            var stirrupType = document.GetElement(ElementIdHelper.Create(plan.Stirrup.BarTypeId)) as RebarBarType;
            if (mainType == null || stirrupType == null)
            {
                _errors.Add($"Tầng {plan.Storey.LevelName}: không tìm thấy loại thanh thép — bỏ qua.");
                continue;
            }

            // Partition = Mark của cột (nhóm thép theo cột trong schedule/browser); fallback tên tầng.
            var columnMark = host.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            _currentPartition = string.IsNullOrWhiteSpace(columnMark) ? plan.Storey.LevelName : columnMark!;
            stirrupSetCount += BuildStirrups(document, host, item, plan, stirrupType, hook135, lap.CoverMm, spread);

            // Phát hiện bóp cột: so sánh tiết diện tầng này với tầng trên; prevPlan để neo xuống qua đáy dầm
            var nextPlan = i < plans.Count - 1 ? plans[i + 1] : null;
            var nextItem = i < plans.Count - 1 ? stack[i + 1] : null;
            var prevPlan = i > 0 ? plans[i - 1] : null;
            var prevItem = i > 0 ? stack[i - 1] : null;
            // Tầng 1 có thép chờ móng (so le) → chân thép chủ tầng 1 cũng so le để khớp mối nối.
            var hasStarter = i == 0 && starter is { Enabled: true };
            var staggerBottom = hasStarter && lap.StaggerLap;
            // Có thép chờ → thép chủ tầng 1 BẮT ĐẦU tại mối nối cách sàn LapDistanceFromBottomMm (50mm),
            // gối lên đoạn lap thẳng của thép chờ. null = chân tại đáy tầng như cũ.
            double? bottomStartMm = hasStarter ? plan.Storey.BaseElevationMm + lap.LapDistanceFromBottomMm : null;
            mainBarCount += BuildMainBars(document, host, item, plan, nextPlan, nextItem, prevPlan, prevItem,
                mainType, lap, ends, transition, isBottomStorey: i == 0, isTopStorey: i == plans.Count - 1,
                staggerBottom: staggerBottom, bottomStartMm: bottomStartMm);

            if (starter is { Enabled: true } && i == 0)
            {
                var distType = plan.DistributionBar != null
                    ? document.GetElement(ElementIdHelper.Create(plan.DistributionBar.BarTypeId)) as RebarBarType
                    : null;
                starterBarCount += BuildStarterBars(document, host, item, plan, mainType, distType, lap, starter, ends);
            }
        }

        // Gán Partition theo tầng để nhóm thép trong schedule/browser
        if (_addPartition)
            foreach (var (id, partition) in _created)
                if (document.GetElement(id) is Rebar rebar)
                    SetPartition(rebar, partition);

        var reported = _errors.Take(MaxErrorsReported).ToList();
        if (_errors.Count > MaxErrorsReported) reported.Add($"... và {_errors.Count - MaxErrorsReported} lỗi khác.");
        return new RebarBuildResult(mainBarCount, stirrupSetCount, starterBarCount, reported);
    }

    private static void SetPartition(Rebar rebar, string partition)
    {
        var p = rebar.LookupParameter("Partition");
        if (p is { IsReadOnly: false, StorageType: StorageType.String })
            p.Set(partition);
    }

    /// <summary>
    ///     Tạo một rebar trong SubTransaction riêng + regenerate để cô lập lỗi hình học.
    ///     Trả về true nếu thành công; lỗi được gom vào _errors.
    /// </summary>
    private bool TryCreate(Document document, string what, Func<Rebar?> factory)
    {
        using var sub = new SubTransaction(document);
        sub.Start();
        try
        {
            var rebar = factory();
            if (rebar == null) { sub.RollBack(); return false; }
            document.Regenerate();
            sub.Commit();
            Track(rebar);
            return true;
        }
        catch (Exception ex)
        {
            sub.RollBack();
            if (_errors.Count < MaxErrorsReported * 2) _errors.Add($"{what}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Như TryCreate nhưng không ghi lỗi (dùng cho lần thử đầu có fallback).</summary>
    private bool TryCreateQuiet(Document document, Func<Rebar?> factory)
    {
        using var sub = new SubTransaction(document);
        sub.Start();
        try
        {
            var rebar = factory();
            if (rebar == null) { sub.RollBack(); return false; }
            document.Regenerate();
            sub.Commit();
            Track(rebar);
            return true;
        }
        catch
        {
            sub.RollBack();
            return false;
        }
    }

    private void Track(Rebar rebar)
    {
        if (_addPartition && _currentPartition.Length > 0)
            _created.Add((rebar.Id, _currentPartition));
    }

    private int BuildStirrups(Document document, FamilyInstance host, ColumnStackItem item,
        StoreyRebarPlan plan, RebarBarType stirrupType, RebarHookType? hook, double coverMm, StirrupSpreadOptions spread)
    {
        var section = plan.Storey.Section;
        var config = plan.Config;
        var loop = TcvnRebarCalculator.BuildStirrupLoop(section, coverMm, config.StirrupDiameterMm);

        var zoneConfig = spread.SpreadThroughBeam ? config with { BeamDepthMm = 0 } : config;
        var zones = TcvnRebarCalculator.ComputeZones(plan.Storey.ClearHeightMm, section, zoneConfig,
            spread.MinConfineZoneMm, spread.ConfineClearanceDivisor);
        var baseFeet = ToFeet(plan.Storey.BaseElevationMm);
        var firstOffsetFeet = ToFeet(spread.DistanceToFirstMm);

        var isCrosstie = config.StirrupSectionType == SectionStirrupType.Crosstie;
        var crosstieHook = FindHook180(document) ?? hook; // móc chéo dùng 180°, fallback 135°
        var dir = spread.CrosstieDirection;
        var crosstieXs = isCrosstie && (dir is CrosstieDirection.X or CrosstieDirection.Both)
            ? TcvnRebarCalculator.CrosstieXPositions(section, config, coverMm)
            : Array.Empty<double>();
        var crosstieYs = isCrosstie && (dir is CrosstieDirection.Y or CrosstieDirection.Both)
            ? TcvnRebarCalculator.CrosstieYPositions(section, config, coverMm)
            : Array.Empty<double>();
        var loopHalfWmm = Math.Abs(loop[0].Xmm);
        var loopHalfHmm = Math.Abs(loop[0].Ymm);

        var created = 0;
        var isBottomZone = true;
        foreach (var zone in new[] { zones.Bottom, zones.Middle, zones.Top })
        {
            if (zone.Count <= 0 || zone.LengthMm <= 0) { isBottomZone = false; continue; }
            var zBottomFeet = baseFeet + ToFeet(zone.StartElevationMm) + (isBottomZone ? firstOffsetFeet : 0);
            isBottomZone = false;

            var points = OrderedLoopPoints(item, loop, zBottomFeet);
            var curves = ClosedLoopCurves(points);

            // Thử có móc; nếu lỗi (móc không tạo được) thì thử lại không móc
            if (TryCreate(document, $"Đai {plan.Storey.LevelName}",
                    () => CreateStirrup(document, host, curves, stirrupType, hook, zone))
                || TryCreate(document, $"Đai {plan.Storey.LevelName} (không móc)",
                    () => CreateStirrup(document, host, curves, stirrupType, null, zone)))
                created++;

            // Móc chéo phương X (đứng): nối thanh giữa mặt trên ↔ dưới. Fallback móc 180° → 135° → không móc.
            foreach (var x in crosstieXs)
            {
                var line = new List<Curve>
                {
                    Line.CreateBound(ToWorld(item, x, loopHalfHmm, zBottomFeet), ToWorld(item, x, -loopHalfHmm, zBottomFeet))
                };
                if (TryCreateQuiet(document, () => CreateTie(document, host, line, RebarStyle.StirrupTie, stirrupType, crosstieHook, zone))
                    || TryCreateQuiet(document, () => CreateTie(document, host, line, RebarStyle.StirrupTie, stirrupType, hook, zone))
                    || TryCreate(document, $"Móc chéo X {plan.Storey.LevelName}",
                        () => CreateTie(document, host, line, RebarStyle.StirrupTie, stirrupType, null, zone)))
                    created++;
            }

            // Móc chéo phương Y (ngang): nối thanh giữa mặt trái ↔ phải. Fallback móc 180° → 135° → không móc.
            foreach (var y in crosstieYs)
            {
                var line = new List<Curve>
                {
                    Line.CreateBound(ToWorld(item, loopHalfWmm, y, zBottomFeet), ToWorld(item, -loopHalfWmm, y, zBottomFeet))
                };
                if (TryCreateQuiet(document, () => CreateTie(document, host, line, RebarStyle.StirrupTie, stirrupType, crosstieHook, zone))
                    || TryCreateQuiet(document, () => CreateTie(document, host, line, RebarStyle.StirrupTie, stirrupType, hook, zone))
                    || TryCreate(document, $"Móc chéo Y {plan.Storey.LevelName}",
                        () => CreateTie(document, host, line, RebarStyle.StirrupTie, stirrupType, null, zone)))
                    created++;
            }
        }

        // Đai gia cường trong vùng dầm/nút (khi đai chính dừng dưới dầm)
        if (spread.ReinforceJoint && !spread.SpreadThroughBeam && config.BeamDepthMm > 0)
        {
            var count = Math.Max(2, spread.JointStirrupCount);
            var beamMm = config.BeamDepthMm;
            var jointZone = new StirrupZone(0, beamMm, beamMm / (count - 1), count);
            var zJointFeet = baseFeet + ToFeet(plan.Storey.ClearHeightMm - beamMm);
            var jointCurves = ClosedLoopCurves(OrderedLoopPoints(item, loop, zJointFeet));

            if (TryCreate(document, $"Đai nút {plan.Storey.LevelName}",
                    () => CreateStirrup(document, host, jointCurves, stirrupType, hook, jointZone)))
                created++;
        }

        return created;
    }

    private int BuildMainBars(Document document, FamilyInstance host, ColumnStackItem item,
        StoreyRebarPlan plan, StoreyRebarPlan? nextPlan, ColumnStackItem? nextItem,
        StoreyRebarPlan? prevPlan, ColumnStackItem? prevItem,
        RebarBarType mainType, RebarLapOptions lap, ColumnEndOptions ends,
        SectionTransitionOptions transition, bool isBottomStorey, bool isTopStorey, bool staggerBottom,
        double? bottomStartMm = null)
    {
        var section = plan.Storey.Section;
        var config = plan.Config;
        var bars = TcvnRebarCalculator.BuildClassifiedMainBars(section, config, lap.CoverMm);

        // Tầng DƯỚI to hơn (nút bóp ngay dưới) → thép cột TRÊN neo XUỐNG nút một đoạn.
        // CHỈ áp ở Hình 1 (cột trên thép riêng). Hình 2 thì cây dưới uốn vát liên tục lên nên không cần.
        var prevBop = !isBottomStorey && prevPlan != null &&
            TcvnRebarCalculator.SectionStepOffsetMm(prevPlan.Storey.Section, prevPlan.Config, section, config, lap.CoverMm) >= 1d;
        var jointDownMm = prevBop && transition.LargeStepMode == LargeStepMode.AnchorAtSlab
            ? transition.JointAnchorDownMm : 0d;

        var distType = plan.DistributionBar != null
            ? document.GetElement(ElementIdHelper.Create(plan.DistributionBar.BarTypeId)) as RebarBarType
            : null;

        var staggerShiftMm = TcvnRebarCalculator.LapLengthMm(config.MainBarDiameterMm, lap.LapFactor);
        var lapZoneOffsetMm = lap.LapPosition == LapPosition.Middle
            ? plan.Storey.ClearHeightMm / 2d
            : lap.LapDistanceFromBottomMm;

        // Nút BÓP cột (cột TRÊN nhỏ hơn): xác định thanh cột DƯỚI nào có cặp thanh cột trên để nối.
        // Thanh KHÔNG có cặp (ở mặt bị bóp) → Hình 1: bẻ móc ngang tại sàn; Hình 2: uốn vát liên tục lên.
        const double partnerTolMm = 40d;
        var nextBop = false;
        IReadOnlyList<PlacedBar>? upperBars = null;
        var offLocalXMm = 0d;
        var offLocalYMm = 0d;
        if (!isTopStorey && nextPlan != null && nextItem != null)
        {
            var stepMm = TcvnRebarCalculator.SectionStepOffsetMm(
                section, config, nextPlan.Storey.Section, nextPlan.Config, lap.CoverMm);
            if (stepMm >= 1d)
            {
                nextBop = true;
                upperBars = TcvnRebarCalculator.BuildClassifiedMainBars(nextPlan.Storey.Section, nextPlan.Config, lap.CoverMm);
                var dxMm = ToMm(nextItem.CenterXFeet - item.CenterXFeet);
                var dyMm = ToMm(nextItem.CenterYFeet - item.CenterYFeet);
                var cos = Math.Cos(item.RotationRad);
                var sin = Math.Sin(item.RotationRad);
                offLocalXMm = dxMm * cos + dyMm * sin;
                offLocalYMm = -dxMm * sin + dyMm * cos;
            }
        }

        var placements = new List<MainBarPlacement>(bars.Count);
        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var barType = bar.IsCorner ? mainType : distType ?? mainType;
            var lapMm = TcvnRebarCalculator.LapLengthMm(bar.DiameterMm, lap.LapFactor);

            // So le 50/50 theo VỊ TRÍ trên chu vi (bàn cờ) — 2 thanh kề nhau trên cùng cạnh nối ở 2 cao độ.
            var staggerParity = TcvnRebarCalculator.StaggerParity(bar.Position, section, config, lap.CoverMm);
            var (bottomMm, topMm) = TcvnRebarCalculator.MainBarSpan(
                plan.Storey.BaseElevationMm, plan.Storey.TopElevationMm, lapMm, staggerShiftMm, lapZoneOffsetMm,
                isBottomStorey, isTopStorey, lap.StaggerLap, staggerParity, staggerBottom);

            // Tầng trên cùng: đỉnh thép LÙI XUỐNG 1 lớp bê tông bảo vệ (cover + d_đai) so với mặt trên cột,
            // không chạm mép bê tông. Tầng giữa nối lên tầng trên nên giữ nguyên.
            if (isTopStorey)
                topMm -= lap.CoverMm + config.StirrupDiameterMm;

            var topFeet = ToFeet(topMm);
            // Chân thép chủ: bottomStartMm (vị trí uốn thép chờ) nếu có thép chờ — giữ phần so le của bottomMm;
            // nếu không có thép chờ → bottomMm như cũ. Nút bóp dưới đè lên cả 2 (neo xuống nút).
            var staggeredStartMm = bottomStartMm.HasValue ? bottomStartMm.Value + (bottomMm - plan.Storey.BaseElevationMm) : bottomMm;
            var bottomFeet = ToFeet(prevBop ? plan.Storey.BaseElevationMm - jointDownMm : staggeredStartMm);
            var slabFeet = ToFeet(plan.Storey.TopElevationMm);

            List<Curve>? primary = null;
            var topPos = bar.Position;

            // Tại nút bóp: tìm thanh cột trên gần nhất; xác định có cặp để nối không
            var transitionOffsetMm = 0d;
            var noUpperPartner = false;
            if (nextBop && upperBars!.Count > 0)
            {
                var inUpperFrame = new Point2D(bar.Position.Xmm - offLocalXMm, bar.Position.Ymm - offLocalYMm);
                var nearest = upperBars.OrderBy(b =>
                    Math.Pow(b.Position.Xmm - inUpperFrame.Xmm, 2) + Math.Pow(b.Position.Ymm - inUpperFrame.Ymm, 2)).First();
                topPos = new Point2D(nearest.Position.Xmm + offLocalXMm, nearest.Position.Ymm + offLocalYMm);
                transitionOffsetMm = Math.Sqrt(Math.Pow(topPos.Xmm - bar.Position.Xmm, 2) + Math.Pow(topPos.Ymm - bar.Position.Ymm, 2));
                noUpperPartner = transitionOffsetMm > partnerTolMm;
            }

            // Hình 2 (uốn vát liên tục) chỉ áp khi cần dịch & user chọn CrankContinuous
            var doCrank = nextBop && noUpperPartner && transition.LargeStepMode == LargeStepMode.CrankContinuous;
            // Hình 1 (neo móc tại sàn) khi thanh mặt bóp không có cặp cột trên
            var doAnchorHook = nextBop && noUpperPartner && transition.LargeStepMode == LargeStepMode.AnchorAtSlab;

            var stepBottomFeet = ToFeet(plan.Storey.BaseElevationMm);
            var stepUpperExtMm = staggerShiftMm + lapMm;
            var stepTopFeet = ToFeet(plan.Storey.TopElevationMm + stepUpperExtMm);

            if (doAnchorHook)
            {
                // HÌNH 1 — Thanh cột dưới mặt bóp: chân đứng chạy lên + MÓC NGANG 90° vào lõi (neo vào nút).
                // Đầu móc LÙI XUỐNG 1 lớp bê tông bảo vệ (cover + d_đai) so với mặt sàn, không chạm mép.
                // Không nối lên cột trên (cột trên có thép riêng).
                var anchorTopFeet = ToFeet(plan.Storey.TopElevationMm - lap.CoverMm - config.StirrupDiameterMm);
                var hookMm = ends.TopHookLengthMm > 0 ? ends.TopHookLengthMm : 150d;
                var legEnd = TcvnRebarCalculator.TopHookLegEnd(bar.Position, hookMm);
                primary = new List<Curve>
                {
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, bottomFeet),
                                     ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, anchorTopFeet)),
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, anchorTopFeet),
                                     ToWorld(item, legEnd.Xmm, legEnd.Ymm, anchorTopFeet))
                };
            }
            else if (isTopStorey && ends.TopHookBending && ends.TopHookLengthMm > 0)
            {
                // Móc đỉnh cột trên cùng — topFeet đã lùi xuống 1 lớp bê tông bảo vệ (xử lý ở trên).
                var legEnd = TcvnRebarCalculator.TopHookLegEnd(bar.Position, ends.TopHookLengthMm);
                primary = new List<Curve>
                {
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, bottomFeet),
                                     ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, topFeet)),
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, topFeet),
                                     ToWorld(item, legEnd.Xmm, legEnd.Ymm, topFeet))
                };
            }
            else if (doCrank)
            {
                // HÌNH 2 — Uốn vát liên tục: vát dưới sàn vào vị trí thanh cột trên rồi chạy thẳng lên nối.
                var hdMm = Math.Max(transitionOffsetMm * transition.SlopeRatioHdOverE, 300);
                var slopeStartFeet = ToFeet(plan.Storey.TopElevationMm - hdMm);
                primary = new List<Curve>
                {
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, stepBottomFeet),
                                     ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, slopeStartFeet)),
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, slopeStartFeet),
                                     ToWorld(item, topPos.Xmm, topPos.Ymm, slabFeet)),
                    Line.CreateBound(ToWorld(item, topPos.Xmm, topPos.Ymm, slabFeet),
                                     ToWorld(item, topPos.Xmm, topPos.Ymm, stepTopFeet))
                };
            }
            else if (!isTopStorey && ends.CrankAtLap && lapMm > 0 && !doAnchorHook)
            {
                // Uốn lệch đoạn nối chồng ~1 đường kính
                var crankMm = bar.DiameterMm;
                var slopeMm = Math.Min(Math.Max(crankMm * 6, 50), lapMm / 2);
                var crankStartFeet = ToFeet(topMm - lapMm);
                var slopeTopFeet = ToFeet(topMm - lapMm + slopeMm);
                var crankOffset = TcvnRebarCalculator.TopHookLegEnd(bar.Position, crankMm);
                primary = new List<Curve>
                {
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, bottomFeet),
                                     ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, crankStartFeet)),
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, crankStartFeet),
                                     ToWorld(item, crankOffset.Xmm, crankOffset.Ymm, slopeTopFeet)),
                    Line.CreateBound(ToWorld(item, crankOffset.Xmm, crankOffset.Ymm, slopeTopFeet),
                                     ToWorld(item, crankOffset.Xmm, crankOffset.Ymm, topFeet))
                };
            }

            // Thanh thẳng fallback:
            //  - neo móc/uốn vát (mặt bóp): giữ thẳng base → sàn trừ lớp bảo vệ (không chạm mép, không chĩa ngoài cột trên)
            //  - bình thường: span theo MainBarSpan tại vị trí thanh
            List<Curve> straight;
            if (doAnchorHook || doCrank)
            {
                var fallbackTopFeet = ToFeet(plan.Storey.TopElevationMm - lap.CoverMm - config.StirrupDiameterMm);
                straight = new List<Curve>
                {
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, bottomFeet),
                                     ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, fallbackTopFeet))
                };
            }
            else
            {
                straight = new List<Curve>
                {
                    Line.CreateBound(ToWorld(item, topPos.Xmm, topPos.Ymm, bottomFeet),
                                     ToWorld(item, topPos.Xmm, topPos.Ymm, topFeet))
                };
            }

            // Vị trí mặt phẳng (X,Y mm) của thanh dùng để gom các thanh thẳng hàng cách đều.
            var anchor = primary != null ? bar.Position : topPos;
            placements.Add(new MainBarPlacement(barType, anchor, primary, straight));
        }

        return CreateGroupedMainBars(document, host, item, plan.Storey.LevelName, placements);
    }

    /// <summary>
    ///     Gom các thanh thép chủ giống nhau (cùng loại + cùng kiểu uốn + thẳng hàng cách đều)
    ///     thành MỘT Rebar đại diện nhiều thanh (SetLayoutAsFixedNumber) → schedule gọn (Quantity = N).
    ///     Thanh không gom được (đơn lẻ / không đều) vẫn tạo riêng từng cây.
    /// </summary>
    private int CreateGroupedMainBars(Document document, FamilyInstance host, ColumnStackItem item,
        string levelName, IReadOnlyList<MainBarPlacement> placements)
    {
        const double collinearTolMm = 1d; // sai số khi xét thẳng hàng / cách đều
        var created = 0;

        // Chữ ký gom: loại thanh + có uốn hay không + hình dạng chuẩn hoá về gốc (mm).
        var groups = placements
            .Select((p, idx) => (p, idx))
            .GroupBy(t => ShapeSignature(t.p));

        foreach (var group in groups)
        {
            var members = group.OrderBy(t => t.idx).Select(t => t.p).ToList();

            // Một group (cùng loại + cùng hình dạng) có thể chứa nhiều HÀNG khác nhau
            // (vd 4 góc = 2 hàng song song). Phân rã thành các hàng thẳng hàng cách đều,
            // rồi mỗi hàng ≥4 thanh tách thành 2 nhóm XEN KẼ (chẵn/lẻ) để mối nối lap so le 50%
            // — đúng cách bản vẽ mẫu gom thép chủ (2 element NumberWithSpacing, step = 2× bước thanh).
            foreach (var run in SplitCollinearRuns(members, collinearTolMm).SelectMany(InterleaveForStagger))
            {
                if (run.Count >= 2 && TryEvenlySpaced(run, collinearTolMm, out var spacingMm, out var dirX, out var dirY))
                {
                    var first = run[0];
                    var curves = first.Primary ?? first.Straight;
                    var count = run.Count;
                    var spreadDir = PlaneVectorToWorld(item, dirX, dirY);
                    if (TryCreate(document, $"Thép chủ {levelName} (nhóm {count})",
                            () => CreateLaidOut(document, host, curves, first.BarType, count, ToFeet(spacingMm), spreadDir)))
                    {
                        created += count;
                        continue;
                    }
                    // Layout thất bại → fallback tạo riêng từng cây.
                }

                foreach (var m in run)
                    if (CreateSingleMainBar(document, host, levelName, m))
                        created++;
            }
        }

        return created;
    }

    /// <summary>
    ///     Phân rã danh sách thanh (cùng hình dạng) thành các HÀNG thẳng hàng cách đều + CÙNG cao độ đáy.
    ///     Điều kiện cùng Z bắt buộc vì layout NumberWithSpacing nhân bản cùng 1 cao độ — thanh nối so le
    ///     (stagger, khác Z) phải vào hàng riêng để đặt đúng cao độ. Cột 4 góc → 2 hàng (cạnh dưới/trên).
    /// </summary>
    private static IEnumerable<List<MainBarPlacement>> SplitCollinearRuns(
        IReadOnlyList<MainBarPlacement> members, double tolMm)
    {
        const double zTolMm = 1d;
        var remaining = members.ToList();
        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            var seedZ = BottomZmm(seed.Primary ?? seed.Straight);
            // Cùng cao độ đáy + cùng Y (hàng ngang) hoặc cùng X (hàng dọc).
            var atZ = remaining.Where(m => Math.Abs(BottomZmm(m.Primary ?? m.Straight) - seedZ) <= zTolMm).ToList();
            var sameY = atZ.Where(m => Math.Abs(m.Anchor.Ymm - seed.Anchor.Ymm) <= tolMm).ToList();
            var sameX = atZ.Where(m => Math.Abs(m.Anchor.Xmm - seed.Anchor.Xmm) <= tolMm).ToList();
            var run = sameY.Count >= sameX.Count ? sameY : sameX;
            if (run.Count == 0) run = new List<MainBarPlacement> { seed };

            foreach (var m in run) remaining.Remove(m);
            yield return run.OrderBy(m => m.Anchor.Xmm + m.Anchor.Ymm).ToList();
        }
    }

    /// <summary>
    ///     Tách một hàng thanh đã sắp thứ tự thành 2 nhóm XEN KẼ (thanh thứ chẵn / thứ lẻ).
    ///     Mỗi nhóm trở thành 1 element NumberWithSpacing với bước = 2× bước thanh gốc → mối nối
    ///     lap so le 50% giữa 2 nhóm (đúng cách bản vẽ mẫu). Hàng < 4 thanh giữ nguyên 1 nhóm.
    /// </summary>
    private static IEnumerable<List<MainBarPlacement>> InterleaveForStagger(List<MainBarPlacement> run)
    {
        if (run.Count < 4)
        {
            yield return run;
            yield break;
        }

        var even = new List<MainBarPlacement>();
        var odd = new List<MainBarPlacement>();
        for (var i = 0; i < run.Count; i++)
            (i % 2 == 0 ? even : odd).Add(run[i]);

        yield return even;
        yield return odd;
    }

    private bool CreateSingleMainBar(Document document, FamilyInstance host, string levelName, MainBarPlacement m)
    {
        if (m.Primary != null)
        {
            if (TryCreateQuiet(document, () => CreateTie(document, host, m.Primary, RebarStyle.Standard, m.BarType, null, null)))
                return true;
            return TryCreate(document, $"Thép chủ {levelName} (thẳng)",
                () => CreateTie(document, host, m.Straight, RebarStyle.Standard, m.BarType, null, null));
        }

        return TryCreate(document, $"Thép chủ {levelName}",
            () => CreateTie(document, host, m.Straight, RebarStyle.Standard, m.BarType, null, null));
    }

    /// <summary>
    ///     Tạo 1 Rebar đại diện N thanh bằng SetLayoutAsNumberWithSpacing dọc theo <paramref name="spreadDir"/>.
    ///     Dùng NumberWithSpacing (số thanh + khoảng cách) đúng cách bản vẽ chuyên nghiệp gom thép chủ.
    /// </summary>
    private static Rebar? CreateLaidOut(Document document, Element host, IList<Curve> curves,
        RebarBarType type, int count, double spacingFeet, XYZ spreadDir)
    {
        // Normal của Rebar = pháp tuyến mặt phẳng chứa thanh. Để layout rải ĐÚNG theo spreadDir,
        // normal phải song song spreadDir (hướng nhân bản). spreadDir đã nằm ngang ⊥ thanh đứng.
        var norm = spreadDir.Normalize();
        var rebar = RebarCompat.CreateFromCurves(document, RebarStyle.Standard, type, null, null, host,
            norm, curves);
        if (rebar == null) return null;

        rebar.GetShapeDrivenAccessor()
            .SetLayoutAsNumberWithSpacing(count, spacingFeet, true, true, true);
        return rebar;
    }

    /// <summary>
    ///     Chữ ký HÌNH DẠNG của thanh, độc lập vị trí: dời mọi điểm về ĐIỂM ĐẦU (world) của chính thanh.
    ///     Anchor (toạ độ phẳng tiết diện) KHÔNG dùng để dời vì curves ở hệ world (đã xoay + offset tâm cột)
    ///     — trừ 2 hệ khác nhau sẽ ra giá trị rác khác nhau từng thanh. Hai thanh cùng hình dạng (cùng các
    ///     vector đoạn) → cùng chữ ký dù khác vị trí/cao độ.
    /// </summary>
    private static string ShapeSignature(MainBarPlacement p)
    {
        var curves = p.Primary ?? p.Straight;
        var origin = curves[0].GetEndPoint(0);
        var ox = ToMm(origin.X);
        var oy = ToMm(origin.Y);
        var oz = ToMm(origin.Z);
        var bent = p.Primary != null ? "B" : "S";
        var sb = new System.Text.StringBuilder();
        sb.Append(p.BarType.Id.ToValue()).Append('|').Append(bent);
        foreach (var c in curves)
        {
            var a = c.GetEndPoint(0);
            var b = c.GetEndPoint(1);
            sb.Append('|')
              .Append(Round(ToMm(a.X) - ox)).Append(',').Append(Round(ToMm(a.Y) - oy)).Append(',').Append(Round(ToMm(a.Z) - oz))
              .Append(';')
              .Append(Round(ToMm(b.X) - ox)).Append(',').Append(Round(ToMm(b.Y) - oy)).Append(',').Append(Round(ToMm(b.Z) - oz));
        }

        return sb.ToString();
    }

    /// <summary>Cao độ đáy thanh (mm) — Z nhỏ nhất trong tất cả điểm của curves.</summary>
    private static double BottomZmm(IReadOnlyList<Curve> curves)
    {
        var min = double.MaxValue;
        foreach (var c in curves)
        {
            min = Math.Min(min, ToMm(c.GetEndPoint(0).Z));
            min = Math.Min(min, ToMm(c.GetEndPoint(1).Z));
        }

        return min;
    }

    private static long Round(double mm) => (long)Math.Round(mm);

    /// <summary>
    ///     Kiểm tra các thanh có anchor thẳng hàng + cách đều theo 1 hướng trong mặt phẳng tiết diện.
    ///     Trả về khoảng cách (mm) và vector đơn vị (dirX,dirY) từ thanh đầu → thanh cuối.
    /// </summary>
    private static bool TryEvenlySpaced(IReadOnlyList<MainBarPlacement> members, double tolMm,
        out double spacingMm, out double dirX, out double dirY)
    {
        spacingMm = 0; dirX = 0; dirY = 0;
        var first = members[0].Anchor;
        var last = members[^1].Anchor;
        var totalDx = last.Xmm - first.Xmm;
        var totalDy = last.Ymm - first.Ymm;
        var total = Math.Sqrt(totalDx * totalDx + totalDy * totalDy);
        if (total < tolMm) return false; // trùng vị trí — không phải hàng

        dirX = totalDx / total;
        dirY = totalDy / total;
        spacingMm = total / (members.Count - 1);

        // Mọi thanh phải nằm trên đường thẳng + đúng khoảng cách đều.
        for (var k = 0; k < members.Count; k++)
        {
            var expectedX = first.Xmm + dirX * spacingMm * k;
            var expectedY = first.Ymm + dirY * spacingMm * k;
            var dx = members[k].Anchor.Xmm - expectedX;
            var dy = members[k].Anchor.Ymm - expectedY;
            if (Math.Sqrt(dx * dx + dy * dy) > tolMm) return false;
        }

        return true;
    }

    /// <summary>Vector trong mặt phẳng tiết diện (mm, chưa scale) → hướng world (đã xoay theo cột).</summary>
    private static XYZ PlaneVectorToWorld(ColumnStackItem item, double dx, double dy)
    {
        var cos = Math.Cos(item.RotationRad);
        var sin = Math.Sin(item.RotationRad);
        return new XYZ(dx * cos - dy * sin, dx * sin + dy * cos, 0).Normalize();
    }


    private int BuildStarterBars(Document document, FamilyInstance host, ColumnStackItem item,
        StoreyRebarPlan plan, RebarBarType mainType, RebarBarType? distType, RebarLapOptions lap,
        FoundationStarterOptions starter, ColumnEndOptions ends)
    {
        var section = plan.Storey.Section;
        var config = plan.Config;
        var bars = TcvnRebarCalculator.BuildClassifiedMainBars(section, config, lap.CoverMm);
        var baseFeet = ToFeet(plan.Storey.BaseElevationMm);
        var (baseDirX, baseDirY) = starter.DirectionVector;

        // Dịch so le ĐỒNG ĐỀU (giống thép chủ) — mối nối thép chờ↔thép chủ tầng 1 gom đúng 2 cao độ.
        var staggerShiftMm = TcvnRebarCalculator.LapLengthMm(config.MainBarDiameterMm, lap.LapFactor);

        var created = 0;
        foreach (var bar in bars)
        {
            var barType = bar.IsCorner ? mainType : distType ?? mainType;
            var lapMm = TcvnRebarCalculator.LapLengthMm(bar.DiameterMm, lap.LapFactor);

            // Hướng bẻ chân: cố định 1 bên (mặc định), hoặc đối xứng 2 BÊN — thanh bẻ ra phía cùng dấu
            // với vị trí của nó trên trục bẻ (nửa âm bẻ về âm, nửa dương bẻ về dương → chĩa ra ngoài 2 phía).
            var (dirX, dirY) = (baseDirX, baseDirY);
            if (starter.SplitBothSides)
            {
                if (baseDirX != 0) dirX = bar.Position.Xmm >= 0 ? 1 : -1;
                if (baseDirY != 0) dirY = bar.Position.Ymm >= 0 ? 1 : -1;
            }

            // So le 50/50 theo VỊ TRÍ trên chu vi (bàn cờ): thanh parity 1 vươn cao thêm 1 đoạn so le
            // → đầu trên thép chờ (mối nối với chân thép chủ) nằm xen kẽ 2 cao độ, không cùng mặt cắt.
            var staggerParity = TcvnRebarCalculator.StaggerParity(bar.Position, section, config, lap.CoverMm);
            var staggerMm = lap.StaggerLap && staggerParity == 1 ? staggerShiftMm : 0d;

            // Chân thép chờ CẮM XUỐNG móng đoạn Hm (dưới đáy cột), foot Lb ngang ở đáy.
            // Đoạn lap (nối chồng với chân thép chủ tầng 1) bắt đầu cách SÀN một đoạn lapFromBottom (50mm)
            // — khớp điểm bắt đầu thép chủ tầng 1. So le 50/50: parity 1 nâng đoạn lap lên.
            var bottomFeet = baseFeet - ToFeet(starter.HmMm);
            var baseMm = plan.Storey.BaseElevationMm;
            var lapBottomMm = baseMm + lap.LapDistanceFromBottomMm + staggerMm; // đáy đoạn lap (= chân thép chủ tầng 1)
            var lapTopMm = lapBottomMm + lapMm;                                 // đỉnh thanh (hết đoạn lap)
            var topFeet = ToFeet(lapTopMm);

            // foot ngang ở đáy móng
            var footStart = ToWorld(item, bar.Position.Xmm + dirX * starter.LbMm, bar.Position.Ymm + dirY * starter.LbMm, bottomFeet);
            var bottom = ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, bottomFeet);

            var crankMm0 = bar.DiameterMm;
            var slopeMm0 = Math.Min(Math.Max(crankMm0 * 6, 50), lapMm / 2);
            List<Curve> curves;
            // Crank cần đủ chỗ giữa móng (−Hm) và đáy đoạn lap để chứa đoạn vát (không chui xuống móng).
            if (ends.CrankAtLap && lapMm > 0 && lapBottomMm - slopeMm0 >= baseMm - starter.HmMm)
            {
                // Crank ở ĐẦU TRÊN (đúng thuật toán nối thép chủ giữa tầng): đoạn dưới chạy thẳng theo
                // vị trí thanh, vát vào lõi ~1 đường kính ngay dưới đoạn lap, rồi đoạn lap thẳng đứng đã
                // lệch vào trong — thép chủ tầng 1 nối chồng kề bên không bị đè.
                var crankMm = crankMm0;
                var slopeMm = slopeMm0;
                var crankStartFeet = ToFeet(lapBottomMm - slopeMm);
                var slopeTopFeet = ToFeet(lapBottomMm);
                // Crank vát theo ĐÚNG phương chân bẻ móng (dirX,dirY) để mọi đoạn đồng phẳng (Revit mới
                // chấp nhận), nhưng CHIỀU luôn VÀO TÂM cột (gốc tiết diện = 0,0) — không chĩa ra ngoài.
                var crankX = bar.Position.Xmm;
                var crankY = bar.Position.Ymm;
                if (dirX != 0 && bar.Position.Xmm != 0) crankX -= Math.Sign(bar.Position.Xmm) * crankMm;
                if (dirY != 0 && bar.Position.Ymm != 0) crankY -= Math.Sign(bar.Position.Ymm) * crankMm;
                curves = new List<Curve>
                {
                    Line.CreateBound(footStart, bottom),
                    Line.CreateBound(bottom, ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, crankStartFeet)),
                    Line.CreateBound(ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, crankStartFeet),
                                     ToWorld(item, crankX, crankY, slopeTopFeet)),
                    Line.CreateBound(ToWorld(item, crankX, crankY, slopeTopFeet),
                                     ToWorld(item, crankX, crankY, topFeet))
                };
            }
            else
            {
                curves = new List<Curve>
                {
                    Line.CreateBound(footStart, bottom),
                    // đoạn đứng từ đáy móng vươn lên cột
                    Line.CreateBound(bottom, ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, topFeet))
                };
            }

            // Crank dựng được thì dùng; lỗi hình học → fallback thanh chữ L thẳng (giữ chân thép chờ).
            var straight = new List<Curve>
            {
                Line.CreateBound(footStart, bottom),
                Line.CreateBound(bottom, ToWorld(item, bar.Position.Xmm, bar.Position.Ymm, topFeet))
            };

            if (TryCreateQuiet(document, () => CreateTie(document, host, curves, RebarStyle.Standard, barType, null, null))
                || TryCreate(document, $"Thép chờ {plan.Storey.LevelName}",
                    () => CreateTie(document, host, straight, RebarStyle.Standard, barType, null, null)))
                created++;
        }

        return created;
    }

    // ===== factory helpers (chạy trong SubTransaction của TryCreate) =====

    private static Rebar? CreateStirrup(Document document, FamilyInstance host, IList<Curve> curves,
        RebarBarType type, RebarHookType? hook, StirrupZone zone)
    {
        var rebar = RebarCompat.CreateFromCurves(document, RebarStyle.StirrupTie, type, hook, hook, host,
            XYZ.BasisZ, curves);
        if (rebar == null) return null;
        ApplyLayout(rebar, zone);
        return rebar;
    }

    private static Rebar? CreateTie(Document document, Element host, IList<Curve> curves,
        RebarStyle style, RebarBarType type, RebarHookType? hook, StirrupZone? zone)
    {
        var norm = style == RebarStyle.StirrupTie ? XYZ.BasisZ : ComputeBarNormal(curves);
        var rebar = RebarCompat.CreateFromCurves(document, style, type, hook, hook, host,
            norm, curves);
        if (rebar == null) return null;
        ApplyLayout(rebar, zone);
        return rebar;
    }

    /// <summary>
    ///     Pháp tuyến mặt phẳng chứa thanh thép: tích có hướng của cặp đoạn KHÔNG song song đầu tiên
    ///     (đúng cho thanh uốn nhiều đoạn: móc, chữ L, crank). Thanh thẳng → ⊥ bất kỳ.
    /// </summary>
    private static XYZ ComputeBarNormal(IList<Curve> curves)
    {
        var dirs = curves.Select(c => (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize()).ToList();
        for (var i = 0; i < dirs.Count; i++)
        for (var j = i + 1; j < dirs.Count; j++)
        {
            var n = dirs[i].CrossProduct(dirs[j]);
            if (n.GetLength() > 1e-9) return n.Normalize();
        }

        // Tất cả đoạn song song (thanh thẳng): pháp tuyến ngang ⊥; thẳng đứng → BasisX
        var horizontal = dirs[0].CrossProduct(XYZ.BasisZ);
        return horizontal.GetLength() > 1e-9 ? horizontal.Normalize() : XYZ.BasisX;
    }

    private static void ApplyLayout(Rebar rebar, StirrupZone? zone)
    {
        var accessor = rebar.GetShapeDrivenAccessor();
        if (zone is { Count: >= 2 })
            accessor.SetLayoutAsNumberWithSpacing(zone.Count, ToFeet(zone.SpacingMm), true, true, true);
        else
            accessor.SetLayoutAsSingle();
    }

    private static List<XYZ> OrderedLoopPoints(ColumnStackItem item, IReadOnlyList<Point2D> loop, double zFeet)
    {
        // Bắt đầu từ góc (+hw,+hh) ngược chiều kim đồng hồ → móc gập vào trong lõi
        var ordered = new[] { loop[2], loop[3], loop[0], loop[1] };
        return ordered.Select(p => ToWorld(item, p.Xmm, p.Ymm, zFeet)).ToList();
    }

    private static List<Curve> ClosedLoopCurves(IReadOnlyList<XYZ> points)
    {
        var curves = new List<Curve>(points.Count);
        for (var k = 0; k < points.Count; k++)
            curves.Add(Line.CreateBound(points[k], points[(k + 1) % points.Count]));
        return curves;
    }

    private static XYZ ToWorld(ColumnStackItem item, double xMm, double yMm, double zFeet)
    {
        var x = ToFeet(xMm);
        var y = ToFeet(yMm);
        var cos = Math.Cos(item.RotationRad);
        var sin = Math.Sin(item.RotationRad);
        return new XYZ(item.CenterXFeet + x * cos - y * sin, item.CenterYFeet + x * sin + y * cos, zFeet);
    }

    private static RebarHookType? FindHook135(Document document) => FindHookByAngle(document, Hook135Radians, "135");

    private static RebarHookType? FindHook180(Document document) => FindHookByAngle(document, Hook180Radians, "180");

    private static RebarHookType? FindHookByAngle(Document document, double angleRad, string nameHint)
    {
        var hooks = new FilteredElementCollector(document)
            .OfClass(typeof(RebarHookType))
            .Cast<RebarHookType>()
            .ToList();

        foreach (var hook in hooks)
        {
            var angle = hook.get_Parameter(BuiltInParameter.REBAR_HOOK_ANGLE)?.AsDouble() ?? 0;
            if (Math.Abs(angle - angleRad) < 0.05) return hook;
        }

        return hooks.FirstOrDefault(h => h.Name.Contains(nameHint));
    }

    private static double ToFeet(double millimeters) => UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);

    private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
}
