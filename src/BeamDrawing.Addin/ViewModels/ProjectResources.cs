using System.Collections.ObjectModel;

namespace BeamDrawing.Addin.ViewModels;

/// <summary>
///     Nguồn dữ liệu cho các ComboBox trong dialog, nạp từ document hiện hành.
///     Phase 3 dùng bản rỗng (<see cref="Empty"/>) để dựng UI; Phase 4 nạp dữ liệu thật
///     qua ProjectResourceProvider.
/// </summary>
public sealed class ProjectResources
{
    public IReadOnlyList<ComboOption> RebarTagTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> ViewTemplates { get; init; } = [];
    public IReadOnlyList<ComboOption> ViewportTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> SectionTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> Scales { get; init; } = [];
    public IReadOnlyList<ComboOption> SpotElevationTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> DimensionTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> BreakLineFamilies { get; init; } = [];
    public IReadOnlyList<ComboOption> TitleBlocks { get; init; } = [];

    /// <summary>Sheet có sẵn trong project — Name = số hiệu, Id = tên sheet. Cho nút 🔍 Sheet Number.</summary>
    public IReadOnlyList<SheetOption> Sheets { get; init; } = [];

    public static ProjectResources Empty { get; } = new();
}

/// <summary>Một sheet có sẵn: số hiệu + tên.</summary>
public sealed record SheetOption(string Number, string Name)
{
    public string Display => $"{Number} — {Name}";
    public override string ToString() => Display;
}
