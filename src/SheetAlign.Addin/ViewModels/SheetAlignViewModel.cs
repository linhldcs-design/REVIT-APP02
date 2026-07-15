using System.Collections.ObjectModel;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SheetAlign.Addin.ViewModels;

/// <summary>Chế độ chọn điểm neo.</summary>
public enum AnchorMode
{
    /// <summary>Giao 2 lưới trục (mặt bằng).</summary>
    GridGrid,

    /// <summary>Giao 1 trục × 1 Level (mặt cắt / elevation).</summary>
    GridLevel
}

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

/// <summary>Tuỳ chọn 1 Level trong combo.</summary>
public sealed class LevelOption
{
    public LevelOption(ElementId levelId, string name)
    {
        LevelId = levelId;
        Name = name;
    }

    public ElementId LevelId { get; }
    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>
///     ViewModel dialog căn chỉnh viewport theo lưới trục. User tick các sheet, chọn 1 sheet mẫu,
///     chọn cặp trục neo. Validation bật nút OK khi đủ điều kiện.
/// </summary>
public sealed partial class SheetAlignViewModel : ObservableObject
{
    public SheetAlignViewModel(
        IReadOnlyList<SheetItemViewModel> sheets,
        IReadOnlyList<GridOption> grids,
        IReadOnlyList<LevelOption> levels)
    {
        Sheets = new ObservableCollection<SheetItemViewModel>(sheets);
        Grids = grids;
        Levels = levels;

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
        SelectedLevel = levels.FirstOrDefault();
    }

    public ObservableCollection<SheetItemViewModel> Sheets { get; }
    public IReadOnlyList<GridOption> Grids { get; }
    public IReadOnlyList<LevelOption> Levels { get; }

    public AnchorMode Mode => UseGridLevel ? AnchorMode.GridLevel : AnchorMode.GridGrid;

    /// <summary>True = neo Trục × Level (mặt cắt); False = neo Trục × Trục (mặt bằng).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridGrid))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private bool _useGridLevel;

    /// <summary>Tiện cho binding hiển thị combo trục B (chỉ ở chế độ Trục×Trục).</summary>
    public bool IsGridGrid => !UseGridLevel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private SheetItemViewModel? _selectedMaster;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private GridOption? _gridA;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private GridOption? _gridB;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private LevelOption? _selectedLevel;

    /// <summary>Phát khi user bấm OK (true) hoặc Huỷ (false) để View đóng dialog.</summary>
    public event EventHandler<bool>? CloseRequested;

    public IReadOnlyList<SheetItemViewModel> SelectedSheets =>
        Sheets.Where(sheet => sheet.IsSelected).ToList();

    private bool CanOk()
    {
        if (SelectedSheets.Count < 2 || SelectedMaster == null || !SelectedMaster.IsSelected || GridA == null)
        {
            return false;
        }

        return UseGridLevel
            ? SelectedLevel != null
            : GridB != null && GridA.GridId != GridB.GridId;
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

    [RelayCommand]
    private void SetGridGrid() => UseGridLevel = false;

    [RelayCommand]
    private void SetGridLevel() => UseGridLevel = true;

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
