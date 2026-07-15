using Autodesk.Revit.UI;
using Serilog;

namespace RevitAPP.Services.PointCloud;

/// <summary>
///     Cầu nối gọi Revit API từ panel modeless. UI đặt <see cref="PendingAction" /> rồi
///     <c>Raise()</c> ExternalEvent; Revit gọi <see cref="Execute" /> trong API context an toàn.
/// </summary>
public sealed class PointCloudExternalEventHandler : IExternalEventHandler
{
    /// <summary>Hành động sẽ chạy lần Raise kế tiếp. Đặt mới sẽ ghi đè (giữ thao tác cuối).</summary>
    public Action<UIApplication>? PendingAction { get; set; }

    /// <summary>ExternalEvent sở hữu handler này — gán sau khi <c>ExternalEvent.Create</c> để có thể re-raise.</summary>
    public ExternalEvent? Event { get; set; }

    public void Execute(UIApplication app)
    {
        var action = PendingAction;
        PendingAction = null;
        if (action == null) return;

        try
        {
            action(app);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "PointCloud external event thất bại");
        }

        // Nếu UI đặt action mới trong lúc Execute đang chạy → re-raise để không mất giá trị slider cuối.
        if (PendingAction != null) Event?.Raise();
    }

    public string GetName() => "PointCloud Display Event";
}
