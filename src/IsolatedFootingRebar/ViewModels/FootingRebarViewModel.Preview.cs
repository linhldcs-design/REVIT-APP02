using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services;

namespace IsolatedFootingRebar.ViewModels;

public sealed partial class FootingRebarViewModel
{
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private FootingGeometry? _previewGeometry;
    private bool _previewInitialized;

    [ObservableProperty] private FootingRebarPreviewPlan? _previewPlan;
    [ObservableProperty] private string? _previewValidationMessage;

    public event Action? FitPreviewRequested;

    private void InitializePreview()
    {
        if (_previewInitialized) return;
        _previewInitialized = true;
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreview();
        };
        PropertyChanged += OnPreviewInputChanged;
        foreach (var direction in Directions())
            direction.PropertyChanged += OnDirectionPreviewInputChanged;
    }

    private IEnumerable<DirectionViewModel> Directions()
    {
        yield return BottomX; yield return BottomY;
        yield return TopX; yield return TopY;
        yield return MidX; yield return MidY;
    }

    private void OnPreviewInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreviewPlan) or nameof(PreviewValidationMessage) or nameof(StatusMessage)) return;
        SchedulePreview();
    }

    private void OnDirectionPreviewInputChanged(object? sender, PropertyChangedEventArgs e) => SchedulePreview();

    private void SchedulePreview()
    {
        if (_previewGeometry is null) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void CapturePreviewGeometry()
    {
        if (_foundation is null) return;
        var overrideX = _dirXOverride is { } d ? new Autodesk.Revit.DB.XYZ(d.X, d.Y, d.Z) : null;
        if (!new FootingGeometryReader().TryRead(_foundation, overrideX, out var geometry, out var error))
        {
            PreviewValidationMessage = error;
            return;
        }

        _previewGeometry = geometry;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_previewGeometry is null) return;
        try
        {
            var plan = FootingRebarPreviewFactory.Build(_previewGeometry, BuildModel());
            PreviewValidationMessage = null;
            PreviewPlan = null;
            PreviewPlan = plan;
        }
        catch (Exception ex)
        {
            PreviewValidationMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void FitPreview() => FitPreviewRequested?.Invoke();
}
