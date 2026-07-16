using Autodesk.Revit.DB;
using WallRebar.Models;
using WallRebar.Services.Rebar;

namespace WallRebar.Services;

/// <summary>
///     Cấu hình tối giản cho vẽ thép tường qua API headless (MCP/script). null/0 → dùng mặc định theo
///     screenshot dialog (D6@150 dọc, D6@200 ngang, D6@500 tie; cover 25; bottom offset 250; hook 100/200).
/// </summary>
public sealed record WallRebarApiOptions
{
    public int VerticalDiameterMm { get; init; } = 6;
    public double VerticalSpacingMm { get; init; } = 150;
    public int HorizontalDiameterMm { get; init; } = 6;
    public double HorizontalSpacingMm { get; init; } = 200;
    public int TieDiameterMm { get; init; } = 6;
    public double TieSpacingMm { get; init; } = 500;
    public bool TieEnabled { get; init; } = true;

    public double CoverTopBottomMm { get; init; } = 25;
    public double CoverLeftRightMm { get; init; } = 25;
    public double CoverStartEndMm { get; init; } = 25;

    /// <summary>"Closed" | "Half" | "Straight" (không phân biệt hoa thường). Khác → Closed.</summary>
    public string TopHookType { get; init; } = "Closed";
    public string TopHookDirection { get; init; } = "Inward";
    public double TopHookLengthMm { get; init; } = 100;
    public string BottomHookType { get; init; } = "Closed";
    public string BottomHookDirection { get; init; } = "Inward";
    public double BottomHookLengthMm { get; init; } = 200;

    public double TopOffsetMm { get; init; }
    public double BottomOffsetMm { get; init; } = 250;
    public double HorizontalOffsetStartMm { get; init; }
    public double HorizontalOffsetEndMm { get; init; }

    public bool DrawAdditionalRebar { get; init; }
}

/// <summary>
///     API tĩnh vẽ thép tường KHÔNG cần dialog — dùng cho gọi tự động (MCP tool) hoặc script. Nhận id tường
///     + cấu hình tối giản, dựng <see cref="WallRebarModel"/> rồi gọi <see cref="WallRebarOrchestrator"/>
///     (orchestrator TỰ mở transaction). Caller KHÔNG mở transaction trước.
/// </summary>
public static class WallRebarApi
{
    public static RebarCreationResult DrawForWall(
        Document document, ElementId wallId, WallRebarApiOptions? options = null)
        => Run(document, wallId, options, useExistingTransaction: false);

    /// <summary>Như <see cref="DrawForWall" /> nhưng KHÔNG tự mở Transaction — caller đã có transaction (vd revit-mcp).</summary>
    public static RebarCreationResult DrawForWallInExistingTransaction(
        Document document, ElementId wallId, WallRebarApiOptions? options = null)
        => Run(document, wallId, options, useExistingTransaction: true);

    private static RebarCreationResult Run(
        Document document, ElementId wallId, WallRebarApiOptions? options, bool useExistingTransaction)
    {
        options ??= new WallRebarApiOptions();

        if (document.GetElement(wallId) is not Wall wall)
            return new RebarCreationResult(0, 0, 0, ["Element không phải tường (Wall)."]);

        var model = new WallRebarModel
        {
            Cover = new CoverSettings
            {
                TopBottomMm = options.CoverTopBottomMm,
                LeftRightMm = options.CoverLeftRightMm,
                StartEndMm = options.CoverStartEndMm
            },
            Vertical = new WallLayerConfig
            {
                Diameter = new RebarDiameter(options.VerticalDiameterMm),
                SpacingMm = options.VerticalSpacingMm
            },
            Horizontal = new WallLayerConfig
            {
                Diameter = new RebarDiameter(options.HorizontalDiameterMm),
                SpacingMm = options.HorizontalSpacingMm
            },
            Tie = new WallLayerConfig
            {
                Enabled = options.TieEnabled,
                Diameter = new RebarDiameter(options.TieDiameterMm),
                SpacingMm = options.TieSpacingMm
            },
            TopHookType = ParseHook(options.TopHookType),
            TopHookDirection = ParseHookDirection(options.TopHookDirection),
            TopHookLengthMm = options.TopHookLengthMm,
            BottomHookType = ParseHook(options.BottomHookType),
            BottomHookDirection = ParseHookDirection(options.BottomHookDirection),
            BottomHookLengthMm = options.BottomHookLengthMm,
            TopOffsetMm = options.TopOffsetMm,
            BottomOffsetMm = options.BottomOffsetMm,
            HorizontalOffsetStartMm = options.HorizontalOffsetStartMm,
            HorizontalOffsetEndMm = options.HorizontalOffsetEndMm,
            DrawAdditionalRebar = options.DrawAdditionalRebar
        };

        var orchestrator = new WallRebarOrchestrator();
        return useExistingTransaction
            ? orchestrator.CreateInTransaction(document, wall, model)
            : orchestrator.Create(document, wall, model);
    }

    private static HookType ParseHook(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "half" => HookType.Half,
        "straight" => HookType.Straight,
        _ => HookType.Closed
    };

    private static HookBendDirection ParseHookDirection(string? value) =>
        value?.Trim().ToLowerInvariant() == "outward"
            ? HookBendDirection.Outward
            : HookBendDirection.Inward;
}
