using BeamRebarPro.Services;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Models;

namespace BeamRebarPro.ViewModels;

/// <summary>
/// Khung xem trước của màn hình chi tiết: cùng hình học với bản xem trước ở Quick Setting, nhưng dựng
/// từ cấu hình chi tiết đang chỉnh nên phản ánh cả các thiết lập riêng theo từng nhịp.
/// </summary>
public sealed partial class BeamRebarDetailViewModel
{
    private BeamRebarPreviewCoordinator? _preview;
    private BeamRebarGeometryPlan? _previewPlan;

    /// <summary>
    /// Hình học thép dựng từ cấu hình chi tiết hiện tại.
    /// So sánh bằng tham chiếu chứ không bằng giá trị: bản mô tả là record nên hai lần dựng cho kết
    /// quả giống nhau sẽ bị coi là "không đổi" và khung xem trước đứng im dù người dùng vừa sửa số.
    /// </summary>
    public BeamRebarGeometryPlan? PreviewPlan
    {
        get => _previewPlan;
        private set
        {
            if (ReferenceEquals(_previewPlan, value)) return;

            // Đặt rỗng trước rồi mới gán bản mới: bản mô tả thép so sánh bằng giá trị, nên gán thẳng
            // một bản có nội dung tương đương sẽ bị khung vẽ coi là không đổi và bỏ qua.
            _previewPlan = null;
            OnPropertyChanged();

            _previewPlan = value;
            OnPropertyChanged();
            Serilog.Log.Debug("[DETAIL] PreviewPlan da bao UI: paths={Paths}", value?.Paths.Count ?? -1);
        }
    }

    /// <summary>Khung xem trước canh lại khung nhìn khi người dùng yêu cầu.</summary>
    public event Action? FitPreviewRequested;

    [RelayCommand]
    private void FitPreview() => FitPreviewRequested?.Invoke();

    /// <summary>Dựng lại bản xem trước sau khi cấu hình chi tiết thay đổi.</summary>
    public void RefreshPreview()
    {
        Serilog.Log.Debug("[DETAIL] RefreshPreview vao: spanRows={SpanRows}, parentSpans={ParentSpans}",
            SpanRows.Count, _parent.PickedSpans.Count);

        _preview ??= CreateCoordinator();
        _preview.Invalidate();

        Serilog.Log.Debug("[DETAIL] RefreshPreview xong: paths={Paths}, context={Context}",
            PreviewPlan?.Paths.Count ?? -1, PreviewPlan?.Context.Count ?? -1);
    }

    private BeamRebarPreviewCoordinator CreateCoordinator()
    {
        var coordinator = new BeamRebarPreviewCoordinator(BuildPreviewPlan);
        coordinator.PlanChanged += plan => PreviewPlan = plan;
        return coordinator;
    }

    /// <summary>
    /// Dựng bản xem trước từ cấu hình đang chỉnh. Dùng chính bộ dựng cấu hình mà lệnh tạo thép dùng,
    /// nên hình xem trước và thép được tạo luôn đi từ một nguồn.
    /// </summary>
    private BeamRebarGeometryPlan BuildPreviewPlan()
    {
        // Bộ dựng cấu hình có ghi lại trạng thái ô đang sửa; bỏ qua khi màn hình đang nạp lại dữ liệu
        // để không ghi đè giá trị bằng nội dung dở dang.
        if (_isLoadingEditor || _isSyncingParent)
        {
            Serilog.Log.Debug("[DETAIL] BuildPreviewPlan BI CHAN: loading={Loading}, syncing={Syncing}",
                _isLoadingEditor, _isSyncingParent);
            return PreviewPlan ?? BeamRebarGeometryPlan.Empty;
        }

        var spans = PreviewSpans();
        var plan = BeamRebarPreviewService.Build(BuildModel(), spans, _parent.SecondaryBeams);
        Serilog.Log.Debug("[DETAIL] BuildPreviewPlan: spans={Spans}, paths={Paths}",
            spans.Count, plan.Paths.Count);
        return plan;
    }

    /// <summary>
    /// Nhịp dùng để dựng bản xem trước. Bảng nhịp của màn chi tiết là nguồn cập nhật nhất — nó được
    /// nạp lại mỗi khi người dùng chọn thêm gối — nên ưu tiên nó trước cấu hình ở màn ngoài.
    /// </summary>
    private IReadOnlyList<Models.SpanInfo> PreviewSpans() =>
        SpanRows.Count > 0
            ? SpanRows.Select(r => r.Info).ToList()
            : _parent.PickedSpans;
}
