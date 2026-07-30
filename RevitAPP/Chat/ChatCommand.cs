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

    public override void Execute()
    {
        if (!LicenseCommandGate.Ensure("Chat AI")) return;
        // Host có thể chưa Start nếu load qua Add-in Manager (OnStartup không chạy) → đảm bảo khởi tạo.
        ChatHost.Start();

        // Chat AI và MCP dùng chung một bridge; Chat AI vẫn giữ nguyên giao diện và tool loop.
        ChatHost.BindRevitBridge();
        ChatHost.StartMcpServer();
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
