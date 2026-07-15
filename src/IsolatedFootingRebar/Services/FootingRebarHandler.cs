using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services.Rebar;

namespace IsolatedFootingRebar.Services;

/// <summary>
///     Cầu nối ExternalEvent: chạy thao tác Revit API trên main thread khi dialog modeless yêu cầu.
///     Hỗ trợ 2 request: tạo thép (CreateRebar) và pick line hướng (PickDirectionLine).
/// </summary>
public sealed class FootingRebarHandler : IExternalEventHandler
{
    public FootingRequest Request { get; set; } = FootingRequest.CreateRebar;

    /// <summary>Móng đã chọn (lúc bấm Ribbon).</summary>
    public Element? Foundation { get; set; }

    /// <summary>Cấu hình do ViewModel dựng trước khi raise.</summary>
    public FootingRebarModel? Model { get; set; }

    public Action<RebarCreationResult>? OnCompleted { get; set; }
    public Action<Point3?>? OnDirectionPicked { get; set; }

    public void Execute(UIApplication app)
    {
        var uiDoc = app.ActiveUIDocument;
        if (uiDoc is null)
        {
            OnCompleted?.Invoke(new RebarCreationResult(0, 0, 0, ["Không có tài liệu Revit đang mở."]));
            return;
        }

        if (Request == FootingRequest.PickDirectionLine)
        {
            OnDirectionPicked?.Invoke(new DirectionLinePicker().PickDirection(uiDoc));
            return;
        }

        if (Foundation is null || Model is null)
        {
            OnCompleted?.Invoke(new RebarCreationResult(0, 0, 0, ["Chưa chọn móng hoặc chưa có cấu hình."]));
            return;
        }

        var result = new FootingRebarOrchestrator().Create(uiDoc.Document, Foundation, Model);
        OnCompleted?.Invoke(result);
    }

    public string GetName() => "IsolatedFootingRebar - Tạo thép móng";
}

public enum FootingRequest
{
    CreateRebar,
    PickDirectionLine
}
