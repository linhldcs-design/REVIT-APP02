using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Tools;
using RevitAPP.Licensing;

namespace RevitAPP.Chat.Services;

/// <summary>
///     Cầu nối thực thi tool trên Revit UI thread. Vòng chat (background thread) gọi
///     <see cref="ExecuteToolOnRevitThread"/> → raise ExternalEvent → Revit chạy <see cref="Execute"/> trên
///     UI thread (mở Transaction nếu tool cần, gate license nếu tool cần) → trả kết quả JSON.
///     KHÔNG được gọi từ UI thread (sẽ deadlock vì chờ chính thread đang block).
/// </summary>
public sealed class ChatToolEventHandler : IExternalEventHandler
{
    private readonly ChatToolRegistry _registry;
    private readonly ManualResetEvent _done = new(false);

    private ExternalEvent? _event;
    private string _pendingName = string.Empty;
    private JObject _pendingInput = new();
    private string _result = string.Empty;

    public ChatToolEventHandler(ChatToolRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Gán ExternalEvent (phải tạo trong API context — xem ChatCommand).</summary>
    public void Bind(ExternalEvent externalEvent) => _event = externalEvent;

    /// <summary>Chạy 1 tool trên Revit thread và chờ kết quả (JSON string). Gọi từ background thread.</summary>
    public string ExecuteToolOnRevitThread(string name, JObject input, int timeoutMs = 120_000)
    {
        if (_event is null)
            return Error("Chat chưa sẵn sàng (ExternalEvent chưa khởi tạo).");

        _pendingName = name;
        _pendingInput = input;
        _result = Error("Không nhận được kết quả từ Revit.");
        _done.Reset();

        _event.Raise();
        if (!_done.WaitOne(timeoutMs))
            return Error($"Tool '{name}' quá thời gian chờ ({timeoutMs / 1000}s).");

        return _result;
    }

    public void Execute(UIApplication app)
    {
        try
        {
            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
            {
                _result = Error("Không có tài liệu Revit đang mở.");
                return;
            }

            if (!_registry.TryGet(_pendingName, out var tool))
            {
                _result = Error($"Tool không tồn tại: {_pendingName}");
                return;
            }

            if (tool.RequiresLicense)
            {
                var (ok, message) = LicenseService.EnsureValid();
                if (!ok)
                {
                    _result = Error(message);
                    return;
                }
            }

            var doc = uiDoc.Document;
            var ctx = new ChatToolContext(doc, uiDoc);
            AddSelectedElementIdsWhenMissing(_pendingName, _pendingInput, ctx);
            var rebarBefore = IsRebarDrawTool(_pendingName) ? CollectRebarIds(doc) : null;

            object output;
            if (tool.RequiresTransaction)
            {
                using var transaction = new Transaction(doc, "Chat AI tool");
                transaction.Start();
                output = tool.Execute(_pendingInput, ctx);
                transaction.Commit();
            }
            else
            {
                output = tool.Execute(_pendingInput, ctx);
            }

            var result = JObject.FromObject(output);
            if (rebarBefore is not null)
            {
                var createdIds = CollectRebarIds(doc).Where(id => !rebarBefore.Contains(id)).ToArray();
                result["createdElementIds"] = new JArray(createdIds);
            }
            _result = result.ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            _result = Error(ex.Message);
        }
        finally
        {
            _done.Set();
        }
    }

    public string GetName() => "ChatToolEventHandler";

    private static bool IsRebarDrawTool(string name) => name is
        "draw_column_rebar" or "draw_beam_rebar" or "draw_beam_rebar_from_open_excel" or
        "draw_wall_rebar" or "draw_footing_rebar";

    private static HashSet<long> CollectRebarIds(Document document) =>
        new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Rebar)
            .WhereElementIsNotElementType()
            .Select(element => ChatElementIdCompat.Value(element.Id))
            .ToHashSet();

    private static void AddSelectedElementIdsWhenMissing(string toolName, JObject input, ChatToolContext ctx)
    {
        var mapping = toolName switch
        {
            "draw_column_rebar" => ("columnIds", BuiltInCategory.OST_StructuralColumns),
            "draw_beam_rebar" => ("beamIds", BuiltInCategory.OST_StructuralFraming),
            "draw_beam_rebar_from_open_excel" => ("beamIds", BuiltInCategory.OST_StructuralFraming),
            "draw_beam_drawing" => ("beamIds", BuiltInCategory.OST_StructuralFraming),
            "draw_footing_drawing" => ("footingIds", BuiltInCategory.OST_StructuralFoundation),
            "draw_footing_section" => ("footingIds", BuiltInCategory.OST_StructuralFoundation),
            "draw_and_arrange_footing_sheet" => ("footingIds", BuiltInCategory.OST_StructuralFoundation),
            "draw_wall_rebar" => ("wallIds", BuiltInCategory.OST_Walls),
            "draw_footing_rebar" => ("footingIds", BuiltInCategory.OST_StructuralFoundation),
            _ => (string.Empty, BuiltInCategory.INVALID)
        };

        if (string.IsNullOrEmpty(mapping.Item1) || input[mapping.Item1] is JArray existing && existing.Count > 0)
            return;

        var categoryId = ChatElementIdCompat.Create((long)mapping.Item2);
        var ids = new JArray();
        foreach (var id in ctx.UiDoc.Selection.GetElementIds())
        {
            var element = ctx.Doc.GetElement(id);
            if (element?.Category?.Id == categoryId)
                ids.Add(ChatElementIdCompat.Value(id));
        }

        if (ids.Count > 0) input[mapping.Item1] = ids;
    }

    private static string Error(string message) =>
        JsonConvert.SerializeObject(new { success = false, message });
}
