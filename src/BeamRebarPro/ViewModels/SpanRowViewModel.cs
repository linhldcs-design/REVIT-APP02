using BeamRebarPro.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BeamRebarPro.ViewModels;

/// <summary>
///     Một dòng trong bảng nhịp của màn Detail: chiều dài nhịp + chiều dài thép gia cường top/bottom.
///     Mặc định tính TỰ ĐỘNG theo TCVN (top 0.25L mỗi bên gối, bottom 1/8..6/8 L). Bỏ tick "Auto" →
///     cho phép nhập tay (override) chiều dài.
/// </summary>
public sealed partial class SpanRowViewModel : ObservableObject
{
    private readonly SpanInfo _info;

    public SpanRowViewModel(SpanInfo info)
    {
        _info = info;
        _topLengthMm = info.TopExtendEachSideMm;
        _bottomLengthMm = info.BottomLengthMm;
    }

    public int Index => _info.Index;
    public string SpanName => $"Span {_info.Index}";
    public double LengthMm => _info.LengthMm;

    /// <summary>Auto = tính theo TCVN; bỏ tick → nhập tay.</summary>
    [ObservableProperty] private bool _topAuto = true;
    [ObservableProperty] private bool _bottomAuto = true;

    [ObservableProperty] private double _topLengthMm;
    [ObservableProperty] private double _bottomLengthMm;

    partial void OnTopAutoChanged(bool value)
    {
        if (value) TopLengthMm = _info.TopExtendEachSideMm;
    }

    partial void OnBottomAutoChanged(bool value)
    {
        if (value) BottomLengthMm = _info.BottomLengthMm;
    }

    /// <summary>% chiều dài top trên tổng nhịp (để map xuống engine LengthPercent). Top = 2 đoạn 0.25L → ~50%.</summary>
    public double TopLengthPercent => LengthMm <= 0 ? 0 : Math.Min(100, TopLengthMm * 2 / LengthMm * 100);
    public double BottomLengthPercent => LengthMm <= 0 ? 0 : Math.Min(100, BottomLengthMm / LengthMm * 100);
}
