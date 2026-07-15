using Autodesk.Revit.DB;
using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services.Rebar;

namespace IsolatedFootingRebar.Services;

/// <summary>
///     Cấu hình tối giản cho vẽ lưới thép móng qua API headless (MCP/test). null/0 → dùng mặc định
///     theo screenshot (D6@150, hook đáy 600, hook trên 400).
/// </summary>
public sealed record FootingRebarApiOptions
{
    public int MeshDiameterMm { get; init; } = 6;
    public double BottomSpacingMm { get; init; } = 150;
    public double TopSpacingMm { get; init; } = 150;
    public double BottomHookMm { get; init; } = 600;
    public double TopHookMm { get; init; } = 400;
    public bool DrawTop { get; init; } = true;

    public double BottomCoverMm { get; init; } = 185;
    public double TopCoverMm { get; init; } = 35;
    public double SideCoverMm { get; init; } = 35;
}

/// <summary>
///     API tĩnh vẽ lưới thép móng KHÔNG cần dialog — dùng cho gọi tự động (MCP tool) hoặc script. Nhận
///     id móng + cấu hình tối giản, dựng <see cref="FootingRebarModel"/> rồi gọi
///     <see cref="FootingRebarOrchestrator"/> (orchestrator TỰ mở transaction). Caller KHÔNG mở transaction trước.
/// </summary>
public static class FootingRebarApi
{
    public static RebarCreationResult DrawForFooting(
        Document document, ElementId footingId, FootingRebarApiOptions? options = null)
        => Run(document, footingId, options, useExistingTransaction: false);

    /// <summary>Như <see cref="DrawForFooting" /> nhưng KHÔNG tự mở Transaction — caller đã có transaction (vd revit-mcp).</summary>
    public static RebarCreationResult DrawForFootingInExistingTransaction(
        Document document, ElementId footingId, FootingRebarApiOptions? options = null)
        => Run(document, footingId, options, useExistingTransaction: true);

    private static RebarCreationResult Run(
        Document document, ElementId footingId, FootingRebarApiOptions? options, bool useExistingTransaction)
    {
        options ??= new FootingRebarApiOptions();

        var foundation = document.GetElement(footingId);
        if (foundation is null || foundation.Category?.Id.ToValue() != (long)BuiltInCategory.OST_StructuralFoundation)
            return new RebarCreationResult(0, 0, 0, ["Element không phải móng kết cấu (Structural Foundation)."]);

        var diameter = new RebarDiameter(options.MeshDiameterMm);
        var model = new FootingRebarModel
        {
            BottomEnabled = true,
            BottomX = new LayerBarConfig { Diameter = diameter, SpacingMm = options.BottomSpacingMm, HookLengthMm = options.BottomHookMm },
            BottomY = new LayerBarConfig { Diameter = diameter, SpacingMm = options.BottomSpacingMm, HookLengthMm = options.BottomHookMm },
            TopEnabled = options.DrawTop,
            TopX = new LayerBarConfig { Diameter = diameter, SpacingMm = options.TopSpacingMm, HookLengthMm = options.TopHookMm },
            TopY = new LayerBarConfig { Diameter = diameter, SpacingMm = options.TopSpacingMm, HookLengthMm = options.TopHookMm },
            MidEnabled = false,
            VerticalEnabled = false,
            HorizontalEnabled = false,
            Cover = new CoverSettings
            {
                BottomMm = options.BottomCoverMm,
                TopMm = options.TopCoverMm,
                SideMm = options.SideCoverMm
            }
        };

        var orchestrator = new FootingRebarOrchestrator();
        return useExistingTransaction
            ? orchestrator.CreateInTransaction(document, foundation, model)
            : orchestrator.Create(document, foundation, model);
    }
}
