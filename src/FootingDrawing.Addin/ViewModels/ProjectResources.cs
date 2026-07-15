namespace FootingDrawing.Addin.ViewModels;

/// <summary>Nguồn dữ liệu cho các ComboBox trong dialog, nạp từ document hiện hành.</summary>
public sealed class ProjectResources
{
    public IReadOnlyList<ComboOption> DimensionTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> RebarTagTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> BendingDetailTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> ParentPlanViews { get; init; } = [];
    public IReadOnlyList<ComboOption> CalloutTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> ViewTemplates { get; init; } = [];
    public IReadOnlyList<ComboOption> ViewportTypes { get; init; } = [];
    public IReadOnlyList<ComboOption> TitleBlocks { get; init; } = [];

    /// <summary>Sheet có sẵn — cho nút 🔍 PickSheet.</summary>
    public IReadOnlyList<SheetOption> Sheets { get; init; } = [];

    public static ProjectResources Empty { get; } = new();
}
