using RevitAPP.Core.Models;

namespace RevitAPP.ViewModels;

/// <summary>Một chấm thép trên section preview (toạ độ pixel trên Canvas).</summary>
public sealed record SectionPreviewDot(double Left, double Top, double Size, bool IsCorner);

/// <summary>Tuỳ chọn kiểu đai cho combo (enum + nhãn tiếng Việt).</summary>
public sealed record StirrupTypeOption(SectionStirrupType Value, string Label)
{
    public override string ToString() => Label;
}
