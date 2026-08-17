using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Models;

namespace BeamRebarPro.ViewModels;

/// <summary>
/// Phần khung xem trước của hộp thoại: giữ hình học thép dựng từ cấu hình hiện tại và dựng lại mỗi
/// khi người dùng đổi thông số.
/// </summary>
public sealed partial class BeamRebarProViewModel
{
    private BeamRebarGeometryPlan? _previewPlan;

    /// <summary>
    /// Hình học thép dựng từ cấu hình hiện tại, hiển thị ở khung xem trước.
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
        }
    }

    /// <summary>Vị trí lấy mặt cắt ngang trong khung xem trước (mm). NaN = giữa dầm.</summary>
    [ObservableProperty] private double _previewSectionStationMm = double.NaN;

    /// <summary>Khung xem trước canh lại khung nhìn khi người dùng yêu cầu.</summary>
    public event Action? FitPreviewRequested;

    /// <summary>
    /// Tạm ngừng dựng lại trong lúc nhiều thông số được ghi liên tiếp, để một thao tác của người dùng
    /// chỉ dẫn tới một lần dựng thay vì hàng chục lần.
    /// </summary>
    private bool _previewSuspended;

    /// <summary>Dựng lại bản xem trước sau khi thông số thay đổi.</summary>
    private void RefreshPreview()
    {
        if (_previewSuspended) return;
        _preview.Invalidate();
    }

    /// <summary>
    /// Gom một loạt thay đổi thành một lần dựng. Màn hình chi tiết ghi ngược hàng chục thông số mỗi
    /// lần người dùng gõ một ký tự, nên nếu không gom thì mỗi ký tự sẽ dựng lại hàng chục lần.
    /// </summary>
    internal IDisposable SuspendPreview() => new PreviewSuspension(this);

    private sealed class PreviewSuspension : IDisposable
    {
        private readonly BeamRebarProViewModel _owner;

        public PreviewSuspension(BeamRebarProViewModel owner)
        {
            _owner = owner;
            _owner._previewSuspended = true;
        }

        public void Dispose()
        {
            _owner._previewSuspended = false;
            _owner.RefreshPreview();
        }
    }

    [RelayCommand]
    private void FitPreview() => FitPreviewRequested?.Invoke();

    // Mọi thông số ảnh hưởng hình học đều dựng lại bản xem trước.
    partial void OnMainTopCountChanged(int value) => RefreshPreview();
    partial void OnMainTopDiameterMmChanged(int value) => RefreshPreview();
    partial void OnMainBottomCountChanged(int value) => RefreshPreview();
    partial void OnMainBottomDiameterMmChanged(int value) => RefreshPreview();
    partial void OnMainTopBendDownLengthMmChanged(double value) => RefreshPreview();
    partial void OnTopAdditionalEnabledChanged(bool value) => RefreshPreview();
    partial void OnTopAdditionalCountChanged(int value) => RefreshPreview();
    partial void OnTopAdditionalDiameterMmChanged(int value) => RefreshPreview();
    partial void OnTopAdditionalPercentChanged(double value) => RefreshPreview();
    partial void OnTopAdditionalEdgeHookDownLengthMmChanged(double value) => RefreshPreview();
    partial void OnTopAdditionalLayer2EnabledChanged(bool value) => RefreshPreview();
    partial void OnTopAdditionalLayer2CountChanged(int value) => RefreshPreview();
    partial void OnTopAdditionalLayer2DiameterMmChanged(int value) => RefreshPreview();
    partial void OnBottomAdditionalEnabledChanged(bool value) => RefreshPreview();
    partial void OnBottomAdditionalCountChanged(int value) => RefreshPreview();
    partial void OnBottomAdditionalDiameterMmChanged(int value) => RefreshPreview();
    partial void OnBottomAdditionalPercentChanged(double value) => RefreshPreview();
    partial void OnBottomAdditionalLayer2EnabledChanged(bool value) => RefreshPreview();
    partial void OnBottomAdditionalLayer2CountChanged(int value) => RefreshPreview();
    partial void OnBottomAdditionalLayer2DiameterMmChanged(int value) => RefreshPreview();
    partial void OnStirrupDiameterMmChanged(int value) => RefreshPreview();
    partial void OnStirrupSpacingEndMmChanged(double value) => RefreshPreview();
    partial void OnStirrupSpacingMidMmChanged(double value) => RefreshPreview();
    partial void OnStirrupFirstDistanceMmChanged(double value) => RefreshPreview();
    partial void OnCoverMmChanged(double value) => RefreshPreview();
}
