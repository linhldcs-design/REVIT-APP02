namespace FootingDrawing.Addin.ViewModels;

/// <summary>
///     Một lựa chọn trong ComboBox UI — tách phần hiển thị (Name) khỏi định danh Revit (Id).
///     Id có thể là tên (view template) hoặc ElementId.Value tuỳ nguồn.
/// </summary>
public sealed record ComboOption(string Name, string? Id = null)
{
    public override string ToString() => Name;
}

/// <summary>Một sheet có sẵn: số hiệu + tên.</summary>
public sealed record SheetOption(string Number, string Name)
{
    public string Display => $"{Number} — {Name}";
    public override string ToString() => Display;
}
