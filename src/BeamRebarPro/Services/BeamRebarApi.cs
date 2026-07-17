using Autodesk.Revit.DB;
using BeamRebarPro.Models;
using BeamRebarPro.Services.Rebar;

namespace BeamRebarPro.Services;

/// <summary>
///     Cấu hình vẽ thép dầm qua API headless (MCP). Expose ĐẦY ĐỦ các nhóm thép mà BeamRebarPro/UI hỗ trợ:
///     thép chủ trên/dưới (kèm móc bẻ xuống hai đầu), thép gia cường gối/nhịp (2 lớp), cốt đai, thép chống
///     phình, lớp bảo vệ. null/0 → dùng mặc định TCVN (<see cref="QuickSettingFactory.CreateDefault"/>).
/// </summary>
public sealed record BeamRebarApiOptions
{
    // ── Thép chủ ──────────────────────────────────────────────────────────────
    public int MainTopCount { get; init; } = 3;
    public int MainTopDiameterMm { get; init; } = 16;
    public int MainBottomCount { get; init; } = 3;
    public int MainBottomDiameterMm { get; init; } = 16;

    /// <summary>Chiều dài neo vào gối mỗi đầu thép chủ (mm). 0 → mặc định.</summary>
    public double MainAnchorLengthMm { get; init; }

    /// <summary>
    ///     Đoạn bẻ xuống hai đầu thép chủ lớp TRÊN (mm) — cố định cho mọi dầm. 0 = để thẳng.
    ///     Nếu <see cref="MainTopBendDownFromHeightMinusMm"/> &gt; 0 thì giá trị đó (tính theo H) được ưu tiên.
    /// </summary>
    public double MainTopBendDownLengthMm { get; init; }

    /// <summary>
    ///     Bẻ xuống thép chủ trên = chiều cao dầm − X (mm), tính RIÊNG theo H từng dầm. &gt;0 → bật.
    ///     Ví dụ X=100 với dầm H=450 → đoạn bẻ 350mm; dầm H=300 → 200mm. Khi bật, API chạy per-beam.
    /// </summary>
    public double MainTopBendDownFromHeightMinusMm { get; init; }

    // ── Thép gia cường TRÊN (quanh gối) ───────────────────────────────────────
    public bool TopAddEnabled { get; init; }
    public int TopAddCount { get; init; } = 2;
    public int TopAddDiameterMm { get; init; } = 16;
    /// <summary>Chiều dài mỗi bên gối (mm). 0 → TCVN 0.25L.</summary>
    public double TopAddLengthMm { get; init; }
    /// <summary>Đoạn bẻ móc xuống ở đầu biên (hai gối ngoài cùng) của thép gia cường trên (mm). 0 = không bẻ.</summary>
    public double TopAddEdgeHookDownMm { get; init; }

    public bool TopAddL2Enabled { get; init; }
    public int TopAddL2Count { get; init; } = 2;
    public int TopAddL2DiameterMm { get; init; } = 16;
    public double TopAddL2LengthMm { get; init; }
    public double TopAddL2EdgeHookDownMm { get; init; }

    // ── Thép gia cường DƯỚI (giữa nhịp) ───────────────────────────────────────
    public bool BottomAddEnabled { get; init; }
    public int BottomAddCount { get; init; } = 2;
    public int BottomAddDiameterMm { get; init; } = 16;
    public double BottomAddLengthMm { get; init; }

    public bool BottomAddL2Enabled { get; init; }
    public int BottomAddL2Count { get; init; } = 2;
    public int BottomAddL2DiameterMm { get; init; } = 16;
    public double BottomAddL2LengthMm { get; init; }

    // ── Cốt đai ───────────────────────────────────────────────────────────────
    public int StirrupDiameterMm { get; init; } = 6;
    public double StirrupSpacingEndMm { get; init; } = 150;
    public double StirrupSpacingMidMm { get; init; } = 200;

    // ── Thép chống phình ──────────────────────────────────────────────────────
    public bool AntiBulgeEnabled { get; init; }
    public int AntiBulgeDiameterMm { get; init; } = 12;
    public double AntiBulgeSpacingMm { get; init; } = 500;
    public double AntiBulgeHeightThresholdMm { get; init; } = 550;

    // ── Lớp bảo vệ ────────────────────────────────────────────────────────────
    public int CoverMm { get; init; } = 25;
}

/// <summary>
///     API tĩnh vẽ thép dầm KHÔNG cần dialog — dùng cho gọi tự động (MCP tool) hoặc script. Dựng
///     <see cref="QuickSettingModel"/> đầy đủ từ <see cref="BeamRebarApiOptions"/> rồi gọi
///     <see cref="BeamRebarOrchestrator"/> (orchestrator TỰ mở transaction). Caller KHÔNG mở transaction trước.
/// </summary>
public static class BeamRebarApi
{
    public static RebarCreationResult DrawForBeams(
        Document document, IReadOnlyList<ElementId> beamIds, BeamRebarApiOptions? options = null)
        => DrawForBeams(document, beamIds, options, useExistingTransaction: false);

    public static RebarCreationResult DrawForBeams(
        Document document, IReadOnlyList<ElementId> beamIds, QuickSettingModel model)
    {
        var beams = beamIds.Select(document.GetElement).OfType<FamilyInstance>().ToList();
        if (beams.Count == 0)
            return new RebarCreationResult(0, 0, 0, new[] { "Khong co dam hop le trong danh sach." });
        return Run(new BeamRebarOrchestrator(), document, beams, model, useExistingTransaction: false);
    }

    /// <summary>
    ///     Như <see cref="DrawForBeams(Document,IReadOnlyList{ElementId},BeamRebarApiOptions)" /> nhưng
    ///     KHÔNG tự mở Transaction — dùng khi caller đã ở trong một Transaction đang mở
    ///     (vd revit-mcp send_code_to_revit đã mở sẵn transaction).
    /// </summary>
    public static RebarCreationResult DrawForBeamsInExistingTransaction(
        Document document, IReadOnlyList<ElementId> beamIds, BeamRebarApiOptions? options = null)
        => DrawForBeams(document, beamIds, options, useExistingTransaction: true);

    private static RebarCreationResult DrawForBeams(
        Document document, IReadOnlyList<ElementId> beamIds, BeamRebarApiOptions? options, bool useExistingTransaction)
    {
        options ??= new BeamRebarApiOptions();

        var beams = beamIds
            .Select(document.GetElement)
            .OfType<FamilyInstance>()
            .ToList();
        if (beams.Count == 0)
            return new RebarCreationResult(0, 0, 0, new[] { "Không có dầm (FamilyInstance) hợp lệ trong danh sách." });

        // Móc bẻ theo H khác nhau mỗi dầm → phải build model riêng cho từng dầm, chạy orchestrator từng cái.
        if (options.MainTopBendDownFromHeightMinusMm > 0)
            return DrawPerBeam(document, beams, options, useExistingTransaction);

        var model = BuildModel(options, mainTopBendDownMm: options.MainTopBendDownLengthMm);
        return Run(new BeamRebarOrchestrator(), document, beams, model, useExistingTransaction);
    }

    private static RebarCreationResult Run(BeamRebarOrchestrator orchestrator, Document document,
        IReadOnlyList<FamilyInstance> beams, QuickSettingModel model, bool useExistingTransaction)
        => useExistingTransaction
            ? orchestrator.CreateInTransaction(document, beams, model)
            : orchestrator.Create(document, beams, model);

    /// <summary>Chạy từng dầm một để đoạn bẻ móc trên = (chiều cao dầm − X) đúng cho mỗi tiết diện.</summary>
    private static RebarCreationResult DrawPerBeam(
        Document document, IReadOnlyList<FamilyInstance> beams, BeamRebarApiOptions options, bool useExistingTransaction)
    {
        var reader = new BeamGeometryReader();
        var orchestrator = new BeamRebarOrchestrator();
        int longitudinal = 0, stirrup = 0, antiBulge = 0;
        var warnings = new List<string>();

        foreach (var beam in beams)
        {
            double bendMm = options.MainTopBendDownLengthMm;
            if (reader.TryRead(beam, out var segment, out var readError))
            {
                var byHeight = segment.Section.HeightMm - options.MainTopBendDownFromHeightMinusMm;
                if (byHeight > 0) bendMm = byHeight;
            }
            else
            {
                warnings.Add(readError);
            }

            var model = BuildModel(options, mainTopBendDownMm: bendMm);
            var result = Run(orchestrator, document, new[] { beam }, model, useExistingTransaction);
            longitudinal += result.LongitudinalCount;
            stirrup += result.StirrupCount;
            antiBulge += result.AntiBulgeCount;
            warnings.AddRange(result.Warnings);
        }

        return new RebarCreationResult(longitudinal, stirrup, antiBulge, warnings);
    }

    private static QuickSettingModel BuildModel(BeamRebarApiOptions o, double mainTopBendDownMm)
    {
        return QuickSettingFactory.CreateDefault() with
        {
            MainTop = new MainBarConfig
            {
                Count = o.MainTopCount,
                Diameter = new RebarDiameter(o.MainTopDiameterMm),
                AnchorLengthMm = o.MainAnchorLengthMm,
                TopEndBendDownLengthMm = mainTopBendDownMm
            },
            MainBottom = new MainBarConfig
            {
                Count = o.MainBottomCount,
                Diameter = new RebarDiameter(o.MainBottomDiameterMm),
                AnchorLengthMm = o.MainAnchorLengthMm
            },

            TopAdditional = new AdditionalBarConfig
            {
                Enabled = o.TopAddEnabled,
                Count = o.TopAddCount,
                Diameter = new RebarDiameter(o.TopAddDiameterMm),
                Layer = 1,
                LengthMm = o.TopAddLengthMm,
                EdgeHookDownLengthMm = o.TopAddEdgeHookDownMm,
                // Móc bẻ xuống đầu ngoài tại hai gối biên: bộ dựng thanh đọc từ DLeft/DRight, nên map cả hai.
                DLeftMm = o.TopAddEdgeHookDownMm,
                DRightMm = o.TopAddEdgeHookDownMm,
                Side = AdditionalBarSide.TopAtSupport
            },
            TopAdditionalLayer2 = new AdditionalBarConfig
            {
                Enabled = o.TopAddL2Enabled,
                Count = o.TopAddL2Count,
                Diameter = new RebarDiameter(o.TopAddL2DiameterMm),
                Layer = 2,
                LengthMm = o.TopAddL2LengthMm,
                EdgeHookDownLengthMm = o.TopAddL2EdgeHookDownMm,
                // Móc bẻ xuống đầu ngoài tại hai gối biên: bộ dựng thanh đọc từ DLeft/DRight, nên map cả hai.
                DLeftMm = o.TopAddL2EdgeHookDownMm,
                DRightMm = o.TopAddL2EdgeHookDownMm,
                Side = AdditionalBarSide.TopAtSupport
            },
            BottomAdditional = new AdditionalBarConfig
            {
                Enabled = o.BottomAddEnabled,
                Count = o.BottomAddCount,
                Diameter = new RebarDiameter(o.BottomAddDiameterMm),
                Layer = 1,
                LengthMm = o.BottomAddLengthMm,
                Side = AdditionalBarSide.BottomAtMidspan
            },
            BottomAdditionalLayer2 = new AdditionalBarConfig
            {
                Enabled = o.BottomAddL2Enabled,
                Count = o.BottomAddL2Count,
                Diameter = new RebarDiameter(o.BottomAddL2DiameterMm),
                Layer = 2,
                LengthMm = o.BottomAddL2LengthMm,
                Side = AdditionalBarSide.BottomAtMidspan
            },

            Stirrup = new StirrupConfig
            {
                Diameter = new RebarDiameter(o.StirrupDiameterMm),
                Mode = StirrupMode.TwoEnds,
                SpacingEndMm = o.StirrupSpacingEndMm,
                SpacingMidMm = o.StirrupSpacingMidMm
            },

            AntiBulge = new AntiBulgeConfig
            {
                Enabled = o.AntiBulgeEnabled,
                Diameter = new RebarDiameter(o.AntiBulgeDiameterMm),
                SpacingMm = o.AntiBulgeSpacingMm,
                HeightThresholdMm = o.AntiBulgeHeightThresholdMm
            },

            Cover = new CoverSettings { TopMm = o.CoverMm, BottomMm = o.CoverMm, SideMm = o.CoverMm }
        };
    }
}
