using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Mcp;
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
    private readonly object _executionGate = new();
    private readonly object _pendingGate = new();

    private ExternalEvent? _event;
    private PendingToolRequest? _pendingRequest;

    public ChatToolEventHandler(ChatToolRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Gán ExternalEvent (phải tạo trong API context — xem ChatCommand).</summary>
    public void Bind(ExternalEvent externalEvent) => _event = externalEvent;

    /// <summary>Chạy 1 tool trên Revit thread và chờ kết quả (JSON string). Gọi từ background thread.</summary>
    public string ExecuteToolOnRevitThread(
        string name,
        JObject input,
        int timeoutMs = 120_000,
        bool requireUserConfirmation = false)
    {
        lock (_executionGate)
        {
            if (_event is null)
                return Error("Chat/MCP chưa sẵn sàng (ExternalEvent chưa khởi tạo).");

            var effectiveTimeoutMs = name == "draw_beam_longitudinal_drawing"
                ? Math.Max(timeoutMs, 30 * 60 * 1000)
                : timeoutMs;
            using var completion = new McpRequestCompletion();
            var request = new PendingToolRequest(
                name, (JObject)input.DeepClone(), requireUserConfirmation, completion);
            lock (_pendingGate)
            {
                if (_pendingRequest is not null)
                    return Error("Chat/MCP đang xử lý một request khác.");
                _pendingRequest = request;
            }

            var raiseResult = _event.Raise();
            if (raiseResult is not (ExternalEventRequest.Accepted or ExternalEventRequest.Pending))
            {
                var error = Error($"Revit từ chối ExternalEvent cho tool '{name}' ({raiseResult}).");
                completion.TryCancel(error);
                ClearPending(request);
                return error;
            }

            if (!completion.Wait(effectiveTimeoutMs))
            {
                var timeout = Error(
                    $"Tool '{name}' quá thời gian chờ ({effectiveTimeoutMs / 1000}s).");
                if (completion.TryCancel(timeout))
                {
                    ClearPending(request);
                    return timeout;
                }

                // Revit already started this request. Wait for its own correlated result so the
                // next caller can never overwrite or duplicate an in-flight model change.
                completion.Wait();
            }

            ClearPending(request);
            return completion.Result;
        }
    }

    public void Execute(UIApplication app)
    {
        PendingToolRequest? request;
        lock (_pendingGate)
        {
            request = _pendingRequest;
            if (request is null || !request.Completion.TryStart()) return;
        }

        string result;
        try
        {
            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
            {
                result = Error("Không có tài liệu Revit đang mở.");
            }
            else if (!_registry.TryGet(request.Name, out var tool))
            {
                result = Error($"Tool không tồn tại: {request.Name}");
            }
            else if (tool.RequiresLicense && LicenseService.EnsureValid() is var license && !license.Ok)
            {
                result = Error(license.Message);
            }
            else if (request.RequireUserConfirmation && !ConfirmMcpExecution(tool, request.Input))
            {
                result = JsonConvert.SerializeObject(new
                {
                    success = false,
                    cancelled = true,
                    message = "Người dùng đã hủy MCP tool trong Revit."
                });
            }
            else
            {
                var doc = uiDoc.Document;
                var ctx = new ChatToolContext(doc, uiDoc);
                AddSelectedElementIdsWhenMissing(request.Name, request.Input, ctx);
                var rebarBefore = IsRebarDrawTool(request.Name) ? CollectRebarIds(doc) : null;

                object output;
                if (tool.RequiresTransaction)
                {
                    using var transaction = new Transaction(doc, "Chat AI tool");
                    transaction.Start();
                    output = tool.Execute(request.Input, ctx);
                    transaction.Commit();
                }
                else
                {
                    output = tool.Execute(request.Input, ctx);
                }

                var resultObject = JObject.FromObject(output);
                if (rebarBefore is not null)
                {
                    var createdIds = CollectRebarIds(doc).Where(id => !rebarBefore.Contains(id)).ToArray();
                    resultObject["createdElementIds"] = new JArray(createdIds);
                }
                result = resultObject.ToString(Formatting.None);
            }
        }
        catch (Exception ex)
        {
            result = Error(ex.Message);
        }

        request.Completion.Complete(result);
        ClearPending(request);
    }

    public string GetName() => "ChatToolEventHandler";

    private static bool ConfirmMcpExecution(IChatTool tool, JObject inputObject)
    {
        var input = inputObject.ToString(Formatting.Indented);
        if (input.Length > 1600) input = input[..1600] + "\n…";
        var dangerous = tool is IConfirmableChatTool { IsDangerous: true };
        var dialog = new TaskDialog("RevitAPP - MCP")
        {
            MainInstruction = dangerous
                ? $"MCP yêu cầu chạy thao tác nguy hiểm: {tool.Name}"
                : $"MCP yêu cầu thay đổi mô hình: {tool.Name}",
            MainContent = input,
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
            MainIcon = dangerous
                ? TaskDialogIcon.TaskDialogIconWarning
                : TaskDialogIcon.TaskDialogIconInformation
        };
        return dialog.Show() == TaskDialogResult.Yes;
    }

    private void ClearPending(PendingToolRequest request)
    {
        lock (_pendingGate)
            if (ReferenceEquals(_pendingRequest, request))
                _pendingRequest = null;
    }

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
            "draw_beam_longitudinal_drawing" => ("beamIds", BuiltInCategory.OST_StructuralFraming),
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

    private sealed record PendingToolRequest(
        string Name,
        JObject Input,
        bool RequireUserConfirmation,
        McpRequestCompletion Completion);
}
