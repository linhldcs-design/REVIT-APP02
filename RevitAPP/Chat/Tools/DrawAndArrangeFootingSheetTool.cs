using FootingDrawing.Addin.Services;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Core.Services;
using RevitAPP.Services.FootingDrawing;
using RevitAPP.Services.FootingSection;

namespace RevitAPP.Chat.Tools;

/// <summary>Điều phối C# trực tiếp để không cho LLM ghép hoặc chép nhầm viewport id.</summary>
public sealed class DrawAndArrangeFootingSheetTool : IChatTool
{
    public string Name => "draw_and_arrange_footing_sheet";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(Name,
        "Vẽ đồng thời mặt bằng và mặt cắt móng theo preset đã lưu, rồi tự sắp mặt bằng trên/mặt cắt dưới bằng ID thật trong Revit. Dùng tool này khi người dùng yêu cầu triển khai hoàn chỉnh lên sheet.",
        new JsonSchemaBuilder()
            .IntegerArray("footingIds", "ElementId các móng đang chọn.")
            .Text("drawingPresetName", "Tên cấu hình Bản Vẽ Móng đã lưu.", true)
            .Text("sectionPresetName", "Tên cấu hình Mặt Cắt Móng đã lưu.", true)
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var footingIds = DrawFootingDrawingTool.ReadIds(input);
        var drawingPresetName = RequiredName(input, "drawingPresetName");
        var sectionPresetName = RequiredName(input, "sectionPresetName");
        var drawingSetting = new JsonSettingStore().Load()
            .FirstOrDefault(value => string.Equals(value.Name, drawingPresetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Không tìm thấy cấu hình Bản Vẽ Móng '{drawingPresetName}'.");
        var sectionSetting = new FootingSectionPresetStore().Load()
            .FirstOrDefault(value => string.Equals(value.SettingName, sectionPresetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Không tìm thấy cấu hình Mặt Cắt Móng '{sectionPresetName}'.");

        var footings = footingIds.Select(id => ctx.Doc.GetElement(ChatElementIdCompat.Create(id))).ToList();
        if (footings.Any(element => !DrawFootingDrawingTool.IsStructuralFoundation(element)))
            throw new ArgumentException("footingIds chứa phần tử không tồn tại hoặc không phải Structural Foundation.");

        using var releaseGroup = new TransactionGroup(ctx.Doc, "Vẽ và sắp bản vẽ móng");
        releaseGroup.Start();
        long targetSheetId;
        int completed;
        try
        {
            var drawingOrchestrator = new FootingDrawingOrchestrator();
            var sectionReader = new RevitAPP.Services.FootingSection.FootingGeometryReader();
            var sectionOrchestrator = new FootingSectionOrchestrator { Annotator = new FootingSectionAnnotator() };
            var pairs = new List<FootingViewportPair>();
            long? batchSheetId = null;

            foreach (var footing in footings.Cast<Element>())
            {
                var footingId = ChatElementIdCompat.Value(footing.Id);
                var plan = drawingOrchestrator.Generate(ctx.Doc, footing, drawingSetting);
                if (!sectionReader.TryRead(ctx.Doc, footing, sectionSetting.Direction,
                        sectionSetting.ViewBottomLevelName, sectionSetting.ViewTopLevelName,
                        out var geometry, out var geometryError))
                    throw new InvalidOperationException($"Móng {footingId}: {geometryError}");
                var section = sectionOrchestrator.Generate(ctx.Doc, footing, geometry, sectionSetting);
                if (plan.ViewportId <= 0 || section.ViewportId is null or <= 0 || section.SheetId is null)
                    throw new InvalidOperationException($"Móng {footingId}: không nhận được viewport hợp lệ sau khi vẽ.");
                if (plan.SheetId != section.SheetId.Value)
                    throw new InvalidOperationException($"Móng {footingId}: mặt bằng và mặt cắt được tạo trên hai sheet khác nhau. Hãy lưu hai preset cùng một sheet.");
                if (batchSheetId.HasValue && batchSheetId.Value != plan.SheetId)
                    throw new InvalidOperationException("Các móng được tạo trên nhiều sheet; không thể xếp chung trong một lần.");

                batchSheetId = plan.SheetId;
                pairs.Add(new FootingViewportPair(geometry.Mark, footingId, footingId,
                    plan.ViewportId, section.ViewportId.Value));
            }

            if (!batchSheetId.HasValue) throw new InvalidOperationException("Không tạo được bản vẽ móng nào.");
            // Khung tên mẫu của RevitAPP chiếm khoảng 13-15% chiều ngang; dùng 15% và khe 10 mm.
            new FootingSheetLayoutService().Arrange(ctx.Doc, batchSheetId.Value, pairs, 0.15, 10);
            targetSheetId = batchSheetId.Value;
            completed = pairs.Count;
            releaseGroup.Assimilate();
        }
        catch
        {
            releaseGroup.RollBack();
            throw;
        }

        // Đổi active view không thuộc model transaction; lỗi UI không được làm sai trạng thái commit/rollback.
        try { DrawFootingDrawingTool.ActivateLastSheet(ctx, targetSheetId); }
        catch { /* Bản vẽ đã tạo thành công; người dùng có thể mở sheet thủ công. */ }
        return new
        {
            success = true,
            message = $"Đã vẽ và sắp {completed} cặp móng: mặt bằng trên, mặt cắt dưới, tên view ngay dưới hình.",
            sheetId = targetSheetId,
            completed
        };
    }

    private static string RequiredName(JObject input, string key) =>
        input.Value<string?>(key)?.Trim() is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"Thiếu '{key}'.");
}
