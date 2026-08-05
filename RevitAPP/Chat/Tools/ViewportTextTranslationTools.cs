using System.Globalization;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

/// <summary>Reads viewport annotations and the exact title fields an MCP client should translate.</summary>
public sealed class GetViewportTextNotesTool : IChatTool
{
    public string Name => "get_viewport_text_notes";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;

    public ToolSchema Schema => new(
        Name,
        "Đọc TextNote, tiêu đề hiển thị của Viewport và Sheet Name để dịch chuyên ngành xây dựng. " +
        "Ưu tiên Title on Sheet khi có giá trị; chỉ dùng View Name khi Title on Sheet rỗng. Không thay đổi Sheet Number.",
        new JsonSchemaBuilder()
            .IntegerArray("viewportIds", "ElementId các Viewport; bỏ trống để dùng Viewport đang chọn.")
            .Bool("allSheets", "True để đọc mọi Viewport đặt trên tất cả Sheet trong project.")
            .Integer("textOffset", "Vị trí bắt đầu khi phân trang TextNote.")
            .Integer("maxItems", "Số TextNote tối đa trả về, 1-1000.")
            .Integer("titleOffset", "Vị trí bắt đầu khi phân trang tiêu đề Viewport.")
            .Integer("maxTitles", "Số tiêu đề Viewport tối đa trả về, 1-200.")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var textOffset = Math.Max(input.Value<int?>("textOffset") ?? 0, 0);
        var maxItems = Math.Clamp(input.Value<int?>("maxItems") ?? 200, 1, 1000);
        var titleOffset = Math.Max(input.Value<int?>("titleOffset") ?? 0, 0);
        var maxTitles = Math.Clamp(input.Value<int?>("maxTitles") ?? 100, 1, 200);
        var allSheets = input.Value<bool?>("allSheets") ?? false;
        var viewports = allSheets
            ? new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(Viewport))
                .WhereElementIsNotElementType()
                .Cast<Viewport>()
                .ToList()
            : (input["viewportIds"] is JArray array
                    ? array.Values<long>().Distinct().Select(ChatElementIdCompat.Create)
                    : ctx.UiDoc.Selection.GetElementIds())
                .Select(ctx.Doc.GetElement)
                .OfType<Viewport>()
                .GroupBy(viewport => ChatElementIdCompat.Value(viewport.Id))
                .Select(group => group.First())
                .Take(50)
                .ToList();
        if (viewports.Count == 0)
            throw new ArgumentException(
                "Không tìm thấy Viewport. Hãy chọn Viewport trên Sheet hoặc truyền viewportIds.");

        var notes = new Dictionary<long, object>();
        var titles = new Dictionary<string, object>(StringComparer.Ordinal);
        var sheets = new Dictionary<long, object>();
        foreach (var viewport in viewports.OrderBy(value => ChatElementIdCompat.Value(value.Id)))
        {
            if (ctx.Doc.GetElement(viewport.ViewId) is not View view) continue;

            var titleOnSheet = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.AsString();
            var useViewName = string.IsNullOrWhiteSpace(titleOnSheet);
            var titleKey = $"{ChatElementIdCompat.Value(view.Id)}:{(useViewName ? "view_name" : "title_on_sheet")}";
            titles.TryAdd(titleKey, new
            {
                viewportId = ChatElementIdCompat.Value(viewport.Id),
                viewId = ChatElementIdCompat.Value(view.Id),
                targetField = useViewName ? "view_name" : "title_on_sheet",
                originalText = useViewName ? view.Name : titleOnSheet,
                viewName = view.Name
            });

            if (ctx.Doc.GetElement(viewport.SheetId) is ViewSheet sheet)
            {
                var sheetId = ChatElementIdCompat.Value(sheet.Id);
                sheets[sheetId] = new
                {
                    sheetId,
                    sheetNumber = sheet.SheetNumber,
                    originalText = sheet.Name
                };
            }

            foreach (var note in new FilteredElementCollector(ctx.Doc, viewport.ViewId)
                         .OfClass(typeof(TextNote)).Cast<TextNote>()
                         .Where(note => !string.IsNullOrWhiteSpace(note.Text)))
            {
                var noteId = ChatElementIdCompat.Value(note.Id);
                notes.TryAdd(noteId, new
                {
                    textNoteId = noteId,
                    originalText = note.Text,
                    viewportId = ChatElementIdCompat.Value(viewport.Id),
                    viewId = ChatElementIdCompat.Value(view.Id),
                    viewName = view.Name
                });
            }
        }

        return new
        {
            success = true,
            totalCount = notes.Count,
            textOffset,
            returnedCount = Math.Min(Math.Max(notes.Count - textOffset, 0), maxItems),
            truncated = notes.Count > textOffset + maxItems,
            textNotes = notes.OrderBy(pair => pair.Key).Select(pair => pair.Value)
                .Skip(textOffset).Take(maxItems).ToList(),
            totalTitleCount = titles.Count,
            titleOffset,
            returnedTitleCount = Math.Min(Math.Max(titles.Count - titleOffset, 0), maxTitles),
            viewportTitles = titles.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value).Skip(titleOffset).Take(maxTitles).ToList(),
            sheetNames = sheets.Values.ToList(),
            translationGuidance =
                "Dịch như chuyên gia bản vẽ kết cấu/xây dựng. Giữ nguyên mã cấu kiện, tên trục, kích thước, " +
                "ký hiệu, viết tắt và xuống dòng. Với viewport, dùng nguyên targetField được trả về: ưu tiên " +
                "Title on Sheet; chỉ dịch View Name khi Title on Sheet rỗng. Dịch Sheet Name nhưng không đổi Sheet Number."
        };
    }
}

/// <summary>Applies translated viewport annotations with optimistic concurrency checks.</summary>
public sealed class ApplyTextNoteTranslationsTool : IChatTool, IConfirmableChatTool
{
    public string Name => "apply_text_note_translations";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;

    public ToolSchema Schema => new(
        Name,
        "Ghi bản dịch chuyên ngành vào TextNote, Title on Sheet/View Name và Sheet Name. " +
        "Mỗi mục phải gửi lại originalText để tránh ghi đè dữ liệu vừa thay đổi. Không bao giờ đổi Sheet Number.",
        new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["translations"] = TranslationArray("textNoteId"),
                ["viewportTitleTranslations"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["viewportId"] = IntegerProperty("ElementId Viewport"),
                            ["viewId"] = IntegerProperty("ElementId View"),
                            ["targetField"] = new JObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JArray("title_on_sheet", "view_name")
                            },
                            ["originalText"] = TextProperty("Nội dung gốc"),
                            ["translatedText"] = TextProperty("Bản dịch chuyên ngành")
                        },
                        ["required"] = new JArray("viewportId", "viewId", "targetField", "originalText", "translatedText")
                    }
                },
                ["sheetNameTranslations"] = TranslationArray("sheetId"),
                ["appendToOriginal"] = new JObject { ["type"] = "boolean", ["default"] = true },
                ["separator"] = new JObject { ["type"] = "string", ["default"] = " / " },
                ["caseMode"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("preserve", "upper", "lower"),
                    ["default"] = "preserve"
                }
            }
        });

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var textUpdates = input["translations"] as JArray ?? new JArray();
        var titleUpdates = input["viewportTitleTranslations"] as JArray ?? new JArray();
        var sheetUpdates = input["sheetNameTranslations"] as JArray ?? new JArray();
        if (textUpdates.Count + titleUpdates.Count + sheetUpdates.Count == 0)
            throw new ArgumentException("Phải có ít nhất một bản dịch cần áp dụng.");
        if (textUpdates.Count + titleUpdates.Count + sheetUpdates.Count > 500)
            throw new ArgumentException("Mỗi lần chỉ áp dụng tối đa 500 bản dịch.");

        var append = input.Value<bool?>("appendToOriginal") ?? true;
        var separator = input.Value<string>("separator") ?? " / ";
        var caseMode = input.Value<string>("caseMode") ?? "preserve";
        var updated = new List<object>();
        var skipped = new List<object>();

        foreach (var token in textUpdates.OfType<JObject>())
        {
            var id = token.Value<long>("textNoteId");
            if (ctx.Doc.GetElement(ChatElementIdCompat.Create(id)) is not TextNote note)
            {
                skipped.Add(new { kind = "text_note", id, reason = "Không tìm thấy TextNote." });
                continue;
            }
            ApplyValue(ctx.Doc, "text_note", id, note.Text, value => note.Text = value, token, append, separator, caseMode,
                updated, skipped);
        }

        foreach (var token in titleUpdates.OfType<JObject>())
        {
            var viewportId = token.Value<long>("viewportId");
            var viewId = token.Value<long>("viewId");
            if (ctx.Doc.GetElement(ChatElementIdCompat.Create(viewportId)) is not Viewport viewport ||
                ChatElementIdCompat.Value(viewport.ViewId) != viewId ||
                ctx.Doc.GetElement(viewport.ViewId) is not View view)
            {
                skipped.Add(new { kind = "viewport_title", id = viewportId, reason = "Viewport/View không hợp lệ." });
                continue;
            }

            var parameter = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
            var titleOnSheet = parameter?.AsString();
            var actualField = string.IsNullOrWhiteSpace(titleOnSheet) ? "view_name" : "title_on_sheet";
            var requestedField = token.Value<string>("targetField") ?? string.Empty;
            if (!string.Equals(actualField, requestedField, StringComparison.Ordinal))
            {
                skipped.Add(new
                {
                    kind = "viewport_title", id = viewportId,
                    reason = $"Trường ưu tiên đã đổi từ {requestedField} sang {actualField}."
                });
                continue;
            }

            var current = actualField == "view_name" ? view.Name : titleOnSheet!;
            ApplyValue(ctx.Doc, "viewport_title", viewportId, current,
                value =>
                {
                    if (actualField == "view_name") view.Name = value;
                    else if (parameter?.Set(value) != true) throw new InvalidOperationException("Không ghi được Title on Sheet.");
                }, token, append, separator, caseMode, updated, skipped);
        }

        foreach (var token in sheetUpdates.OfType<JObject>())
        {
            var id = token.Value<long>("sheetId");
            if (ctx.Doc.GetElement(ChatElementIdCompat.Create(id)) is not ViewSheet sheet)
            {
                skipped.Add(new { kind = "sheet_name", id, reason = "Không tìm thấy Sheet." });
                continue;
            }
            ApplyValue(ctx.Doc, "sheet_name", id, sheet.Name, value => sheet.Name = value, token, append, separator, caseMode,
                updated, skipped);
        }

        return new
        {
            success = true,
            updatedCount = updated.Count,
            skippedCount = skipped.Count,
            updated,
            skipped,
            sheetNumberChanged = false
        };
    }

    private static void ApplyValue(Document document, string kind, long id, string current, Action<string> setter, JObject input,
        bool append, string separator, string caseMode, ICollection<object> updated, ICollection<object> skipped)
    {
        var original = input.Value<string>("originalText") ?? string.Empty;
        var translated = input.Value<string>("translatedText")?.Trim() ?? string.Empty;
        if (!string.Equals(current, original, StringComparison.Ordinal))
        {
            skipped.Add(new { kind, id, reason = "Nội dung đã thay đổi từ lúc đọc; không ghi đè.", currentText = current });
            return;
        }
        if (translated.Length == 0)
        {
            skipped.Add(new { kind, id, reason = "Bản dịch rỗng." });
            return;
        }

        translated = caseMode switch
        {
            "upper" => translated.ToUpper(CultureInfo.CurrentCulture),
            "lower" => translated.ToLower(CultureInfo.CurrentCulture),
            "preserve" => translated,
            _ => throw new ArgumentException("caseMode phải là preserve, upper hoặc lower.")
        };
        var value = append ? current + separator + translated : translated;
        using var subTransaction = new SubTransaction(document);
        try
        {
            subTransaction.Start();
            setter(value);
            if (subTransaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Sub-transaction không thể commit.");
            updated.Add(new { kind, id, value });
        }
        catch (Exception exception)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
                subTransaction.RollBack();

            skipped.Add(new { kind, id, reason = $"Không thể ghi bản dịch: {exception.Message}" });
        }
    }

    private static JObject TranslationArray(string idName) => new()
    {
        ["type"] = "array",
        ["items"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                [idName] = IntegerProperty("ElementId"),
                ["originalText"] = TextProperty("Nội dung gốc"),
                ["translatedText"] = TextProperty("Bản dịch chuyên ngành")
            },
            ["required"] = new JArray(idName, "originalText", "translatedText")
        }
    };

    private static JObject IntegerProperty(string description) =>
        new() { ["type"] = "integer", ["description"] = description };

    private static JObject TextProperty(string description) =>
        new() { ["type"] = "string", ["description"] = description };
}
