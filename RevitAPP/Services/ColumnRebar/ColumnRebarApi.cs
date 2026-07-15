using Autodesk.Revit.DB;
using RevitAPP.Core.Models;
using RevitAPP.Models;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>
///     API tĩnh vẽ thép cột KHÔNG cần dialog — dùng cho gọi tự động (vd qua revit-mcp send_code_to_revit)
///     hoặc script. Tự dò hệ cột thẳng hàng + sinh plan với cấu hình đưa vào, rồi gọi
///     <see cref="ColumnRebarBuilder" />. CALLER phải đang ở trong một Transaction đang mở
///     (builder dùng SubTransaction nội bộ) — phù hợp với context revit-mcp (đã có transaction sẵn).
/// </summary>
public static class ColumnRebarApi
{
    /// <summary>
    ///     Vẽ thép chủ + đai cho các cột chỉ định bằng cấu hình mặc định/đưa vào.
    ///     Một cột → tự dò cả hệ thẳng hàng các tầng; nhiều cột → dùng đúng các cột đã chọn.
    /// </summary>
    /// <param name="document">Tài liệu Revit (phải có transaction đang mở).</param>
    /// <param name="columnIds">ElementId các cột (FamilyInstance Structural Column).</param>
    /// <param name="options">Cấu hình thép; null → mặc định.</param>
    public static RebarBuildResult DrawForColumns(
        Document document, IReadOnlyList<ElementId> columnIds, ColumnRebarApiOptions? options = null)
    {
        options ??= new ColumnRebarApiOptions();

        var columns = columnIds
            .Select(document.GetElement)
            .OfType<FamilyInstance>()
            .ToList();
        if (columns.Count == 0)
            return new RebarBuildResult(0, 0, 0, new[] { "Không có cột (FamilyInstance) hợp lệ trong danh sách." });

        var detector = new ColumnStackDetector();
        var items = columns.Count == 1
            ? detector.BuildStack(document, columns[0], out _)
            : detector.BuildStackFromColumns(document, columns, out _);
        if (items.Count == 0)
            return new RebarBuildResult(0, 0, 0, new[] { "Không tìm thấy cột hợp lệ." });

        var barTypes = new RebarBarTypeProvider().GetAll(document);
        if (barTypes.Count == 0)
            return new RebarBuildResult(0, 0, 0, new[] { "Dự án chưa có loại thanh thép (RebarBarType)." });

        // Chọn loại thanh theo đường kính yêu cầu (gần nhất); fallback thanh đầu danh sách.
        var mainBar = NearestByDiameter(barTypes, options.MainBarDiameterMm);
        var stirrup = NearestByDiameter(barTypes, options.StirrupDiameterMm);
        var distBar = options.UseDistributionBar
            ? NearestByDiameter(barTypes, options.DistributionBarDiameterMm)
            : null;

        var lap = new RebarLapOptions(options.LapFactor, options.CoverMm, options.StaggerLap, options.LapPosition);
        var ends = new ColumnEndOptions(options.TopHookBending, options.TopHookLengthMm, options.CrankAtLap);
        var starter = options.FoundationStarter
            ? new FoundationStarterOptions(true, options.FoundationHmMm, options.FoundationLbMm,
                options.FoundationDirection, options.FoundationSplitBothSides)
            : (FoundationStarterOptions?)null;

        // Nhiều cột truyền vào có thể thuộc nhiều trục khác nhau. Gom theo tâm XY thành từng hệ cột
        // riêng rồi vẽ độc lập, để mỗi hệ có đúng cột chân (thép chờ móng) và cột đỉnh (móc đỉnh).
        var builder = new ColumnRebarBuilder();
        var totalMain = 0;
        var totalStirrup = 0;
        var totalStarter = 0;
        var allErrors = new List<string>();
        foreach (var group in GroupByPlanLocation(items))
        {
            var stack = group.OrderBy(it => it.Storey.BaseElevationMm).ToList();
            var plans = stack.Select(item =>
            {
                var config = new FloorRebarConfig(
                    mainBar.DiameterMm, options.BarsX, options.BarsY, stirrup.DiameterMm,
                    options.SpacingEndMm, options.SpacingMidMm, options.ConfineZoneLenMm,
                    Math.Round(item.AutoBeamDepthMm),
                    options.UseDistributionBar, distBar?.DiameterMm ?? mainBar.DiameterMm,
                    options.StirrupSectionType);
                return new StoreyRebarPlan(item.Storey, config, mainBar, stirrup, distBar);
            }).ToList();

            var result = builder.Build(
                document, stack, plans, lap, starter: starter, ends: ends, addPartition: options.AddPartition);
            totalMain += result.MainBarCount;
            totalStirrup += result.StirrupSetCount;
            totalStarter += result.StarterBarCount;
            allErrors.AddRange(result.Warnings);
        }

        return new RebarBuildResult(totalMain, totalStirrup, totalStarter, allErrors);
    }

    // 50mm — đủ rộng để gộp sai số đặt cột, đủ hẹp để tách hai trục cột liền kề.
    private const double PlanGroupToleranceFeet = 50d / 304.8;

    /// <summary>Gom các đoạn cột theo tâm XY (làm tròn) thành từng hệ cột thẳng hàng.</summary>
    private static IEnumerable<IReadOnlyList<ColumnStackItem>> GroupByPlanLocation(
        IReadOnlyList<ColumnStackItem> items)
        => items
            .GroupBy(it => (
                X: Math.Round(it.CenterXFeet / PlanGroupToleranceFeet),
                Y: Math.Round(it.CenterYFeet / PlanGroupToleranceFeet)))
            .Select(g => (IReadOnlyList<ColumnStackItem>)g.ToList());

    private static RebarBarTypeOption NearestByDiameter(IReadOnlyList<RebarBarTypeOption> options, double targetMm) =>
        options.OrderBy(o => Math.Abs(o.DiameterMm - targetMm)).First();
}

/// <summary>Cấu hình mặc định cho <see cref="ColumnRebarApi" /> — khớp default của dialog.</summary>
public sealed record ColumnRebarApiOptions(
    double MainBarDiameterMm = 16,
    double StirrupDiameterMm = 8,
    int BarsX = 3,
    int BarsY = 3,
    double CoverMm = 25,
    double LapFactor = 40,
    bool StaggerLap = true,
    LapPosition LapPosition = LapPosition.NearBottom,
    double SpacingEndMm = 100,
    double SpacingMidMm = 200,
    double ConfineZoneLenMm = 0,
    bool UseDistributionBar = false,
    double DistributionBarDiameterMm = 8,
    SectionStirrupType StirrupSectionType = SectionStirrupType.ClosedTie,
    bool AddPartition = true,
    bool CrankAtLap = false,
    bool TopHookBending = false,
    double TopHookLengthMm = 100,
    bool FoundationStarter = false,
    double FoundationHmMm = 250,
    double FoundationLbMm = 200,
    StarterBendDirection FoundationDirection = StarterBendDirection.Right,
    bool FoundationSplitBothSides = false);
