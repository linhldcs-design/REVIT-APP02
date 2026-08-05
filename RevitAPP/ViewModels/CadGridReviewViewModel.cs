using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Core.Models.CadGrid;
using RevitAPP.Core.Services;

namespace RevitAPP.ViewModels;

public sealed partial class CadGridReviewViewModel : ObservableObject
{
    /// <summary>Zoom bounds keep the drawing reachable without letting it vanish.</summary>
    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 20.0;

    private const double ZoomStep = 1.25;

    private readonly Func<(CadGridTransferPackage Package, CadGridPreview Preview)?>? _reselect;

    public CadGridReviewViewModel(
        CadGridTransferPackage package,
        CadGridPreview preview,
        Func<(CadGridTransferPackage Package, CadGridPreview Preview)?>? reselect = null)
    {
        Package = package;
        Preview = preview;
        _reselect = reselect;
        Axes = new ObservableCollection<CadGridAxisViewModel>(
            preview.Axes.Select(axis => new CadGridAxisViewModel(axis)));

        foreach (var axis in Axes) axis.PropertyChanged += OnAxisChanged;
    }

    public CadGridTransferPackage Package { get; private set; }

    public CadGridPreview Preview { get; private set; }

    public ObservableCollection<CadGridAxisViewModel> Axes { get; }

    public event EventHandler<bool>? CloseRequested;

    /// <summary>Raised when the drawing must be redrawn — selection or zoom changed.</summary>
    public event EventHandler? RenderRequested;

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private double _panX;

    [ObservableProperty]
    private double _panY;

    public string SourceLabel =>
        $"{Package.SourceDrawing} — AutoCAD {Package.AutoCadVersion}"
        + $" — {Package.CreatedUtc.ToLocalTime():HH:mm dd/MM/yyyy}";

    public string SummaryLabel =>
        $"Chọn {SelectedCount}/{Axes.Count} trục"
        + $"  •  Trục chính: {Axes.Count(axis => !axis.IsSkew)}"
        + $"  •  Trục xéo: {Axes.Count(axis => axis.IsSkew)}"
        + (Preview.SkippedIds.Count > 0
            ? $"  •  Bỏ qua {Preview.SkippedIds.Count} line quá ngắn"
            : string.Empty);

    public int SelectedCount => Axes.Count(axis => axis.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public IReadOnlyList<CadGridPreviewAxis> SelectedAxes =>
        Axes.Where(axis => axis.IsSelected)
            .Select(axis => axis.Axis with { SuggestedName = axis.Name })
            .ToArray();

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(MaximumZoom, Zoom * ZoomStep);

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(MinimumZoom, Zoom / ZoomStep);

    [RelayCommand]
    private void ZoomToFit()
    {
        Zoom = 1.0;
        PanX = 0;
        PanY = 0;
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Picks a fresh selection in AutoCAD and rebuilds the list, so a mis-selection can be
    /// corrected without cancelling out of the whole command.
    /// </summary>
    [RelayCommand]
    private void Reselect()
    {
        if (_reselect is null) return;

        var replacement = _reselect();
        if (replacement is null) return;

        Package = replacement.Value.Package;
        Preview = replacement.Value.Preview;

        foreach (var axis in Axes) axis.PropertyChanged -= OnAxisChanged;
        Axes.Clear();
        foreach (var axis in Preview.Axes)
        {
            var item = new CadGridAxisViewModel(axis);
            item.PropertyChanged += OnAxisChanged;
            Axes.Add(item);
        }

        Zoom = 1.0;
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SummaryLabel));
        OnPropertyChanged(nameof(HasSelection));
        AcceptCommand.NotifyCanExecuteChanged();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    [RelayCommand]
    private void SelectFamiliesOnly()
    {
        foreach (var axis in Axes) axis.IsSelected = !axis.IsSkew;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Accept() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private void SetAll(bool selected)
    {
        foreach (var axis in Axes) axis.IsSelected = selected;
    }

    private void OnAxisChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(CadGridAxisViewModel.IsSelected)
            or nameof(CadGridAxisViewModel.Name))) return;

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SummaryLabel));
        OnPropertyChanged(nameof(HasSelection));
        AcceptCommand.NotifyCanExecuteChanged();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnZoomChanged(double value) =>
        RenderRequested?.Invoke(this, EventArgs.Empty);
}
