using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WallRebar.Models;
using WallRebar.Services.Rebar;

namespace WallRebar.Services;

/// <summary>
///     Cầu nối ExternalEvent: chạy thao tác Revit API trên main thread khi dialog modeless yêu cầu tạo thép.
/// </summary>
public sealed class WallRebarHandler : IExternalEventHandler
{
    /// <summary>Tường đã chọn (lúc bấm Ribbon).</summary>
    public Wall? Wall { get; set; }

    /// <summary>Cấu hình do ViewModel dựng trước khi raise.</summary>
    public WallRebarModel? Model { get; set; }

    public Action<RebarCreationResult>? OnCompleted { get; set; }

    public void Execute(UIApplication app)
    {
        var uiDoc = app.ActiveUIDocument;
        if (uiDoc is null)
        {
            OnCompleted?.Invoke(new RebarCreationResult(0, 0, 0, ["Không có tài liệu Revit đang mở."]));
            return;
        }

        if (Wall is null || Model is null)
        {
            OnCompleted?.Invoke(new RebarCreationResult(0, 0, 0, ["Chưa chọn tường hoặc chưa có cấu hình."]));
            return;
        }

        var result = new WallRebarOrchestrator().Create(uiDoc.Document, Wall, Model);
        OnCompleted?.Invoke(result);
    }

    public string GetName() => "WallRebar - Tạo thép tường";
}
