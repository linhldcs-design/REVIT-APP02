using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RevitAPP.ViewModels;

/// <summary>Một dòng sheet trong danh sách chọn căn chỉnh.</summary>
public sealed partial class SheetItemViewModel : ObservableObject
{
    public SheetItemViewModel(ElementId sheetId, string sheetNumber, string sheetName)
    {
        SheetId = sheetId;
        SheetNumber = sheetNumber;
        SheetName = sheetName;
    }

    public ElementId SheetId { get; }
    public string SheetNumber { get; }
    public string SheetName { get; }

    public string Display => $"{SheetNumber} — {SheetName}";

    [ObservableProperty] private bool _isSelected;

    /// <summary>True nếu sheet này đang là sheet mẫu (đích căn chỉnh).</summary>
    [ObservableProperty] private bool _isMaster;
}
