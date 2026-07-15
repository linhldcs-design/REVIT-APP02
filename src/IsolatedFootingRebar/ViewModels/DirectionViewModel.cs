using CommunityToolkit.Mvvm.ComponentModel;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.ViewModels;

/// <summary>
///     State một phương (X hoặc Y) trong tab lưới (Bottom/Top/Mid): khớp ảnh — Diameter, radio
///     Spacing|Number, checkbox Rebar Hook Length + value.
/// </summary>
public sealed partial class DirectionViewModel : ObservableObject
{
    [ObservableProperty] private int _diameterMm = 6;
    [ObservableProperty] private bool _useSpacing = true;
    [ObservableProperty] private double _spacingMm = 150;
    [ObservableProperty] private int _count = 5;
    [ObservableProperty] private bool _hookEnabled = true;
    [ObservableProperty] private double _hookLengthMm = 600;

    /// <summary>Số nghịch đảo của UseSpacing — bind radio "Rebar Number".</summary>
    public bool UseNumber
    {
        get => !UseSpacing;
        set => UseSpacing = !value;
    }

    partial void OnUseSpacingChanged(bool value) => OnPropertyChanged(nameof(UseNumber));

    public LayerBarConfig ToConfig(bool enabled = true) => new()
    {
        Enabled = enabled,
        Diameter = new RebarDiameter(DiameterMm),
        UseSpacing = UseSpacing,
        SpacingMm = SpacingMm,
        Count = Count,
        HookEnabled = HookEnabled,
        HookLengthMm = HookLengthMm
    };

    public void Load(LayerBarConfig c)
    {
        DiameterMm = c.Diameter.Millimeters;
        UseSpacing = c.UseSpacing;
        SpacingMm = c.SpacingMm;
        Count = c.Count;
        HookEnabled = c.HookEnabled;
        HookLengthMm = c.HookLengthMm;
    }
}
