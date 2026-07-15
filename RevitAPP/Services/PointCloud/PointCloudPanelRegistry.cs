using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitAPP.ViewModels;
using RevitAPP.Views;
using Serilog;

namespace RevitAPP.Services.PointCloud;

/// <summary>
///     Đăng ký + truy cập dockable pane Point Cloud. Tạo View/ViewModel/ExternalEvent một lần
///     (không có DI container trong project → khởi tạo thủ công theo pattern hiện có).
/// </summary>
public static class PointCloudPanelRegistry
{
    /// <summary>GUID cố định nhận diện dockable pane.</summary>
    public static readonly DockablePaneId PaneId = new(new Guid("8B5C9A21-4E3F-4D7A-9C12-6F2A1B8E7D34"));

    private static PointCloudPanelView? _view;
    private static DateTime _lastLodCheck = DateTime.MinValue;

    /// <summary>Đăng ký pane trong OnStartup. Gọi đúng một lần. Tự nuốt lỗi để không phá ribbon.</summary>
    public static void Register(UIControlledApplication application)
    {
        try
        {
            var service = new PointCloudDisplayService();
            var handler = new PointCloudExternalEventHandler();
            var externalEvent = ExternalEvent.Create(handler);
            handler.Event = externalEvent; // cho phép handler re-raise (tránh mất giá trị slider cuối)
            var renderController = new PointCloudRenderController(new PointCloudReader(), new PointCloudSettingsStore());

            _view = new PointCloudPanelView();
            application.RegisterDockablePane(PaneId, "Hiển thị Point Cloud", _view);

            // ViewModel cần UIApplication (ControlledApplication chưa có) → gán lần view đầu kích hoạt,
            // rồi tự gỡ handler để không giữ tham chiếu suốt session.
            void OnViewActivated(object? sender, ViewActivatedEventArgs args)
            {
                if (sender is not UIApplication uiApp) return;
                _view!.DataContext = new PointCloudPanelViewModel(service, handler, externalEvent, renderController, uiApp);
                application.ViewActivated -= OnViewActivated;
            }

            application.ViewActivated += OnViewActivated;

            // LOD động: định kỳ kiểm tra zoom đổi → re-read điểm theo zoom (throttle 600ms để không spam).
            application.Idling += (sender, _) =>
            {
                if (sender is not UIApplication uiApp) return;
                if ((DateTime.UtcNow - _lastLodCheck).TotalMilliseconds < 600) return;
                _lastLodCheck = DateTime.UtcNow;
                renderController.MaybeRefreshLod(uiApp);
            };
        }
        catch (Exception exception)
        {
            // Không để lỗi đăng ký pane làm hỏng OnStartup (mất toàn bộ ribbon button).
            Log.Error(exception, "Đăng ký dockable pane Point Cloud thất bại");
        }
    }
}
