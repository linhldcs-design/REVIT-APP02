using Autodesk.Revit.DB;
using BeamRebar.Addin.Models;
using BeamRebar.Core.Models;
using BeamRebar.Core.Services;

namespace BeamRebar.Addin.Services.Rebar;

/// <summary>
///     Điều phối tạo toàn bộ cốt thép cho các dầm đã chọn theo <see cref="QuickSettingModel"/>, trong
///     MỘT transaction. Mỗi dầm host thép của chính nó (Revit yêu cầu Rebar hosted trong element chứa).
///     Dùng <see cref="SpanModelBuilder"/> để gom cảnh báo liên tục (gối, tiết diện) cho dầm nhiều nhịp.
///     v1: thép nhịp theo từng host beam, neo thẳng vào gối; neo gối liên tục uốn móc defer v2.
/// </summary>
public sealed class BeamRebarOrchestrator
{
    public RebarCreationResult Create(Document document, IReadOnlyList<FamilyInstance> beams, QuickSettingModel model)
    {
        var warnings = new List<string>();
        var families = new RebarFamilyValidator(document);

        var familyErrors = families.Validate(model);
        if (familyErrors.Count > 0)
            return new RebarCreationResult(0, 0, 0, familyErrors);

        // Đọc geometry từng dầm, giữ host + segment.
        var reader = new BeamGeometryReader();
        var hosted = new List<(FamilyInstance Host, BeamSegment Segment)>();
        foreach (var beam in beams)
        {
            if (reader.TryRead(beam, out var segment, out var error))
                hosted.Add((beam, segment));
            else
                warnings.Add(error);
        }

        if (hosted.Count == 0)
            return new RebarCreationResult(0, 0, 0, warnings.Count > 0 ? warnings : ["Không đọc được dầm nào."]);

        // Cảnh báo liên tục (gối/tiết diện) cho dầm nhiều nhịp.
        var spanModel = SpanModelBuilder.Build(hosted.Select(h => h.Segment).ToList());
        warnings.AddRange(spanModel.Warnings);

        var longCreator = new LongitudinalBarCreator(document, families, model.Cover);
        var stirrupCreator = new StirrupCreator(document, families, model.Cover);
        var antiBulgeCreator = new AntiBulgeCreator(document, families, model.Cover);

        var longCount = 0;
        var stirrupCount = 0;
        var antiBulgeCount = 0;

        using var t = new Transaction(document, "Tạo thép dầm (TCVN)");
        t.Start();

        foreach (var (host, segment) in hosted)
        {
            SpanFrame frame;
            try
            {
                var span = new Span(0, segment.Start, segment.End, segment.Section);
                frame = new SpanFrame(span, segment.TopElevationFeet, segment.BottomElevationFeet);
            }
            catch (InvalidOperationException ex)
            {
                warnings.Add(ex.Message);
                continue;
            }

            longCount += CreateLongitudinal(host, frame, model, longCreator, warnings);
            stirrupCount += stirrupCreator.Create(host, frame, model.Stirrup, warnings);
            antiBulgeCount += antiBulgeCreator.Create(host, frame, model.AntiBulge, warnings);
        }

        t.Commit();

        return new RebarCreationResult(longCount, stirrupCount, antiBulgeCount, warnings);
    }

    private static int CreateLongitudinal(Element host, SpanFrame frame, QuickSettingModel model,
        LongitudinalBarCreator creator, List<string> warnings)
    {
        var count = 0;
        var layer2OffsetFeet = 30.0 / 304.8; // lớp 2 lùi vào ~30mm so với lớp 1.

        count += creator.Create(host, frame, model.MainTop.Diameter, model.MainTop.Count, atTop: true, 0, warnings);
        count += creator.Create(host, frame, model.MainBottom.Diameter, model.MainBottom.Count, atTop: false, 0, warnings);

        if (model.TopAdditional.Enabled)
            count += creator.Create(host, frame, model.TopAdditional.Diameter, model.TopAdditional.Count, true, 0, warnings);
        if (model.TopAdditionalLayer2.Enabled)
            count += creator.Create(host, frame, model.TopAdditionalLayer2.Diameter, model.TopAdditionalLayer2.Count, true, layer2OffsetFeet, warnings);
        if (model.BottomAdditional.Enabled)
            count += creator.Create(host, frame, model.BottomAdditional.Diameter, model.BottomAdditional.Count, false, 0, warnings);
        if (model.BottomAdditionalLayer2.Enabled)
            count += creator.Create(host, frame, model.BottomAdditionalLayer2.Diameter, model.BottomAdditionalLayer2.Count, false, layer2OffsetFeet, warnings);

        return count;
    }
}
