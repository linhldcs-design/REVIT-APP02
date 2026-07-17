using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nice3point.Revit.Toolkit;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Services;
using RevitAPP.Chat.Tools;
using RevitAPP.Chat.Views;

namespace RevitAPP.Chat.ViewModels;

/// <summary>
///     ViewModel cửa sổ Chat AI. Vòng gọi LLM + tool chạy trên background thread; UI chỉ cập nhật qua
///     Dispatcher để KHÔNG block UI thread (nếu block, Revit không drain ExternalEvent → treo).
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly ChatSettingsStore _settingsStore;
    private readonly ChatToolRegistry _registry;
    private readonly ChatToolEventHandler _bridge;
    private readonly ChatMemoryStore _memory;
    private readonly ChatImageService _images;
    private readonly Dispatcher _dispatcher;
    private readonly List<ChatMessage> _history = new();
    private readonly HashSet<long> _lastCreatedRebarIds = new();
    private string _currentUserText = string.Empty;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _title = "Chat AI";
    [ObservableProperty] private string _input = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasActiveKey;
    [ObservableProperty] private string _activityStatus = string.Empty;

    public ChatViewModel(
        ChatSettingsStore settingsStore,
        ChatToolRegistry registry,
        ChatToolEventHandler bridge,
        ChatMemoryStore memory,
        ChatImageService images)
    {
        _settingsStore = settingsStore;
        _registry = registry;
        _bridge = bridge;
        _memory = memory;
        _images = images;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Title = $"Chat AI · {_registry.Schemas.Count} tools";
        RefreshActiveKey();
    }

    public ObservableCollection<ChatBubble> Messages { get; } = new();
    public ObservableCollection<ChatImageAttachment> PendingImages { get; } = new();

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Input?.Trim();
        if ((string.IsNullOrEmpty(text) && PendingImages.Count == 0) || IsBusy) return;

        text = string.IsNullOrEmpty(text) ? "Hãy phân tích ảnh này." : text;

        Input = string.Empty;
        var pendingImages = PendingImages.ToList();
        AddBubble(new ChatBubble(ChatBubble.User,
            text!, Images: pendingImages.Select(image => image.Preview).ToList()));
        if (pendingImages.Count == 0 && TryHandleMemoryCommand(text!)) return;

        var settings = _settingsStore.Load();
        if (!settings.HasKeyFor(settings.ActiveProvider))
        {
            AddBubble(new ChatBubble(ChatBubble.Assistant,
                $"Chưa cấu hình API key cho {settings.ActiveProvider}. Nhấn 'Cấu hình…' để nhập.", IsError: true));
            return;
        }

        var imageBlocks = pendingImages.Select(image =>
            ContentBlock.FromImage(image.MimeType, image.Base64Data, image.FileName));
        _history.Add(ChatMessage.FromUser(text!, imageBlocks));
        PendingImages.Clear();
        _currentUserText = text!;

        IsBusy = true;
        ActivityStatus = $"Đang chờ {settings.ActiveProvider}…";
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            if (IsDeleteLastDrawnRebarIntent(text!) && _lastCreatedRebarIds.Count > 0)
            {
                var deleteInput = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["elementIds"] = new JArray(_lastCreatedRebarIds),
                        ["action"] = "Delete"
                    }
                };
                var deleteResult = await Task.Run(
                    () => ExecuteTool("operate_element", deleteInput, ct), ct).ConfigureAwait(false);
                var deleted = JObject.Parse(deleteResult).Value<bool?>("success") == true;
                var deleteReply = deleted
                    ? $"Đã xóa {_lastCreatedRebarIds.Count} phần tử thép vừa vẽ."
                    : $"Không xóa được thép vừa vẽ: {JObject.Parse(deleteResult).Value<string>("message") ?? deleteResult}";
                if (deleted) _lastCreatedRebarIds.Clear();
                _history.Add(ChatMessage.FromAssistantText(deleteReply));
                AddBubble(new ChatBubble(ChatBubble.Assistant, deleteReply, IsError: !deleted));
                return;
            }

            var client = LlmClientFactory.Create(settings);
            var requestHistory = _history.ToList();
            var memoryContext = _memory.BuildContext(ChatSessionContext.ProjectKey, text!);
            if (!string.IsNullOrEmpty(memoryContext))
                requestHistory.Insert(Math.Max(0, requestHistory.Count - 1), ChatMessage.FromAssistantText(memoryContext));
            var reply = await Task.Run(() => client.SendAsync(requestHistory, _registry.Schemas, ExecuteTool, ct), ct);

            _history.Add(ChatMessage.FromAssistantText(reply));
            AddBubble(new ChatBubble(ChatBubble.Assistant, reply));
            _memory.Add(new ChatMemoryEntry
            {
                Project = IsPersistentPreference(text!) ? string.Empty : ChatSessionContext.ProjectKey,
                UserText = text!,
                AssistantText = reply,
                Success = true,
                Pinned = IsPersistentPreference(text!),
                Kind = IsPersistentPreference(text!) ? "preference" : "conversation"
            });
        }
        catch (OperationCanceledException)
        {
            AddBubble(new ChatBubble(ChatBubble.Assistant, "Đã hủy.", IsError: true));
        }
        catch (LlmClientException ex)
        {
            AddBubble(new ChatBubble(ChatBubble.Assistant, ex.Message, IsError: true));
        }
        catch (Exception ex)
        {
            AddBubble(new ChatBubble(ChatBubble.Assistant, $"Lỗi: {ex.Message}", IsError: true));
        }
        finally
        {
            IsBusy = false;
            ActivityStatus = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void AttachImage()
    {
        foreach (var file in _images.PickFiles()) AddImageFile(file);
    }

    [RelayCommand]
    private void PasteImage()
    {
        try
        {
            if (Clipboard.ContainsImage()) AddImage(_images.FromClipboard());
            else if (Clipboard.ContainsText()) Input += Clipboard.GetText();
            else AddBubble(new ChatBubble(ChatBubble.Assistant, "Clipboard không có ảnh hoặc văn bản.", IsError: true));
        }
        catch (Exception ex)
        {
            AddBubble(new ChatBubble(ChatBubble.Assistant, ex.Message, IsError: true));
        }
    }

    [RelayCommand]
    private void DropImages(string[] files)
    {
        foreach (var file in files) AddImageFile(file);
    }

    [RelayCommand]
    private void RemoveImage(ChatImageAttachment image) => PendingImages.Remove(image);

    private void AddImageFile(string file)
    {
        try { AddImage(_images.FromFile(file)); }
        catch (Exception ex) { AddBubble(new ChatBubble(ChatBubble.Assistant, ex.Message, IsError: true)); }
    }

    private void AddImage(ChatImageAttachment image)
    {
        if (PendingImages.Count >= ChatImageService.MaxImages)
        {
            AddBubble(new ChatBubble(ChatBubble.Assistant,
                $"Mỗi tin nhắn tối đa {ChatImageService.MaxImages} ảnh.", IsError: true));
            return;
        }
        PendingImages.Add(image);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var dialog = new ChatSettingsWindow(new ChatSettingsViewModel(_settingsStore));
        new WindowInteropHelper(dialog) { Owner = RevitContext.UiApplication.MainWindowHandle };
        if (dialog.ShowDialog() == true) RefreshActiveKey();
    }

    /// <summary>ToolExecutor: chạy trên background thread, marshal execute sang Revit UI thread qua bridge.</summary>
    private Task<string> ExecuteTool(string name, JObject input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        RunOnUi(() => ActivityStatus = $"Đang chạy {name}…");
        string result;
        var registeredTool = _registry.Get(name);
        if (registeredTool is IBackgroundChatTool backgroundTool)
        {
            try { result = JsonConvert.SerializeObject(backgroundTool.Execute(input, null!)); }
            catch (Exception ex) { result = JsonConvert.SerializeObject(new { success = false, message = ex.Message }); }
        }
        else if (registeredTool is RevitMcpProxyTool proxy)
        {
            if (proxy.McpMethod == "__focus_chat_ai")
            {
                result = JsonConvert.SerializeObject(new { success = true, message = "Cửa sổ Chat AI hiện đã được mở và ở phía trước." });
            }
            else
            {
                if (proxy.RequiresConfirmation && !ConfirmMcpExecution(proxy, input))
                    result = JsonConvert.SerializeObject(new { success = false, cancelled = true, message = "Người dùng đã hủy thao tác MCP." });
                else
                    result = NativeMcpCommandHost.Execute(proxy.McpMethod, input);
            }
        }
        else
        {
            result = IsBatchDrawTool(name)
                ? ExecuteDrawToolInBatches(name, input, ct)
                : _bridge.ExecuteToolOnRevitThread(name, input);
        }
        timer.Stop();
        RememberCreatedRebarIds(result);
        RememberToolExecution(name, input, result);
        RunOnUi(() => ActivityStatus = $"{name} xong ({timer.Elapsed.TotalSeconds:0.0}s) · đang chờ AI…");
        return Task.FromResult(result);
    }

    private void RememberCreatedRebarIds(string resultJson)
    {
        try
        {
            var root = JToken.Parse(resultJson);
            foreach (var array in root.SelectTokens("$..createdElementIds").OfType<JArray>())
            foreach (var id in array.Values<long>())
                _lastCreatedRebarIds.Add(id);
        }
        catch { /* a tool may return plain text or non-object JSON */ }
    }

    private void RememberToolExecution(string name, JObject input, string result)
    {
        var success = false;
        try { success = JToken.Parse(result).Value<bool?>("success") == true; }
        catch { /* retained as an unsuccessful diagnostic memory */ }
        _memory.Add(new ChatMemoryEntry
        {
            Project = ChatSessionContext.ProjectKey,
            UserText = _currentUserText,
            ToolName = name,
            ArgumentsJson = input.ToString(Formatting.None),
            ResultJson = result,
            Success = success,
            Kind = "tool"
        });
    }

    private bool TryHandleMemoryCommand(string text)
    {
        var normalized = RemoveDiacritics(text).ToUpperInvariant().Trim();
        if (normalized == "XEM TRI NHO")
        {
            var entries = _memory.Recent(ChatSessionContext.ProjectKey);
            var groups = entries
                .GroupBy(item => string.IsNullOrWhiteSpace(item.UserText) ? "(tool)" : item.UserText)
                .Select(group => new
                {
                    Text = group.Key,
                    Time = group.Max(item => item.CreatedAt),
                    Pinned = group.Any(item => item.Pinned),
                    Tools = group.Select(item => item.ToolName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray()
                })
                .OrderByDescending(group => group.Pinned)
                .ThenByDescending(group => group.Time)
                .Take(20)
                .ToList();
            var reply = groups.Count == 0
                ? "Chưa có trí nhớ nào cho dự án này."
                : string.Join("\n", groups.Select((group, index) =>
                    $"{index + 1}. [{group.Time:dd/MM HH:mm}] {(group.Pinned ? "📌 " : string.Empty)}" +
                    $"{ClipMemory(group.Text, 90)}" +
                    (group.Tools.Length == 0 ? string.Empty : $" · {string.Join(" → ", group.Tools)}")));
            AddBubble(new ChatBubble(ChatBubble.Assistant, reply));
            return true;
        }

        if (normalized.StartsWith("QUEN "))
        {
            var query = text.Substring(text.IndexOf(' ') + 1).Trim();
            var count = _memory.Forget(ChatSessionContext.ProjectKey, query);
            AddBubble(new ChatBubble(ChatBubble.Assistant, $"Đã quên {count} ký ức khớp với '{query}'."));
            return true;
        }

        if (normalized.StartsWith("GHIM "))
        {
            var query = text.Substring(text.IndexOf(' ') + 1).Trim();
            var count = _memory.Pin(ChatSessionContext.ProjectKey, query);
            AddBubble(new ChatBubble(ChatBubble.Assistant, $"Đã ghim {count} ký ức khớp với '{query}'."));
            return true;
        }

        if (normalized == "XOA TOAN BO TRI NHO")
        {
            var confirmed = MessageBox.Show("Xóa vĩnh viễn toàn bộ trí nhớ Chat AI trên máy này?",
                "Xác nhận xóa trí nhớ", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
            var count = confirmed ? _memory.Clear() : 0;
            AddBubble(new ChatBubble(ChatBubble.Assistant,
                confirmed ? $"Đã xóa {count} ký ức." : "Đã hủy xóa trí nhớ."));
            return true;
        }

        return false;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(ch =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) !=
            System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);
    }

    private static string ClipMemory(string value, int max) => string.IsNullOrWhiteSpace(value)
        ? "(tool)" : value.Length <= max ? value : value[..max] + "…";

    private static bool IsPersistentPreference(string text)
    {
        var value = RemoveDiacritics(text).ToLowerInvariant();
        return value.Contains("lan sau") || value.Contains("luon luon") ||
               value.Contains("hay nho") || value.Contains("ghi nho rang");
    }

    private static bool IsDeleteLastDrawnRebarIntent(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("xóa") && value.Contains("thép") &&
               (value.Contains("vừa vẽ") || value.Contains("mới vẽ") || value.Contains("vừa tạo"));
    }

    private bool ConfirmMcpExecution(RevitMcpProxyTool proxy, JObject input)
    {
        var details = input.ToString(Formatting.Indented);
        if (details.Length > 1800) details = details[..1800] + "\n…";
        var warning = proxy.IsDangerous
            ? "CẢNH BÁO: thao tác này có thể xóa dữ liệu hoặc chạy mã C# tùy ý.\n\n"
            : "Thao tác này sẽ thay đổi mô hình Revit.\n\n";
        return _dispatcher.Invoke(() => MessageBox.Show(
                   warning + $"Tool: {proxy.Name}\n\n{details}\n\nBạn có chắc muốn thực thi?",
                   "Xác nhận Revit MCP", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                   MessageBoxResult.No)) == MessageBoxResult.Yes;
    }

    private string ExecuteDrawToolInBatches(string name, JObject input, CancellationToken ct)
    {
        var (idKey, category) = name switch
        {
            "draw_column_rebar" => ("columnIds", BuiltInCategory.OST_StructuralColumns),
            "draw_beam_rebar" => ("beamIds", BuiltInCategory.OST_StructuralFraming),
            "draw_wall_rebar" => ("wallIds", BuiltInCategory.OST_Walls),
            "draw_footing_rebar" => ("footingIds", BuiltInCategory.OST_StructuralFoundation),
            _ => (string.Empty, BuiltInCategory.INVALID)
        };

        var ids = input[idKey] is JArray supplied && supplied.Count > 0
            ? supplied.Values<long>().ToList()
            : GetSelectedIds(category);
        if (ids.Count == 0)
            return JsonConvert.SerializeObject(new { success = false, message = $"Không có phần tử phù hợp đang chọn cho {name}." });

        var results = new JArray();
        var succeeded = 0;
        for (var index = 0; index < ids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var current = (JObject)input.DeepClone();
            current[idKey] = new JArray(ids[index]);
            var step = index + 1;
            RunOnUi(() => ActivityStatus = $"Đang chạy {name}: {step}/{ids.Count}…");

            var raw = _bridge.ExecuteToolOnRevitThread(name, current);
            var result = JObject.Parse(raw);
            results.Add(result);
            if (result.Value<bool?>("success") == true) succeeded++;
        }

        return JsonConvert.SerializeObject(new
        {
            success = succeeded == ids.Count,
            completed = succeeded,
            total = ids.Count,
            results
        });
    }

    private List<long> GetSelectedIds(BuiltInCategory category)
    {
        var raw = _bridge.ExecuteToolOnRevitThread("get_selected_elements", new JObject());
        var root = JObject.Parse(raw);
        var expectedCategory = (long)category;
        return (root["elements"] as JArray ?? new JArray())
            .Where(element => element.Value<long?>("categoryId") == expectedCategory)
            .Select(element => element.Value<long>("id"))
            .ToList();
    }

    private static bool IsBatchDrawTool(string name) => name is
        "draw_column_rebar" or "draw_beam_rebar" or "draw_wall_rebar" or "draw_footing_rebar";

    private void AddBubble(ChatBubble bubble) => RunOnUi(() => Messages.Add(bubble));

    private void RefreshActiveKey()
    {
        var settings = _settingsStore.Load();
        HasActiveKey = settings.HasKeyFor(settings.ActiveProvider);
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }
}
