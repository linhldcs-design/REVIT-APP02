using CommunityToolkit.Mvvm.ComponentModel;
using RevitAPP.Core.Services;

namespace RevitAPP.ViewModels;

/// <summary>One reviewable CAD axis: the user decides whether it becomes a Grid.</summary>
public sealed partial class CadGridAxisViewModel : ObservableObject
{
    public CadGridAxisViewModel(CadGridPreviewAxis axis)
    {
        Axis = axis;
        _name = axis.SuggestedName;
        // Skew axes are off by default: they are usually reference lines rather than
        // grids, and creating them unasked is harder to undo than ticking a box.
        _isSelected = axis.Kind == CadGridAxisKind.Family;
    }

    public CadGridPreviewAxis Axis { get; }

    public int Id => Axis.Id;

    public string KindLabel => Axis.Kind == CadGridAxisKind.Family ? "Trục chính" : "Trục xéo";

    public bool IsSkew => Axis.Kind == CadGridAxisKind.Skew;

    public string AngleLabel => $"{Axis.AngleDegrees:0.#}°";

    public string LengthLabel => $"{Math.Round(Axis.LengthMm):0} mm";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _name;
}
