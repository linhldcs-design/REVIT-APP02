using Autodesk.Revit.DB;
using RevitAPP.Core.Models;
using RevitAPP.Models;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>
///     Vẽ thép cột HÀNG LOẠT theo Mark: mỗi hệ cột lấy Mark → tìm preset đã lưu trùng tên → vẽ
///     đúng cấu hình từng tầng (per-floor) như dialog. Dùng cho gọi tự động (vd revit-mcp).
///     Tự mở Transaction riêng — KHÔNG gọi khi đang có transaction khác mở.
/// </summary>
public static class ColumnRebarBatchApi
{
    /// <summary>Kết quả một lượt vẽ batch.</summary>
    public sealed record BatchResult(
        int StacksDrawn, int StacksFailed, int MainBarCount, int StirrupSetCount, int StarterBarCount,
        IReadOnlyList<string> Messages);

    /// <summary>
    ///     Vẽ cho mọi hệ cột có Mark trùng tên một preset đã lưu. Cột không khớp preset bị bỏ qua.
    /// </summary>
    /// <param name="document">Tài liệu Revit.</param>
    /// <param name="onlyMarks">Giới hạn chỉ vẽ các Mark này (null = mọi Mark có preset).</param>
    public static BatchResult DrawByMarkFromPresets(Document document, IReadOnlyCollection<string>? onlyMarks = null)
    {
        using var transaction = new Transaction(document, "Vẽ thép cột theo Mark từ preset");
        transaction.Start();
        var result = RunBatch(document, onlyMarks);
        transaction.Commit();
        return result;
    }

    /// <summary>
    ///     Như <see cref="DrawByMarkFromPresets" /> nhưng KHÔNG tự mở Transaction — dùng khi caller đã
    ///     ở trong một Transaction đang mở (vd revit-mcp send_code_to_revit đã mở sẵn transaction).
    ///     Builder dùng SubTransaction nội bộ nên chạy đúng trong transaction có sẵn.
    /// </summary>
    public static BatchResult DrawByMarkFromPresetsInExistingTransaction(
        Document document, IReadOnlyCollection<string>? onlyMarks = null)
        => RunBatch(document, onlyMarks);

    private static BatchResult RunBatch(Document document, IReadOnlyCollection<string>? onlyMarks)
    {
        var presets = new ColumnRebarConfigStore().LoadAll(document)
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        if (presets.Count == 0)
            return new BatchResult(0, 0, 0, 0, 0, new[] { "Chưa có preset cấu hình nào được lưu." });

        var barTypes = new RebarBarTypeProvider().GetAll(document);
        if (barTypes.Count == 0)
            return new BatchResult(0, 0, 0, 0, 0, new[] { "Dự án chưa có loại thanh thép (RebarBarType)." });

        var markFilter = onlyMarks != null
            ? new HashSet<string>(onlyMarks, StringComparer.OrdinalIgnoreCase)
            : null;

        var seeds = CollectColumns(document, presets, markFilter);

        var detector = new ColumnStackDetector();
        var builder = new ColumnRebarBuilder();
        int stacksDrawn = 0, stacksFailed = 0, totalMain = 0, totalStirrup = 0, totalStarter = 0;
        var messages = new List<string>();

        // Dò hệ cột thẳng đứng bằng footprint (ColumnStackDetector xử lý cả cột thu tiết diện lệch tâm)
        // — KHÔNG gom bằng tâm XY làm tròn vì cột đổi tiết diện có thể lệch tâm > ngưỡng → tách nhầm hệ.
        // Mỗi cột chỉ thuộc một hệ; đã xử lý thì bỏ qua để không vẽ trùng.
        var processed = new HashSet<ElementId>();
        foreach (var seed in seeds)
        {
            if (processed.Contains(seed.Id)) continue;

            var mark = seed.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)!.AsString()!;
            var config = presets[mark];

            var stack = detector.BuildStack(document, seed, out var buildError);
            if (stack.Count == 0)
            {
                stacksFailed++;
                messages.Add($"{mark}: {buildError}");
                processed.Add(seed.Id);
                continue;
            }

            // Đánh dấu mọi đoạn trong hệ đã xử lý. Chỉ giữ đoạn cùng Mark với seed để một hệ
            // không nuốt cột Mark khác thẳng hàng (khác cấu tạo) — hiếm nhưng phòng.
            foreach (var item in stack) processed.Add(item.ColumnId);
            var sameMarkStack = stack
                .Where(it => (document.GetElement(it.ColumnId) as FamilyInstance)
                    ?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() == mark)
                .ToList();
            if (sameMarkStack.Count != stack.Count)
            {
                // Có cột Mark khác lẫn trong hệ thẳng hàng → dùng đúng danh sách cùng Mark.
                stack = detector.BuildStackFromColumns(document,
                    sameMarkStack.Select(it => (FamilyInstance)document.GetElement(it.ColumnId)).ToList(),
                    out buildError);
                if (stack.Count == 0) { stacksFailed++; messages.Add($"{mark}: {buildError}"); continue; }
            }

            var plans = BuildPlans(stack, config, barTypes);
            if (plans.Count != stack.Count)
            {
                stacksFailed++;
                messages.Add($"{mark}: thiếu cấu hình tầng — bỏ qua.");
                continue;
            }

            var result = builder.Build(
                document, stack, plans,
                LapFrom(config), StarterFrom(config), SpreadFrom(config),
                EndsFrom(config), config.AddPartition, TransitionFrom(config));

            stacksDrawn++;
            totalMain += result.MainBarCount;
            totalStirrup += result.StirrupSetCount;
            totalStarter += result.StarterBarCount;
            foreach (var warning in result.Warnings) messages.Add($"{mark}: {warning}");
        }

        return new BatchResult(stacksDrawn, stacksFailed, totalMain, totalStirrup, totalStarter, messages);
    }

    /// <summary>
    ///     Lấy các cột kết cấu có Mark khớp một preset (và trong markFilter nếu có), sắp theo cao độ chân.
    ///     Trả seed để dò hệ; việc gom thành hệ thẳng đứng do <see cref="ColumnStackDetector.BuildStack" /> lo.
    /// </summary>
    private static IReadOnlyList<FamilyInstance> CollectColumns(
        Document document, IReadOnlyDictionary<string, ColumnRebarConfig> presets, HashSet<string>? markFilter)
    {
        return new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(column =>
            {
                var mark = column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                if (string.IsNullOrWhiteSpace(mark) || !presets.ContainsKey(mark!)) return false;
                return markFilter == null || markFilter.Contains(mark!);
            })
            .OrderBy(BaseElevationFeet)
            .ToList();
    }

    private static double BaseElevationFeet(FamilyInstance column)
    {
        var levelId = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();
        var level = levelId != null ? column.Document.GetElement(levelId) as Level : null;
        var offset = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;
        return (level?.Elevation ?? 0) + offset;
    }

    /// <summary>Dựng plan từng tầng: map <see cref="ColumnRebarFloorConfig"/> theo tên tầng; dò lại loại thanh theo đường kính.</summary>
    private static IReadOnlyList<StoreyRebarPlan> BuildPlans(
        IReadOnlyList<ColumnStackItem> stack, ColumnRebarConfig config, IReadOnlyList<RebarBarTypeOption> barTypes)
    {
        var plans = new List<StoreyRebarPlan>(stack.Count);
        foreach (var item in stack)
        {
            var floor = config.Floors.FirstOrDefault(f =>
                            string.Equals(f.LevelName, item.Storey.LevelName, StringComparison.OrdinalIgnoreCase))
                        ?? config.Floors.FirstOrDefault();
            if (floor == null) continue;

            var mainBar = NearestByDiameter(barTypes, floor.MainBarDiameterMm);
            var stirrup = NearestByDiameter(barTypes, floor.StirrupDiameterMm);
            var distBar = floor.UseDistributionBar
                ? NearestByDiameter(barTypes, floor.DistributionBarDiameterMm)
                : null;

            var floorConfig = new FloorRebarConfig(
                mainBar.DiameterMm, floor.BarsX, floor.BarsY, stirrup.DiameterMm,
                floor.SpacingEndMm, floor.SpacingMidMm, floor.ConfineZoneLenMm,
                Math.Round(item.AutoBeamDepthMm),
                floor.UseDistributionBar, distBar?.DiameterMm ?? mainBar.DiameterMm, floor.StirrupSectionType);
            plans.Add(new StoreyRebarPlan(item.Storey, floorConfig, mainBar, stirrup, distBar));
        }

        return plans;
    }

    private static RebarLapOptions LapFrom(ColumnRebarConfig c) =>
        new(c.LapFactor, c.CoverMm, c.StaggerLap, c.LapPosition, c.LapDistanceFromBottomMm);

    private static FoundationStarterOptions? StarterFrom(ColumnRebarConfig c) =>
        c.FoundationEnabled
            ? new FoundationStarterOptions(true, c.FoundationHmMm, c.FoundationLbMm, c.FoundationDirection, c.FoundationSplitBothSides)
            : null;

    private static StirrupSpreadOptions SpreadFrom(ColumnRebarConfig c) =>
        new(c.DistanceToFirstStirrupMm, c.SpreadThroughBeam, c.MinConfineZoneMm, c.ConfineClearanceDivisor,
            c.ReinforceJoint, (int)Math.Max(2, c.JointStirrupCount), c.CrosstieDirection);

    private static ColumnEndOptions EndsFrom(ColumnRebarConfig c) =>
        new(c.TopHookBending, c.TopHookLengthMm, c.CrankAtLap);

    private static SectionTransitionOptions TransitionFrom(ColumnRebarConfig c) =>
        new(c.BendIfOffsetLeMm, c.SlopeRatioHdOverE, c.LargeStepMode, c.JointAnchorDownMm);

    private static RebarBarTypeOption NearestByDiameter(IReadOnlyList<RebarBarTypeOption> options, double targetMm) =>
        options.OrderBy(o => Math.Abs(o.DiameterMm - targetMm)).First();
}
