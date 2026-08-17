using RevitAPP.Core.Models;

namespace BeamRebarPro.Services;

/// <summary>
/// Dựng lại bản xem trước mỗi khi cấu hình đổi.
/// Dựng trực tiếp thay vì hẹn giờ: hộp thoại của add-in chạy trong vòng lặp thông điệp của Revit,
/// nơi bộ hẹn giờ dễ gắn nhầm luồng và im lặng không bao giờ chạy. Việc dựng chỉ mất vài mili giây
/// nên làm ngay vẫn mượt khi gõ.
/// </summary>
public sealed class BeamRebarPreviewCoordinator
{
    private readonly Func<BeamRebarGeometryPlan> _build;
    private bool _isRebuilding;

    public BeamRebarPreviewCoordinator(Func<BeamRebarGeometryPlan> build) => _build = build;

    public event Action<BeamRebarGeometryPlan>? PlanChanged;

    /// <summary>Cấu hình đã đổi — dựng lại bản xem trước.</summary>
    public void Invalidate()
    {
        // Việc dựng có thể chạm lại trạng thái và kích hoạt vòng gọi ngược; bỏ qua lần lồng nhau.
        if (_isRebuilding) return;

        _isRebuilding = true;
        try
        {
            PlanChanged?.Invoke(_build());
        }
        catch (Exception ex)
        {
            // Cấu hình dở dang trong lúc gõ có thể chưa dựng được hình. Giữ hộp thoại sống thay vì
            // để lỗi thoát lên và đóng cửa sổ.
            Serilog.Log.Debug(ex, "Preview rebuild skipped");
            PlanChanged?.Invoke(BeamRebarGeometryPlan.Empty);
        }
        finally
        {
            _isRebuilding = false;
        }
    }
}
