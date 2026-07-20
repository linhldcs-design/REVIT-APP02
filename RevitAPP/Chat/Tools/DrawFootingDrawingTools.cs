using Autodesk.Revit.DB;
using FootingDrawing.Addin.Services;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Core.Services;
using RevitAPP.Services.FootingDrawing;
using RevitAPP.Services.FootingSection;

namespace RevitAPP.Chat.Tools;

/// <summary>Sinh bản vẽ mặt bằng móng bằng đúng preset đã lưu, không mở dialog.</summary>
public sealed class DrawFootingDrawingTool : IChatTool
{
    public string Name => "draw_footing_drawing";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(
        Name,
        "Sinh Bản Vẽ Móng cho các móng đang chọn bằng đúng cấu hình đã lưu. Không mở cửa sổ cấu hình và không tự đoán preset.",
        new JsonSchemaBuilder()
            .IntegerArray("footingIds", "ElementId các móng; bỏ trống để dùng móng đang chọn.")
            .Text("presetName", "Tên cấu hình Bản Vẽ Móng đã lưu, ví dụ M1.", true)
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var footingIds = ReadIds(input);
        var presetName = RequiredPresetName(input);
        var setting = new JsonSettingStore().Load()
            .FirstOrDefault(value => string.Equals(value.Name, presetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Không tìm thấy cấu hình Bản Vẽ Móng '{presetName}'.");

        var footings = footingIds.Select(id => ctx.Doc.GetElement(ChatElementIdCompat.Create(id)))
            .Where(IsStructuralFoundation).ToList();
        if (footings.Count != footingIds.Count)
            throw new ArgumentException("footingIds chứa phần tử không tồn tại hoặc không thuộc Structural Foundations.");

        var orchestrator = new FootingDrawingOrchestrator();
        var results = new List<FootingDrawing.Core.Models.FootingDrawingResult>();
        var created = new List<object>();
        var errors = new List<string>();
        foreach (var footing in footings)
        {
            try
            {
                var result = orchestrator.Generate(ctx.Doc, footing!, setting);
                if (result.ViewportId <= 0)
                {
                    errors.Add($"Móng {ChatElementIdCompat.Value(footing!.Id)}: đã tạo view nhưng không đặt được viewport lên sheet.");
                    continue;
                }
                results.Add(result);
                created.Add(new
                {
                    footingId = ChatElementIdCompat.Value(footing!.Id),
                    mark = ReadMark(footing!),
                    sheetId = result.SheetId,
                    viewId = result.ViewId,
                    viewportId = result.ViewportId
                });
            }
            catch (Exception exception)
            {
                errors.Add($"Móng {ChatElementIdCompat.Value(footing!.Id)}: {exception.Message}");
            }
        }

        var warningList = results.SelectMany(result => result.Warnings).Distinct().ToList();

        ActivateLastSheet(ctx, results.LastOrDefault()?.SheetId);
        var warnings = warningList.Distinct().ToArray();
        var message = $"Đã tạo {results.Count}/{footings.Count} Bản Vẽ Móng bằng cấu hình '{setting.Name}'.";
        if (warnings.Length > 0) message += " | Cảnh báo: " + string.Join("; ", warnings);
        if (errors.Count > 0) message += " | Lỗi: " + string.Join("; ", errors);
        return new
        {
            success = errors.Count == 0 && results.Count == footings.Count,
            message,
            completed = results.Count,
            total = footings.Count,
            errors = errors.ToArray(),
            presetName = setting.Name,
            created,
            sheetIds = results.Select(result => result.SheetId).Where(id => id != 0).ToArray(),
            viewIds = results.Select(result => result.ViewId).Where(id => id != 0).ToArray()
        };
    }

    internal static List<long> ReadIds(JObject input)
    {
        var tokens = input["footingIds"] as JArray
                     ?? throw new ArgumentException("Thiếu 'footingIds' (mảng id móng).");
        var ids = tokens.Select(token => token.Value<long>()).Distinct().ToList();
        if (ids.Count == 0) throw new ArgumentException("'footingIds' rỗng; hãy chọn ít nhất một móng.");
        return ids;
    }

    internal static string RequiredPresetName(JObject input) =>
        input.Value<string?>("presetName")?.Trim() is { Length: > 0 } value
            ? value
            : throw new ArgumentException("Thiếu 'presetName' của cấu hình đã lưu.");

    internal static bool IsStructuralFoundation(Element? element) =>
        element?.Category?.Id == ChatElementIdCompat.Create((long)BuiltInCategory.OST_StructuralFoundation);

    internal static string ReadMark(Element element) =>
        element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim() ?? string.Empty;

    internal static void ActivateLastSheet(ChatToolContext ctx, long? sheetId)
    {
        if (sheetId is null or 0) return;
        if (ctx.Doc.GetElement(ChatElementIdCompat.Create(sheetId.Value)) is ViewSheet sheet)
            ctx.UiDoc.ActiveView = sheet;
    }
}

/// <summary>Sinh mặt cắt móng bằng đúng preset đã lưu, không mở dialog.</summary>
public sealed class DrawFootingSectionTool : IChatTool
{
    public string Name => "draw_footing_section";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(
        Name,
        "Sinh Mặt Cắt Móng cho móng đang chọn bằng đúng cấu hình đã lưu. Không mở cửa sổ cấu hình và không tự đoán preset.",
        new JsonSchemaBuilder()
            .IntegerArray("footingIds", "ElementId móng; bỏ trống để dùng móng đang chọn.")
            .Text("presetName", "Tên cấu hình Mặt Cắt Móng đã lưu.", true)
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var footingIds = DrawFootingDrawingTool.ReadIds(input);
        if (footingIds.Count != 1)
            throw new ArgumentException("Mặt Cắt Móng chỉ nhận đúng một móng mỗi lần để tránh trùng tên view/sheet.");
        var presetName = DrawFootingDrawingTool.RequiredPresetName(input);
        var setting = new FootingSectionPresetStore().Load()
            .FirstOrDefault(value => string.Equals(value.SettingName, presetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Không tìm thấy cấu hình Mặt Cắt Móng '{presetName}'.");
        var footings = footingIds.Select(id => ctx.Doc.GetElement(ChatElementIdCompat.Create(id)))
            .Where(DrawFootingDrawingTool.IsStructuralFoundation).ToList();
        if (footings.Count != footingIds.Count)
            throw new ArgumentException("footingIds chứa phần tử không tồn tại hoặc không thuộc Structural Foundations.");

        var results = new List<RevitAPP.Core.Models.FootingSection.FootingSectionResult>();
        var created = new List<object>();
        var errors = new List<string>();
        foreach (var footing in footings)
        {
            if (!new RevitAPP.Services.FootingSection.FootingGeometryReader().TryRead(ctx.Doc, footing!, setting.Direction,
                    setting.ViewBottomLevelName, setting.ViewTopLevelName, out var geometry, out var geometryError))
            {
                errors.Add($"Móng {ChatElementIdCompat.Value(footing!.Id)}: {geometryError}");
                continue;
            }

            try
            {
                var orchestrator = new FootingSectionOrchestrator { Annotator = new FootingSectionAnnotator() };
                var result = orchestrator.Generate(ctx.Doc, footing!, geometry, setting);
                if (result.ViewportId is null or <= 0)
                {
                    errors.Add($"Móng {ChatElementIdCompat.Value(footing!.Id)}: đã tạo mặt cắt nhưng không đặt được viewport lên sheet.");
                    continue;
                }
                results.Add(result);
                created.Add(new
                {
                    footingId = ChatElementIdCompat.Value(footing!.Id),
                    mark = geometry.Mark,
                    sheetId = result.SheetId,
                    viewId = result.SectionViewId,
                    viewportId = result.ViewportId
                });
            }
            catch (Exception exception)
            {
                errors.Add($"Móng {ChatElementIdCompat.Value(footing!.Id)}: {exception.Message}");
            }
        }

        var warningList = results.SelectMany(result => result.Warnings).Distinct().ToList();

        DrawFootingDrawingTool.ActivateLastSheet(ctx, results.LastOrDefault()?.SheetId);
        var warnings = warningList.Distinct().ToArray();
        var message = $"Đã tạo {results.Count}/{footings.Count} Mặt Cắt Móng bằng cấu hình '{setting.SettingName}'.";
        if (warnings.Length > 0) message += " | Cảnh báo: " + string.Join("; ", warnings);
        if (errors.Count > 0) message += " | Lỗi: " + string.Join("; ", errors);
        return new
        {
            success = errors.Count == 0 && results.Count == footings.Count,
            message,
            completed = results.Count,
            total = footings.Count,
            errors = errors.ToArray(),
            presetName = setting.SettingName,
            created,
            sheetIds = results.Select(result => result.SheetId).Where(id => id.HasValue).ToArray(),
            viewIds = results.Select(result => result.SectionViewId).Where(id => id.HasValue).ToArray()
        };
    }
}
