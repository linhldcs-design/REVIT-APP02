using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Core.Chat.BeamLongitudinalDrawing;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using RevitAPP.Helpers;
using RevitAPP.Services.BeamLongitudinalDrawing;

namespace RevitAPP.Chat.Tools.BeamLongitudinalDrawing;

public sealed class DrawBeamLongitudinalDrawingTool : IChatTool, IConfirmableChatTool
{
    public string Name => "draw_beam_longitudinal_drawing";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;

    public ToolSchema Schema => new(Name,
        "Vẽ mặt cắt dọc cho nhiều dầm vào các sheet có sẵn còn trống. Chỉ chia dependent view khi view dọc không vừa vùng vẽ; điểm chia là lưới gần trung điểm dầm nhất.",
        new JsonSchemaBuilder()
            .IntegerArray("beamIds", "ElementId các dầm kết cấu; bỏ trống để dùng selection Revit.")
            .Integer("beamsPerSheet", "Số dầm tối đa trên mỗi sheet.", true)
            .TextArray("sheetNumbers", "Các sheet có sẵn còn trống, theo thứ tự phân bổ.", true)
            .Text("presetName", "Tên chính xác cấu hình Mặt Cắt Dọc Dầm đã lưu.", true)
            .Bool("reverseDirection", "Đảo hướng toàn bộ view dọc; mặc định false.")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var beamIds = ReadLongArray(input, "beamIds");
        var beamsPerSheet = input.Value<int?>("beamsPerSheet")
                            ?? throw new ArgumentException("Thiếu 'beamsPerSheet'.");
        var sheetNumbers = ReadStringArray(input, "sheetNumbers");
        var presetName = input.Value<string?>("presetName")?.Trim();
        if (string.IsNullOrWhiteSpace(presetName)) throw new ArgumentException("Thiếu 'presetName'.");
        var reverse = input.Value<bool?>("reverseDirection") ?? false;

        var assignments = LongitudinalBatchAssignmentPlanner.Plan(
            beamIds, beamsPerSheet, sheetNumbers);
        var preset = new LongitudinalDrawingPresetStore().Load()
            .FirstOrDefault(value => string.Equals(
                value.SettingName, presetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Không tìm thấy cấu hình Mặt Cắt Dọc Dầm '{presetName}'.");
        var sheets = ResolveAndValidateSheets(ctx.Doc, assignments.Select(item => item.SheetNumber));

        var layout = new LongitudinalBatchSheetLayoutService();
        var splitter = new LongitudinalDependentViewSplitService();
        var placements = sheets.Keys.ToDictionary(
            number => number, _ => new List<LongitudinalChatPlacement>(),
            StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<BeamOutcome>();

        foreach (var assignment in assignments)
        {
            var createdViewIds = new List<long>();
            try
            {
                var beam = ctx.Doc.GetElement(ChatElementIdCompat.Create(assignment.BeamId)) as FamilyInstance;
                if (beam?.Category?.Id != ChatElementIdCompat.Create((long)BuiltInCategory.OST_StructuralFraming))
                    throw new ArgumentException($"Element {assignment.BeamId} không phải dầm kết cấu.");
                var sheet = sheets[assignment.SheetNumber];
                var setting = preset with
                {
                    SheetNumber = sheet.SheetNumber,
                    SheetName = sheet.Name
                };
                var review = BuildReview(ctx.Doc, beam, setting, reverse);

                // Ranh giới cố ý: gọi public API gốc đúng nguyên trạng; mọi split/layout nằm sau đó trong Chat.
                var result = new LongitudinalDrawingOrchestrator().Generate(ctx.Doc, [beam], review);
                createdViewIds.AddRange(result.LongitudinalViewIds);
                createdViewIds.AddRange(result.CrossSectionViewIds);
                if (result.LongitudinalViewIds.Count != 1)
                    throw new InvalidOperationException("Pipeline gốc không trả về đúng một view dọc.");

                var area = layout.ResolveDrawingArea(ctx.Doc, sheet);
                var split = splitter.SplitOnlyWhenRequired(
                    ctx.Doc, sheet, beam, result.LongitudinalViewIds[0], area.Width,
                    setting.DetailComponentTypeName);
                createdViewIds.AddRange(split.PlacedViewIds);
                placements[assignment.SheetNumber].Add(new LongitudinalChatPlacement(
                    assignment.BeamId, split.PlacedViewIds, result.CrossSectionViewIds));
                outcomes.Add(new BeamOutcome(
                    assignment.BeamId, assignment.SheetNumber, true, null,
                    result.LongitudinalViewIds, split.PlacedViewIds,
                    result.CrossSectionViewIds, split.WasSplit, split.GridName,
                    result.Warnings.Select(warning => warning.Message).ToList()));
            }
            catch (Exception exception)
            {
                var cleanupError = CleanupViews(ctx.Doc, createdViewIds);
                outcomes.Add(new BeamOutcome(
                    assignment.BeamId, assignment.SheetNumber, false,
                    AppendCleanupError(exception.Message, cleanupError),
                    [], [], [], false, null, []));
            }
        }

        foreach (var pair in placements.Where(pair => pair.Value.Count > 0))
        {
            try
            {
                layout.Arrange(ctx.Doc, sheets[pair.Key], pair.Value);
            }
            catch (Exception exception)
            {
                var affected = pair.Value.Select(value => value.BeamId).ToHashSet();
                var cleanupError = CleanupViews(ctx.Doc, outcomes
                    .Where(value => affected.Contains(value.BeamId))
                    .SelectMany(value => value.PrimaryLongitudinalViewIds
                        .Concat(value.PlacedLongitudinalViewIds)
                        .Concat(value.CrossSectionViewIds)));
                for (var index = 0; index < outcomes.Count; index++)
                {
                    if (!affected.Contains(outcomes[index].BeamId)) continue;
                    outcomes[index] = outcomes[index] with
                    {
                        Success = false,
                        Error = AppendCleanupError(exception.Message, cleanupError),
                        PlacedLongitudinalViewIds = [],
                        CrossSectionViewIds = []
                    };
                }
            }
        }

        var completed = outcomes.Count(value => value.Success);
        var lastSheet = outcomes.LastOrDefault(value => value.Success)?.SheetNumber;
        if (lastSheet != null)
        {
            try { ctx.UiDoc.ActiveView = sheets[lastSheet]; }
            catch { }
        }
        return new
        {
            success = completed == outcomes.Count,
            completed,
            failed = outcomes.Count - completed,
            message = $"Đã triển khai {completed}/{outcomes.Count} dầm.",
            beams = outcomes
        };
    }

    private static LongitudinalDrawingReviewResult BuildReview(
        Document document,
        FamilyInstance beam,
        LongitudinalDrawingSetting setting,
        bool reverse)
    {
        var reader = new LongitudinalBeamSelectionReader();
        if (!reader.TryRead(document, [beam], out var spans, out var profiles, out var error))
            throw new InvalidOperationException(error);
        var tolerance = new BeamChainTolerance(
            setting.EndpointToleranceMm / 304.8,
            setting.AlignmentToleranceMm / 304.8,
            setting.EndpointToleranceMm / 304.8);
        var chainResult = BeamChainBuilder.Build(spans, tolerance);
        if (!chainResult.IsValid || chainResult.Model == null)
            throw new InvalidOperationException(string.Join(" ", chainResult.Errors.Select(item => item.Message)));
        var chain = chainResult.Model;
        var inputs = spans.ToDictionary(span => span.SourceId);
        var profileBySource = profiles.ToDictionary(profile => profile.SourceId);
        var orderedProfiles = chain.Spans.Select(span =>
        {
            var profile = profileBySource[span.SourceId];
            var input = inputs[span.SourceId];
            var followsInput = span.Start.DistanceTo(input.Start) <= span.Start.DistanceTo(input.End);
            return followsInput
                ? profile
                : profile with { LeftSupport = profile.RightSupport, RightSupport = profile.LeftSupport };
        }).ToList();
        var stations = SectionStationPlanner.Plan(chain, orderedProfiles, reduceUniformSpans: true);
        return new LongitudinalDrawingReviewResult(setting, chain, stations, reverse);
    }

    private static Dictionary<string, ViewSheet> ResolveAndValidateSheets(
        Document document, IEnumerable<string> numbers)
    {
        var requested = numbers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sheets = new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(sheet => !sheet.IsPlaceholder && requested.Contains(
                sheet.SheetNumber, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(sheet => sheet.SheetNumber, StringComparer.OrdinalIgnoreCase);
        foreach (var number in requested)
        {
            if (!sheets.TryGetValue(number, out var sheet))
                throw new InvalidOperationException($"Không tìm thấy sheet có sẵn '{number}'.");
            var hasTitleBlock = new FilteredElementCollector(document, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().Any();
            if (!hasTitleBlock)
                throw new InvalidOperationException($"Sheet '{number}' không có title block.");
            var titleBlockCategoryId =
                ChatElementIdCompat.Create((long)BuiltInCategory.OST_TitleBlocks);
            var existingContent = new FilteredElementCollector(document, sheet.Id)
                .WhereElementIsNotElementType()
                .Where(element => element.Id != sheet.Id &&
                                  element.Category?.Id != titleBlockCategoryId &&
                                  element is not ScheduleSheetInstance
                                  {
                                      IsTitleblockRevisionSchedule: true
                                  })
                .Take(5)
                .Select(element => $"{element.GetType().Name} {ChatElementIdCompat.Value(element.Id)}")
                .ToList();
            if (existingContent.Count > 0)
                throw new InvalidOperationException(
                    $"Sheet '{number}' đã có nội dung ({string.Join(", ", existingContent)}). " +
                    "Tool chỉ dùng sheet có sẵn còn trống.");
        }
        return sheets;
    }

    private static string? CleanupViews(Document document, IEnumerable<long> ids)
    {
        var elementIds = ids.Where(id => id > 0).Distinct()
            .Select(ChatElementIdCompat.Create)
            .Where(id => document.GetElement(id) is View)
            .ToList();
        if (elementIds.Count == 0) return null;
        using var transaction = new Transaction(document, "Chat AI - dọn view dầm lỗi");
        transaction.Start();
        try
        {
            foreach (var id in elementIds)
                if (document.GetElement(id) != null) document.Delete(id);
            return transaction.Commit() == TransactionStatus.Committed
                ? null
                : "Revit không commit được transaction dọn view.";
        }
        catch (Exception exception)
        {
            if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
            return exception.Message;
        }
    }

    private static string AppendCleanupError(string original, string? cleanupError) =>
        string.IsNullOrWhiteSpace(cleanupError)
            ? original
            : $"{original} Không dọn được view lỗi: {cleanupError}";

    private static IReadOnlyList<long> ReadLongArray(JObject input, string name) =>
        input[name] is JArray values
            ? values.Values<long>().ToList()
            : throw new ArgumentException($"Thiếu '{name}'.");

    private static IReadOnlyList<string> ReadStringArray(JObject input, string name) =>
        input[name] is JArray values
            ? values.Values<string>().Where(value => value != null).Select(value => value!).ToList()
            : throw new ArgumentException($"Thiếu '{name}'.");

    private sealed record BeamOutcome(
        long BeamId,
        string SheetNumber,
        bool Success,
        string? Error,
        IReadOnlyList<long> PrimaryLongitudinalViewIds,
        IReadOnlyList<long> PlacedLongitudinalViewIds,
        IReadOnlyList<long> CrossSectionViewIds,
        bool WasSplit,
        string? SplitGrid,
        IReadOnlyList<string> Warnings);
}
