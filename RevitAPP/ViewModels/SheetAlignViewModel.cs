using System.Collections.ObjectModel;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RevitAPP.ViewModels;

/// <summary>Tuỳ chọn 1 lưới trục trong combo.</summary>
public sealed class GridOption
{
    public GridOption(ElementId gridId, string name)
    {
        GridId = gridId;
        Name = name;
    }

    public ElementId GridId { get; }
    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>
///     ViewModel dialog căn chỉnh viewport theo lưới trục. User tick các sheet, chọn 1 sheet mẫu,
///     chọn cặp trục neo (trục A + trục B). Validation bật nút OK khi đủ điều kiện.
/// </summary>
public sealed partial class SheetAlignViewModel : ObservableObject
{
    public SheetAlignViewModel(
        IReadOnlyList<SheetItemViewModel> sheets,
        IReadOnlyList<GridOption> grids)
    {
        Sheets = new ObservableCollection<SheetItemViewModel>(sheets);
        Grids = grids;

        foreach (var sheet in Sheets)
        {
            sheet.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SheetItemViewModel.IsSelected))
                {
                    OkCommand.NotifyCanExecuteChanged();
                }
            };
        }

        SelectedMaster = Sheets.FirstOrDefault();
        if (SelectedMaster != null)
        {
            SelectedMaster.IsMaster = true;
        }

        GridA = grids.FirstOrDefault();
        GridB = grids.Count > 1 ? grids[1] : grids.FirstOrDefault();
    }

    public ObservableCollection<SheetItemViewModel> Sheets { get; }
    public IReadOnlyList<GridOption> Grids { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private SheetItemViewModel? _selectedMaster;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private GridOption? _gridA;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private GridOption? _gridB;

    /// <summary>Phát khi user bấm OK (true) hoặc Huỷ (false) để View đóng dialog.</summary>
    public event EventHandler<bool>? CloseRequested;

    public IReadOnlyList<SheetItemViewModel> SelectedSheets =>
        Sheets.Where(sheet => sheet.IsSelected).ToList();

    private bool CanOk()
    {
        return SelectedSheets.Count >= 2
               && SelectedMaster != null
               && SelectedMaster.IsSelected
               && GridA != null
               && GridB != null
               && GridA.GridId != GridB.GridId;
    }

    [RelayCommand]
    private void SetMaster(SheetItemViewModel? sheet)
    {
        if (sheet == null)
        {
            return;
        }

        sheet.IsSelected = true;

        foreach (var item in Sheets)
        {
            item.IsMaster = ReferenceEquals(item, sheet);
        }

        SelectedMaster = sheet;
    }

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
