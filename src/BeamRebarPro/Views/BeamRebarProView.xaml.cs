using BeamRebarPro.ViewModels;
using System.Windows;

namespace BeamRebarPro.Views;

public sealed partial class BeamRebarProView
{
    public BeamRebarProView(BeamRebarProViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.RequestClose += Close;
        viewModel.FitPreviewRequested += FitPreview;

        // Đẩy thẳng bản mô tả vào khung vẽ thay vì để ràng buộc dữ liệu tự lo: bản mô tả so sánh bằng
        // nội dung nên ràng buộc có thể bỏ qua bản mới, khiến khung vẽ chỉ cập nhật lúc chuyển thẻ.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            viewModel.RequestClose -= Close;
            viewModel.FitPreviewRequested -= FitPreview;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    /// <summary>
    /// Canh lại khung nhìn của cả hai bản xem trước về vừa khít thép.
    /// Thẻ nào chưa từng được mở thì nội dung chưa dựng, nên phải kiểm tra trước khi gọi.
    /// </summary>
    private void FitPreview()
    {
        Preview2D?.Fit();
        Preview3D?.Fit();
    }

    /// <summary>Đưa bản mô tả mới nhất vào cả hai khung vẽ ngay khi nó được dựng xong.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BeamRebarProViewModel.PreviewPlan)) return;
        if (DataContext is not BeamRebarProViewModel vm) return;

        var plan = vm.PreviewPlan;
        if (plan is null) return;

        if (Preview2D is not null)
        {
            Preview2D.Plan = plan;
            Preview2D.InvalidateVisual();
        }

        if (Preview3D is not null) Preview3D.Plan = plan;
    }

    /// <summary>
    /// Nạp lại bản xem trước khi người dùng chuyển thẻ. WPF chỉ dựng nội dung thẻ lúc thẻ được mở lần
    /// đầu, nên bản dựng trước đó chưa tới được khung hiển thị vừa sinh ra.
    /// </summary>
    private void PreviewTab_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        if (DataContext is not BeamRebarProViewModel vm) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var plan = vm.PreviewPlan;
            if (plan is null) return;
            if (Preview2D is not null) Preview2D.Plan = plan;
            if (Preview3D is not null) Preview3D.Plan = plan;
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
