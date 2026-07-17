using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Chat.ViewModels;
using RevitAPP.Chat.Views;
using RevitAPP.Chat.Services;
using RevitAPP.Commands;

namespace RevitAPP.Chat;

/// <summary>
///     Mở cửa sổ Chat AI (modeless, singleton). Bấm lần hai chỉ đưa cửa sổ đang mở lên trước,
///     không tạo trùng. License gate nằm ở bước execute tool (phase sau), không chặn ở đây.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ChatCommand : ExternalCommand
{
    private static ChatWindow? _window;
    private static Autodesk.Revit.UI.ExternalEvent? _externalEvent;

    public override void Execute()
    {
        if (!LicenseCommandGate.Ensure("Chat AI")) return;
        // Host có thể chưa Start nếu load qua Add-in Manager (OnStartup không chạy) → đảm bảo khởi tạo.
        ChatHost.Start();

        // ExternalEvent phải tạo trong API context (đang ở Execute) → tạo 1 lần rồi bind cho bridge.
        _externalEvent ??= Autodesk.Revit.UI.ExternalEvent.Create(ChatHost.Bridge);
        ChatHost.Bridge.Bind(_externalEvent);
        // The MCP command assembly is an optional capability. Chat must still open on a
        // clean RevitAPP installation so native, ribbon, memory, and Excel tools remain usable.
        try
        {
            NativeMcpCommandHost.Initialize(RevitContext.UiApplication);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Optional Revit MCP commands were not loaded: {ex}");
        }
        ChatSessionContext.ProjectKey = RevitContext.UiApplication.ActiveUIDocument?.Document.Title ?? string.Empty;

        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var viewModel = ChatHost.GetService<ChatViewModel>();
        _window = new ChatWindow(viewModel);
        _window.Closed += (_, _) => _window = null;

        new WindowInteropHelper(_window) { Owner = RevitContext.UiApplication.MainWindowHandle };
        _window.Show();
    }
}
