namespace WallRebar.Models;

/// <summary>
///     Kiểu móc bẻ đầu thanh thép — khớp ComboBox 3 lựa chọn trong dialog "Wall Rebar":
///     <list type="bullet">
///         <item><see cref="Closed"/>: móc kín (bẻ 180°, đoạn móc dài hơn — hình ⊓/⊔).</item>
///         <item><see cref="Half"/>: móc nửa (bẻ 90°, đoạn móc bằng Hook Length).</item>
///         <item><see cref="Straight"/>: thẳng, không móc.</item>
///     </list>
/// </summary>
public enum HookType
{
    Closed,
    Half,
    Straight
}
