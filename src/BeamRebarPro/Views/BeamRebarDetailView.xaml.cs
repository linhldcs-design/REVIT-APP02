using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BeamRebarPro.ViewModels;

namespace BeamRebarPro.Views;

public sealed partial class BeamRebarDetailView
{
    public BeamRebarDetailView(BeamRebarDetailViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.FitPreviewRequested += FitPreview;

        // Đẩy thẳng bản mô tả vào khung vẽ thay vì để ràng buộc dữ liệu tự lo: bản mô tả so sánh bằng
        // nội dung nên ràng buộc có thể bỏ qua bản mới, khiến khung vẽ chỉ cập nhật lúc chuyển thẻ.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            viewModel.FitPreviewRequested -= FitPreview;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
        Loaded += (_, _) => viewModel.RefreshPreview();
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
        if (e.PropertyName != nameof(BeamRebarDetailViewModel.PreviewPlan)) return;
        if (DataContext is not BeamRebarDetailViewModel vm) return;

        var plan = vm.PreviewPlan;
        if (plan is null) return;

        if (Preview2D is not null)
        {
            Preview2D.Plan = plan;
            Preview2D.InvalidateVisual();
        }

        if (Preview3D is not null && Preview3D.IsVisible) Preview3D.Plan = plan;
    }

    /// <summary>
    /// Nạp lại bản xem trước khi người dùng chuyển thẻ. WPF chỉ dựng nội dung thẻ lúc thẻ được mở lần
    /// đầu, nên bản dựng trước đó chưa tới được khung hiển thị vừa sinh ra.
    /// </summary>
    private void PreviewTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        if (DataContext is not BeamRebarDetailViewModel vm) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var plan = vm.PreviewPlan;
            if (plan is null) return;
            if (Preview2D is not null) Preview2D.Plan = plan;
            if (Preview3D is not null && Preview3D.IsVisible) Preview3D.Plan = plan;
        }), DispatcherPriority.Loaded);
    }

    private void ToggleSection_Click(object sender, RoutedEventArgs e)
    {
        // Bước sau: chuyển giữa hình mặt cắt và mặt đứng.
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BeamRebarDetailViewModel vm)
        {
            vm.ApplyRebar();
            if (vm.ApplyRequested) Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
