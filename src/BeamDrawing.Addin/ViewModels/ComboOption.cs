namespace BeamDrawing.Addin.ViewModels;

/// <summary>
///     Một lựa chọn trong ComboBox UI — tách phần hiển thị (Name) khỏi định danh Revit (Id).
///     Id có thể là tên (view template, section type) hoặc ElementId.Value tuỳ nguồn.
/// </summary>
public sealed record ComboOption(string Name, string? Id = null)
{
    public override string ToString() => Name;
}
