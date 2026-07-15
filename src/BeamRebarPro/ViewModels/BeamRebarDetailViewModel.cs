using Autodesk.Revit.UI;
using BeamRebarPro.Models;
using BeamRebarPro.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeamRebarPro.ViewModels;

/// <summary>
///     Các mục thép trong cột Setting bên trái màn hình Beam Rebar chi tiết.
/// </summary>
public enum RebarSettingTab
{
    MainTopBar,
    MainBotBar,
    AddTopBar,
    AddBotBar,
    Stirrup,
    AntiBulge
}

public sealed record RebarListItem(string Key, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>Một đai phụ trong list (UI) — như video: "Đai Lồng Kín [Start:X - End:Y]".</summary>
public sealed record AdditionalStirrupItem(int DiameterMm, AdditionalStirrupType Type, int StartBar, int EndBar)
{
    public override string ToString() => Type == AdditionalStirrupType.Closed
        ? $"Đai Lồng Kín [ Start :{StartBar} - End :{EndBar} ] D{DiameterMm}"
        : $"Đai Móc C [ Vị trí :{StartBar} ] D{DiameterMm}";
}

/// <summary>1 chấm thanh chủ trong panel Section (toạ độ canvas + nhãn số nếu là hàng có nhãn).</summary>
public sealed record SectionBarDot(double X, double Y, double Diameter, string? Label);

/// <summary>1 hình đai phụ vẽ trong panel Section. Closed = khung kín (Rectangle Width/Height tại X,Y).
///     CHook = thân thẳng (PathData) + 2 vòng móc (2 Ellipse: HookTopX/Y, HookBotX/Y, HookR).</summary>
public sealed record SectionStirrupShape(bool IsClosed, double X, double Y, double Width, double Height,
    string PathData, double HookTopX, double HookTopY, double HookBotX, double HookBotY, double HookR, double HookSize);

public sealed record PreviewLine(double X1, double Y1, double X2, double Y2, string Stroke, double Thickness = 1.0);

public sealed record PreviewRect(double X, double Y, double Width, double Height, string Fill, string Stroke, double Thickness = 1.0);

public sealed record PreviewText(string Text, double X, double Y, string Foreground, double FontSize = 11.0);

/// <summary>
///     ViewModel cho màn "Beam Rebar" (Detail Rebar Forms) — mở từ Quick Setting. Bước 1: khung 6 tab
///     Setting + chọn tab hiện hành. Form chi tiết từng tab sẽ bổ sung ở các bước sau, vẫn dùng lại
///     engine tạo Rebar hiện có (Longitudinal/Stirrup/AntiBulge) — không viết engine mới.
/// </summary>
public sealed partial class BeamRebarDetailViewModel : ObservableObject
{
    private readonly RebarCreationHandler _handler;
    private readonly ExternalEvent _externalEvent;
    private readonly BeamRebarProViewModel _parent;

    private int _mainTopDiameterMmState;
    private bool _mainTopEnabledState = true;
    private int _mainTopNumberState;
    private double _mainTopAnchorLeftMmState;
    private double _mainTopAnchorRightMmState;
    private double _mainTopAnchorXLeftMmState;
    private double _mainTopAnchorXRightMmState;
    private double _mainTopBendDownLengthMmState;
    private int _mainTopStartPointState;
    private int _mainTopEndPointState;
    private string _mainTopPositionInSectionState = "0,1,2";
    private int _mainBottomDiameterMmState;
    private bool _mainBottomEnabledState = true;
    private int _mainBottomNumberState;
    private double _mainBottomAnchorLeftMmState;
    private double _mainBottomAnchorRightMmState;
    private double _mainBottomAnchorXLeftMmState;
    private double _mainBottomAnchorXRightMmState;
    private int _mainBottomStartPointState;
    private int _mainBottomEndPointState;
    private string _mainBottomPositionInSectionState = "0,1,2";

    private AddBarState _topAddLayer1 = AddBarState.DefaultTop(1);
    private AddBarState _topAddLayer2 = AddBarState.DefaultTop(2);
    private readonly List<AddBarState> _topAddItems = [];
    private AddBarState _bottomAddLayer1 = AddBarState.DefaultBottom(1);
    private AddBarState _bottomAddLayer2 = AddBarState.DefaultBottom(2);
    private readonly List<AddBarState> _bottomAddItems = [];
    private bool _topAddItemsMaterialized;
    private bool _bottomAddItemsMaterialized;
    private readonly List<StirrupSpanState> _stirrupSpanItems = [];
    private int _selectedStirrupSpanIndex;
    private int _topAddSelectedLayer = 1;
    private int _bottomAddSelectedLayer = 1;
    private bool _isLoadingEditor;
    private bool _isSyncingParent;
    private bool _isRefreshingRebarList;

    private sealed record AddBarState(
        bool Enabled,
        int Layer,
        int Count,
        int DiameterMm,
        int StartPoint,
        int EndPoint,
        string StartType,
        string EndType,
        double LeftRatio,
        double RightRatio,
        double LeftLengthMm,
        double RightLengthMm,
        double DLeftMm,
        double DRightMm,
        string PositionInSection,
        double AnchorLeftMm,
        double AnchorRightMm)
    {
        public static AddBarState DefaultTop(int layer) => new(false, layer, 2, layer == 2 ? 20 : 16, 0, int.MaxValue,
            "1: Attached to column", "1: Attached to column", 0.25, 0.25, 0, 0, 300, 300, "0,1", 0, 0);

        // Lớp 1: ratio 0.75 (thép gia cường dưới dài). Lớp 2: ratio 0.375 (= 3/8) → GỢI Ý thép từ 1/8 đến 7/8
        // L thông thủy (ngắn, giữa nhịp), KHÔNG chạy hết dầm. UI tự tính Length=3/8 clear, Anchor=1/8 clear.
        public static AddBarState DefaultBottom(int layer) => new(false, layer, 2, layer == 2 ? 20 : 16, 0, int.MaxValue,
            "2: Go through the span", "2: Go through the span", layer == 2 ? 0.375 : 0.75, layer == 2 ? 0.375 : 0.75,
            0, 0, 0, 0, "0,1", 0, 0);
    }

    private sealed record StirrupSpanState(
        int SpanIndex,
        bool Enabled,
        int DiameterMm,
        bool TwoEnds,
        double SpacingEndMm,
        double SpacingMidMm,
        double EndZoneStartMm,
        double EndZoneEndMm,
        double FirstDistanceMm);

    /// <summary>
    ///     Tái dùng handler + ExternalEvent của Quick Setting (đã tạo trong API context lúc command chạy)
    ///     và tham chiếu Quick Setting VM để THÔNG SỐ LIÊN LẠC THẬT — đọc giá trị hiện tại khi mở, ghi
    ///     ngược lại khi thay đổi. KHÔNG tự tạo ExternalEvent (tránh Revit fatal error).
    /// </summary>
    public BeamRebarDetailViewModel(BeamRebarProViewModel parent, RebarCreationHandler handler, ExternalEvent externalEvent)
    {
        _parent = parent;
        _handler = handler;
        _externalEvent = externalEvent;
        _parent.PropertyChanged += (_, e) => ReloadFromParentIfExternal(e.PropertyName);
        LoadFromParent();
        LoadEditorFromSelectedTab();
        RefreshRebarListFromState();
        LoadPickedSpans();
    }

    /// <summary>Nạp giá trị từ Quick Setting vào các field Detail (đồng bộ khi mở).</summary>
    private void LoadFromParent()
    {
        _isLoadingEditor = true;
        _mainTopEnabledState = _parent.MainTopCount > 0;
        _mainTopDiameterMmState = _parent.MainTopDiameterMm;
        _mainTopNumberState = _parent.MainTopCount;
        _mainTopAnchorLeftMmState = _parent.MainAnchorLengthMm;
        _mainTopAnchorRightMmState = _parent.MainAnchorLengthMm;
        _mainTopAnchorXLeftMmState = 0;
        _mainTopAnchorXRightMmState = 0;
        _mainTopBendDownLengthMmState = _parent.MainTopBendDownLengthMm;
        _mainTopStartPointState = 0;
        _mainTopEndPointState = DefaultEndPoint;
        _mainBottomEnabledState = _parent.MainBottomCount > 0;
        _mainBottomDiameterMmState = _parent.MainBottomDiameterMm;
        _mainBottomNumberState = _parent.MainBottomCount;
        _mainBottomAnchorLeftMmState = _parent.MainAnchorLengthMm;
        _mainBottomAnchorRightMmState = _parent.MainAnchorLengthMm;
        _mainBottomAnchorXLeftMmState = 0;
        _mainBottomAnchorXRightMmState = 0;
        _mainBottomStartPointState = 0;
        _mainBottomEndPointState = DefaultEndPoint;

        _topAddLayer1 = AddBarState.DefaultTop(1) with { Enabled = _parent.TopAdditionalEnabled, Count = _parent.TopAdditionalCount, DiameterMm = _parent.TopAdditionalDiameterMm, DLeftMm = _parent.TopAdditionalEdgeHookDownLengthMm, DRightMm = _parent.TopAdditionalEdgeHookDownLengthMm, PositionInSection = "0,1" };
        _topAddLayer2 = AddBarState.DefaultTop(2) with { Enabled = _parent.TopAdditionalLayer2Enabled, Count = _parent.TopAdditionalLayer2Count, DiameterMm = _parent.TopAdditionalLayer2DiameterMm, DLeftMm = _parent.TopAdditionalEdgeHookDownLengthMm, DRightMm = _parent.TopAdditionalEdgeHookDownLengthMm, PositionInSection = "0,1" };
        _topAddItems.Clear();
        _topAddItemsMaterialized = false;
        _bottomAddLayer1 = AddBarState.DefaultBottom(1) with { Enabled = _parent.BottomAdditionalEnabled, Count = _parent.BottomAdditionalCount, DiameterMm = _parent.BottomAdditionalDiameterMm, PositionInSection = "0,1" };
        _bottomAddLayer2 = AddBarState.DefaultBottom(2) with { Enabled = _parent.BottomAdditionalLayer2Enabled, Count = _parent.BottomAdditionalLayer2Count, DiameterMm = _parent.BottomAdditionalLayer2DiameterMm, PositionInSection = "0,1" };
        _bottomAddItems.Clear();
        _bottomAddItemsMaterialized = false;
        _topAddSelectedLayer = !_parent.TopAdditionalEnabled && _parent.TopAdditionalLayer2Enabled ? 2 : 1;
        _bottomAddSelectedLayer = !_parent.BottomAdditionalEnabled && _parent.BottomAdditionalLayer2Enabled ? 2 : 1;

        StirrupDiameterMm = _parent.StirrupDiameterMm;
        StirrupTwoEnds = _parent.StirrupTwoEnds;
        StirrupSpacingEndMm = _parent.StirrupSpacingEndMm;
        StirrupSpacingMidMm = _parent.StirrupSpacingMidMm;
        StirrupEndZoneStartMm = StirrupEndZoneMm;
        StirrupEndZoneEndMm = StirrupEndZoneMm;
        StirrupFirstDistanceMm = _parent.StirrupFirstDistanceMm;
        AntiBulgeDiameterMm = _parent.AntiBulgeDiameterMm;
        AntiBulgeColumnEmbedMm = _parent.AntiBulgeColumnEmbedMm;

        // Nếu dầm có config ĐẦY ĐỦ đã lưu (theo ID) → override các field Detail-only (anchor X/Y,
        // start/end point, position) bằng giá trị đã lưu, để nhớ đúng mọi thông số trong màn Beam Rebar.
        ApplySavedDetailConfig(_parent.SavedConfig);
        EnsureMainBarsFullRun();

        _isLoadingEditor = false;
    }

    private void ApplySavedDetailConfig(QuickSettingModel? m)
    {
        if (m is null) return;

        var mt = m.MainTop;
        _mainTopStartPointState = mt.StartPointIndex;
        _mainTopEndPointState = mt.EndPointIndex == int.MaxValue ? DefaultEndPoint : mt.EndPointIndex;
        _mainTopAnchorLeftMmState = mt.AnchorLeftMm > 0 ? mt.AnchorLeftMm : _mainTopAnchorLeftMmState;
        _mainTopAnchorRightMmState = mt.AnchorRightMm > 0 ? mt.AnchorRightMm : _mainTopAnchorRightMmState;
        _mainTopAnchorXLeftMmState = mt.AnchorXLeftMm > 0 ? mt.AnchorXLeftMm : _mainTopAnchorXLeftMmState;
        _mainTopAnchorXRightMmState = mt.AnchorXRightMm > 0 ? mt.AnchorXRightMm : _mainTopAnchorXRightMmState;
        _mainTopBendDownLengthMmState = mt.TopEndBendDownLengthMm;
        if (!string.IsNullOrWhiteSpace(mt.PositionInSection)) _mainTopPositionInSectionState = mt.PositionInSection;

        var mb = m.MainBottom;
        _mainBottomStartPointState = mb.StartPointIndex;
        _mainBottomEndPointState = mb.EndPointIndex == int.MaxValue ? DefaultEndPoint : mb.EndPointIndex;
        _mainBottomAnchorLeftMmState = mb.AnchorLeftMm > 0 ? mb.AnchorLeftMm : _mainBottomAnchorLeftMmState;
        _mainBottomAnchorRightMmState = mb.AnchorRightMm > 0 ? mb.AnchorRightMm : _mainBottomAnchorRightMmState;
        _mainBottomAnchorXLeftMmState = mb.AnchorXLeftMm > 0 ? mb.AnchorXLeftMm : _mainBottomAnchorXLeftMmState;
        _mainBottomAnchorXRightMmState = mb.AnchorXRightMm > 0 ? mb.AnchorXRightMm : _mainBottomAnchorXRightMmState;
        if (!string.IsNullOrWhiteSpace(mb.PositionInSection)) _mainBottomPositionInSectionState = mb.PositionInSection;
    }

    /// <summary>Ghi giá trị Detail ngược về Quick Setting (đồng bộ khi thay đổi / trước khi tạo thép).</summary>
    private void WriteBackToParent(bool saveEditor = true)
    {
        if (_isLoadingEditor) return;
        if (saveEditor)
            SaveEditorToSelectedTab();

        _isSyncingParent = true;
        _parent.MainTopDiameterMm = _mainTopDiameterMmState;
        _parent.MainBottomDiameterMm = _mainBottomDiameterMmState;
        _parent.MainTopCount = _mainTopNumberState;
        _parent.MainBottomCount = _mainBottomNumberState;
        _parent.MainAnchorLengthMm = _mainTopAnchorLeftMmState;
        _parent.MainTopBendDownLengthMm = _mainTopBendDownLengthMmState;
        _parent.TopAdditionalDiameterMm = _topAddLayer1.DiameterMm;
        _parent.TopAdditionalCount = _topAddLayer1.Count;
        _parent.TopAdditionalEnabled = _topAddLayer1.Enabled;
        _parent.TopAdditionalLayer2DiameterMm = _topAddLayer2.DiameterMm;
        _parent.TopAdditionalLayer2Count = _topAddLayer2.Count;
        _parent.TopAdditionalLayer2Enabled = _topAddLayer2.Enabled;
        _parent.TopAdditionalEdgeHookDownLengthMm = Math.Max(
            Math.Max(_topAddLayer1.DLeftMm, _topAddLayer1.DRightMm),
            Math.Max(_topAddLayer2.DLeftMm, _topAddLayer2.DRightMm));
        _parent.BottomAdditionalDiameterMm = _bottomAddLayer1.DiameterMm;
        _parent.BottomAdditionalCount = _bottomAddLayer1.Count;
        _parent.BottomAdditionalEnabled = _bottomAddLayer1.Enabled;
        _parent.BottomAdditionalLayer2DiameterMm = _bottomAddLayer2.DiameterMm;
        _parent.BottomAdditionalLayer2Count = _bottomAddLayer2.Count;
        _parent.BottomAdditionalLayer2Enabled = _bottomAddLayer2.Enabled;
        _parent.StirrupDiameterMm = StirrupDiameterMm;
        _parent.StirrupTwoEnds = StirrupTwoEnds;
        _parent.StirrupSpacingEndMm = StirrupSpacingEndMm;
        _parent.StirrupSpacingMidMm = StirrupSpacingMidMm;
        _parent.StirrupFirstDistanceMm = StirrupFirstDistanceMm;
        StirrupEndZoneMm = Math.Max(StirrupEndZoneStartMm, StirrupEndZoneEndMm);
        _parent.AntiBulgeDiameterMm = AntiBulgeDiameterMm;
        _parent.AntiBulgeColumnEmbedMm = AntiBulgeColumnEmbedMm;
        _parent.AntiBulgeEnabled = AntiBulgeNumber > 0;
        _isSyncingParent = false;
    }

    private void ReloadFromParentIfExternal(string? propertyName = null)
    {
        if (_isSyncingParent || _isLoadingEditor) return;
        var spansChanged = propertyName == nameof(BeamRebarProViewModel.PickedSpans);
        if (spansChanged)
            SyncSpanRowsFromParent(resetSpanDependentItems: false);

        LoadFromParent();
        if (spansChanged)
            ResetSpanDependentItemsForCurrentSpans();

        LoadEditorFromSelectedTab();
        RefreshRebarListFromState();
        RefreshElevationPreview();
    }

    private void EnsureMainBarsFullRun()
    {
        var endPoint = DefaultEndPoint;
        if (endPoint <= 0) return;

        _mainTopStartPointState = 0;
        _mainTopEndPointState = endPoint;
        _mainBottomStartPointState = 0;
        _mainBottomEndPointState = endPoint;
    }

    /// <summary>Nạp nhịp đã pick lúc bấm Ribbon (nếu có) vào bảng — khỏi pick lại.</summary>
    private void LoadPickedSpans()
    {
        if (_parent.PickedSpans.Count == 0) return;
        SyncSpanRowsFromParent(resetSpanDependentItems: true);
        LoadEditorFromSelectedTab();
        RefreshRebarListFromState();
        StatusMessage = $"Đã có {_parent.PickedSpans.Count} nhịp từ dầm đã chọn. Chiều dài thép gia cường tính tự động (TCVN).";
    }

    private void SyncSpanRowsFromParent(bool resetSpanDependentItems)
    {
        if (_parent.PickedSpans.Count == 0) return;

        SpanRows.Clear();
        foreach (var s in _parent.PickedSpans)
            SpanRows.Add(new SpanRowViewModel(s));

        OnPropertyChanged(nameof(PointOptions));
        EnsureMainBarsFullRun();
        if (resetSpanDependentItems)
            ResetSpanDependentItemsForCurrentSpans();
    }

    private void ResetSpanDependentItemsForCurrentSpans()
    {
        _topAddItems.Clear();
        _bottomAddItems.Clear();
        _stirrupSpanItems.Clear();

        AddTopRangeItemsFromLegacy(_topAddLayer1 with { StartPoint = 0, EndPoint = int.MaxValue });
        AddTopRangeItemsFromLegacy(_topAddLayer2 with { StartPoint = 0, EndPoint = int.MaxValue });
        AddBottomRangeItemsFromLegacy(_bottomAddLayer1 with { StartPoint = 0, EndPoint = int.MaxValue });
        AddBottomRangeItemsFromLegacy(_bottomAddLayer2 with { StartPoint = 0, EndPoint = int.MaxValue });
        _topAddItemsMaterialized = true;
        _bottomAddItemsMaterialized = true;
        EnsureStirrupSpanItems();
    }

    [ObservableProperty] private RebarSettingTab _selectedTab = RebarSettingTab.MainTopBar;

    [ObservableProperty] private string _statusMessage = "Bấm 'Pick Beam' để chọn dầm và lấy thông số nhịp, sau đó cấu hình thép.";

    /// <summary>Bảng nhịp sau khi chọn dầm: chiều dài + chiều dài thép gia cường (auto hoặc nhập tay).</summary>
    public System.Collections.ObjectModel.ObservableCollection<SpanRowViewModel> SpanRows { get; } = [];

    public System.Collections.ObjectModel.ObservableCollection<PreviewLine> PreviewLines { get; } = [];

    public System.Collections.ObjectModel.ObservableCollection<PreviewRect> PreviewRects { get; } = [];

    public System.Collections.ObjectModel.ObservableCollection<PreviewText> PreviewTexts { get; } = [];

    public System.Collections.ObjectModel.ObservableCollection<double> ColumnWidths { get; } = [];

    /// <summary>True khi event tạo thép đã raise → View đóng. View đọc cờ này sau ApplyRebar.</summary>
    public bool ApplyRequested { get; private set; }

    public IReadOnlyList<int> PointOptions => Enumerable.Range(0, Math.Max(2, DefaultEndPoint + 1)).ToList();

    private int DefaultEndPoint => Math.Max(1, SpanRows.Count > 0 ? SpanRows.Count : _parent.PickedSpans.Count);

    public string SelectedTabTitle => SelectedTab switch
    {
        RebarSettingTab.MainTopBar => "Main Top Bar",
        RebarSettingTab.MainBotBar => "Main Bottom Bar",
        RebarSettingTab.AddTopBar => "Additional Top Bar",
        RebarSettingTab.AddBotBar => "Additional Bottom Bar",
        RebarSettingTab.Stirrup => "Stirrup",
        RebarSettingTab.AntiBulge => "Anti-bulge Rebar",
        _ => string.Empty
    };

    public string SelectedDiagramImagePath => SelectedTab switch
    {
        RebarSettingTab.MainTopBar => "/BeamRebarPro;component/Resources/Images/MainTopBarDiagram.png",
        RebarSettingTab.MainBotBar => "/BeamRebarPro;component/Resources/Images/MainBotBarDiagram.png",
        RebarSettingTab.AddTopBar => "/BeamRebarPro;component/Resources/Images/AddTopBarDiagram.png",
        RebarSettingTab.AddBotBar => "/BeamRebarPro;component/Resources/Images/AddBotBarDiagram.png",
        RebarSettingTab.Stirrup => "/BeamRebarPro;component/Resources/Images/StirrupDiagram.png",
        RebarSettingTab.AntiBulge => "/BeamRebarPro;component/Resources/Images/AntiBulgeDiagram.png",
        _ => "/BeamRebarPro;component/Resources/Images/MainTopBarDiagram.png"
    };

    partial void OnSelectedTabChanged(RebarSettingTab value)
    {
        OnPropertyChanged(nameof(SelectedTabTitle));
        OnPropertyChanged(nameof(SelectedDiagramImagePath));
        OnPropertyChanged(nameof(IsMainBarTab));
        OnPropertyChanged(nameof(IsAddBarTab));
        OnPropertyChanged(nameof(IsStirrupTab));
        OnPropertyChanged(nameof(IsAntiBulgeTab));
        OnPropertyChanged(nameof(IsPlaceholderTab));
        OnPropertyChanged(nameof(ShowAddButtons));
        OnPropertyChanged(nameof(ShowAddPreview));
        OnPropertyChanged(nameof(ShowSpanGrid));
        OnPropertyChanged(nameof(ShowDiagramImage));
        OnPropertyChanged(nameof(ShowSectionPanel));
        RefreshSectionPanel();
        NotifyDiagram();
        RefreshElevationPreview();
        RefreshRebarListFromState();
    }

    private void SaveEditorToSelectedTab()
    {
        switch (SelectedTab)
        {
            case RebarSettingTab.MainTopBar:
                _mainTopDiameterMmState = MainDiameterMm;
                _mainTopNumberState = MainNumber;
                _mainTopAnchorLeftMmState = AnchorLeftMm;
                _mainTopAnchorRightMmState = AnchorRightMm;
                _mainTopAnchorXLeftMmState = AnchorXLeftMm;
                _mainTopAnchorXRightMmState = AnchorXRightMm;
                _mainTopBendDownLengthMmState = TopBendDownLengthMm;
                _mainTopStartPointState = MainStartPoint;
                _mainTopEndPointState = MainEndPoint;
                _mainTopPositionInSectionState = MainPositionInSection;
                break;
            case RebarSettingTab.MainBotBar:
                _mainBottomDiameterMmState = MainDiameterMm;
                _mainBottomNumberState = MainNumber;
                _mainBottomAnchorLeftMmState = AnchorLeftMm;
                _mainBottomAnchorRightMmState = AnchorRightMm;
                _mainBottomAnchorXLeftMmState = AnchorXLeftMm;
                _mainBottomAnchorXRightMmState = AnchorXRightMm;
                _mainBottomStartPointState = MainStartPoint;
                _mainBottomEndPointState = MainEndPoint;
                _mainBottomPositionInSectionState = MainPositionInSection;
                break;
            case RebarSettingTab.AddTopBar:
                _topAddSelectedLayer = AddLayer;
                SaveAddState(AdditionalBarSide.TopAtSupport);
                break;
            case RebarSettingTab.AddBotBar:
                _bottomAddSelectedLayer = AddLayer;
                SaveAddState(AdditionalBarSide.BottomAtMidspan);
                break;
            case RebarSettingTab.Stirrup:
                SaveStirrupEditorToSelectedSpan();
                break;
        }
    }

    private void LoadEditorFromSelectedTab()
    {
        _isLoadingEditor = true;
        switch (SelectedTab)
        {
            case RebarSettingTab.MainTopBar:
                MainDiameterMm = _mainTopDiameterMmState;
                MainNumber = _mainTopNumberState;
                AnchorLeftMm = _mainTopAnchorLeftMmState;
                AnchorRightMm = _mainTopAnchorRightMmState;
                AnchorXLeftMm = _mainTopAnchorXLeftMmState;
                AnchorXRightMm = _mainTopAnchorXRightMmState;
                TopBendDownLengthMm = _mainTopBendDownLengthMmState;
                MainStartPoint = _mainTopStartPointState;
                MainEndPoint = _mainTopEndPointState;
                MainPositionInSection = _mainTopPositionInSectionState;
                break;
            case RebarSettingTab.MainBotBar:
                MainDiameterMm = _mainBottomDiameterMmState;
                MainNumber = _mainBottomNumberState;
                AnchorLeftMm = _mainBottomAnchorLeftMmState;
                AnchorRightMm = _mainBottomAnchorRightMmState;
                AnchorXLeftMm = _mainBottomAnchorXLeftMmState;
                AnchorXRightMm = _mainBottomAnchorXRightMmState;
                MainStartPoint = _mainBottomStartPointState;
                MainEndPoint = _mainBottomEndPointState;
                MainPositionInSection = _mainBottomPositionInSectionState;
                break;
            case RebarSettingTab.AddTopBar:
                AddLayer = _topAddSelectedLayer;
                LoadAddState(AdditionalBarSide.TopAtSupport);
                break;
            case RebarSettingTab.AddBotBar:
                AddLayer = _bottomAddSelectedLayer;
                LoadAddState(AdditionalBarSide.BottomAtMidspan);
                break;
            case RebarSettingTab.Stirrup:
                EnsureStirrupSpanItems();
                var spanIndex = TryGetSelectedStirrupSpanIndex(out var selected) ? selected : _selectedStirrupSpanIndex;
                var state = _stirrupSpanItems.FirstOrDefault(s => s.SpanIndex == spanIndex)
                            ?? _stirrupSpanItems.FirstOrDefault();
                if (state is not null)
                    LoadStirrupStateIntoEditor(state);
                break;
        }

        _isLoadingEditor = false;
        NotifyDiagram();
    }

    private void SaveAddState(AdditionalBarSide side)
    {
        var state = new AddBarState(AddNumber > 0, AddLayer, AddNumber, AddDiameterMm,
            AddStartPoint, AddEndPoint, AddStartType, AddEndType,
            AddLeftRatio, AddRightRatio, AddLeftLengthMm, AddRightLengthMm,
            side == AdditionalBarSide.TopAtSupport ? AddDLeftMm : 0,
            side == AdditionalBarSide.TopAtSupport ? AddDRightMm : 0,
            AddPositionInSection,
            AddAnchorLeftMm,
            AddAnchorRightMm);

        if (side == AdditionalBarSide.TopAtSupport)
        {
            if (TryGetSelectedTopAddItemIndex(out var itemIndex))
            {
                _topAddItems[itemIndex] = state with
                {
                    Enabled = true,
                    StartPoint = AddStartPoint,
                    EndPoint = AddEndPoint
                };
                return;
            }

            if (_topAddItemsMaterialized)
                return;

            if (AddLayer == 2) _topAddLayer2 = state;
            else _topAddLayer1 = state;
            return;
        }

        if (TryGetSelectedBottomAddItemIndex(out var bottomItemIndex))
        {
            _bottomAddItems[bottomItemIndex] = state with { Enabled = true };
            return;
        }

        if (_bottomAddItemsMaterialized)
            return;

        if (AddLayer == 2) _bottomAddLayer2 = state;
        else _bottomAddLayer1 = state;
    }

    private void LoadAddState(AdditionalBarSide side)
    {
        var state = side == AdditionalBarSide.TopAtSupport
            ? (AddLayer == 2 ? _topAddLayer2 : _topAddLayer1)
            : (AddLayer == 2 ? _bottomAddLayer2 : _bottomAddLayer1);

        LoadAddStateIntoEditor(state);
    }

    private void LoadAddStateIntoEditor(AddBarState state)
    {
        AddLayer = state.Layer;
        AddNumber = state.Count;
        AddDiameterMm = state.DiameterMm;
        AddStartPoint = state.StartPoint;
        AddEndPoint = state.EndPoint == int.MaxValue ? DefaultEndPoint : state.EndPoint;
        AddStartType = state.StartType;
        AddEndType = state.EndType;
        
        _isLoadingEditor = true;
        AddLeftRatio = state.LeftRatio;
        AddRightRatio = state.RightRatio;

        // GỢI Ý sẵn vào ô: nếu Length chưa nhập (=0) nhưng có ratio → tự tính từ ratio (Round50) để người dùng
        // thấy số mặc định, muốn sửa thì sửa sau. (Trước đây để 0 → ô trống, gây nhầm.)
        var lClear = GetCurrentClearSpanMm();
        AddLeftLengthMm = state.LeftLengthMm > 0 ? state.LeftLengthMm
            : (state.LeftRatio > 0 && lClear > 0 ? Round50(lClear * state.LeftRatio) : 0);
        AddRightLengthMm = state.RightLengthMm > 0 ? state.RightLengthMm
            : (state.RightRatio > 0 && lClear > 0 ? Round50(lClear * state.RightRatio) : 0);
        AddAnchorLeftMm = state.AnchorLeftMm != 0 ? state.AnchorLeftMm
            : (lClear > 0 ? Round50(lClear / 2 - AddLeftLengthMm) : 0);
        AddAnchorRightMm = state.AnchorRightMm != 0 ? state.AnchorRightMm
            : (lClear > 0 ? Round50(lClear / 2 - AddRightLengthMm) : 0);
        _isLoadingEditor = false;

        AddDLeftMm = state.DLeftMm;
        AddDRightMm = state.DRightMm;
        AddPositionInSection = state.PositionInSection;
        AddTopEdgeHookDownLengthMm = Math.Max(state.DLeftMm, state.DRightMm);
        OnPropertyChanged(nameof(AddTotalMm));
    }

    private bool TryGetSelectedTopAddItemIndex(out int index)
    {
        index = -1;
        const string prefix = "add-top-item-";
        var key = SelectedRebarListItem?.Key;
        return key is not null
               && key.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(key[prefix.Length..], out index)
               && index >= 0
               && index < _topAddItems.Count;
    }

    private bool TryGetSelectedBottomAddItemIndex(out int index)
    {
        index = -1;
        const string prefix = "add-bot-item-";
        var key = SelectedRebarListItem?.Key;
        return key is not null
               && key.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(key[prefix.Length..], out index)
               && index >= 0
               && index < _bottomAddItems.Count;
    }

    private void EnsureTopAddItemsFromLegacy()
    {
        if (_topAddItemsMaterialized) return;

        AddTopRangeItemsFromLegacy(_topAddLayer1);
        AddTopRangeItemsFromLegacy(_topAddLayer2);
        _topAddItems.Sort((a, b) =>
        {
            var layerCompare = a.Layer.CompareTo(b.Layer);
            return layerCompare != 0 ? layerCompare : a.StartPoint.CompareTo(b.StartPoint);
        });
        _topAddItemsMaterialized = true;
    }

    private void EnsureBottomAddItemsFromLegacy()
    {
        if (_bottomAddItemsMaterialized) return;

        AddBottomRangeItemsFromLegacy(_bottomAddLayer1);
        AddBottomRangeItemsFromLegacy(_bottomAddLayer2);
        _bottomAddItems.Sort((a, b) =>
        {
            var layerCompare = a.Layer.CompareTo(b.Layer);
            return layerCompare != 0 ? layerCompare : a.StartPoint.CompareTo(b.StartPoint);
        });
        _bottomAddItemsMaterialized = true;
    }

    private void AddTopRangeItemsFromLegacy(AddBarState state)
    {
        if (!state.Enabled || state.Count <= 0) return;

        var maxSupport = Math.Max(0, DefaultEndPoint);
        var first = Math.Clamp(state.StartPoint, 0, maxSupport);
        var last = state.EndPoint == int.MaxValue ? maxSupport : Math.Clamp(state.EndPoint, 0, maxSupport);
        if (last < first) (first, last) = (last, first);

        for (var support = first; support <= last; support++)
        {
            _topAddItems.Add(state with
            {
                Enabled = true,
                StartPoint = support,
                EndPoint = support,
                DLeftMm = support == 0 ? state.DLeftMm : 0,
                DRightMm = support == maxSupport ? state.DRightMm : 0
            });
        }
    }

    private void AddBottomRangeItemsFromLegacy(AddBarState state)
    {
        if (!state.Enabled || state.Count <= 0) return;

        var maxSupport = Math.Max(1, DefaultEndPoint);
        var first = Math.Clamp(state.StartPoint, 0, maxSupport - 1);
        var last = state.EndPoint == int.MaxValue ? maxSupport : Math.Clamp(state.EndPoint, first + 1, maxSupport);
        if (last <= first) last = Math.Min(maxSupport, first + 1);

        for (var span = first; span < last; span++)
        {
            _bottomAddItems.Add(state with
            {
                Enabled = true,
                StartPoint = span,
                EndPoint = span + 1
            });
        }
    }

    private void RefreshRebarListFromState()
    {
        if (_isRefreshingRebarList) return;
        _isRefreshingRebarList = true;

        var selectedKey = SelectedRebarListItem?.Key;
        RebarList.Clear();

        switch (SelectedTab)
        {
            case RebarSettingTab.MainTopBar:
                if (_mainTopEnabledState)
                    RebarList.Add(new RebarListItem("main-top",
                        $"Count-{_mainTopNumberState}-D{_mainTopDiameterMmState}-S-{_mainTopStartPointState}-E-{_mainTopEndPointState}"));
                break;
            case RebarSettingTab.MainBotBar:
                if (_mainBottomEnabledState)
                    RebarList.Add(new RebarListItem("main-bottom",
                        $"Count-{_mainBottomNumberState}-D{_mainBottomDiameterMmState}-S-{_mainBottomStartPointState}-E-{_mainBottomEndPointState}"));
                break;
            case RebarSettingTab.AddTopBar:
                EnsureTopAddItemsFromLegacy();
                if (_topAddItemsMaterialized || _topAddItems.Count > 0)
                {
                    for (var i = 0; i < _topAddItems.Count; i++)
                        AddIfEnabled($"add-top-item-{i}", _topAddItems[i]);
                }
                else
                {
                    AddIfEnabled("add-top-l1", _topAddLayer1);
                    AddIfEnabled("add-top-l2", _topAddLayer2);
                }
                break;
            case RebarSettingTab.AddBotBar:
                EnsureBottomAddItemsFromLegacy();
                if (_bottomAddItemsMaterialized || _bottomAddItems.Count > 0)
                {
                    for (var i = 0; i < _bottomAddItems.Count; i++)
                        AddIfEnabled($"add-bot-item-{i}", _bottomAddItems[i]);
                }
                else
                {
                    AddIfEnabled("add-bot-l1", _bottomAddLayer1);
                    AddIfEnabled("add-bot-l2", _bottomAddLayer2);
                }
                break;
            case RebarSettingTab.Stirrup:
                EnsureStirrupSpanItems();
                foreach (var item in _stirrupSpanItems.Where(i => i.Enabled))
                {
                    var mode = item.TwoEnds ? $"A1 {item.SpacingEndMm:F0} A2 {item.SpacingMidMm:F0}" : $"A1 {item.SpacingEndMm:F0}";
                    RebarList.Add(new RebarListItem($"stirrup-span-{item.SpanIndex}",
                        $"Span {item.SpanIndex}: D{item.DiameterMm} {mode}"));
                }
                break;
            case RebarSettingTab.AntiBulge:
                if (AntiBulgeNumber > 0)
                    RebarList.Add(new RebarListItem("anti-bulge",
                        $"AntiBulge: {AntiBulgeNumber}xD{AntiBulgeDiameterMm} Tie D{AntiBulgeTieDiameterMm}@{AntiBulgeSpacingMm:F0}"));
                break;
        }

        SelectedRebarListItem = RebarList.FirstOrDefault(i => i.Key == selectedKey);
        _isRefreshingRebarList = false;

        void AddIfEnabled(string key, AddBarState state)
        {
            if (!state.Enabled || state.Count <= 0) return;
            var endPointVal = state.EndPoint == int.MaxValue ? DefaultEndPoint : state.EndPoint;
            RebarList.Add(new RebarListItem(key,
                $"L{state.Layer} Count-{state.Count}-D{state.DiameterMm}-S-{state.StartPoint}-E-{endPointVal}"));
        }
    }

    // ── Hiển thị form theo tab ──
    public bool IsMainBarTab => SelectedTab is RebarSettingTab.MainTopBar or RebarSettingTab.MainBotBar;
    public bool IsAddBarTab => SelectedTab is RebarSettingTab.AddTopBar or RebarSettingTab.AddBotBar;
    public bool IsStirrupTab => SelectedTab is RebarSettingTab.Stirrup;
    public bool IsAntiBulgeTab => SelectedTab is RebarSettingTab.AntiBulge;
    public bool IsPlaceholderTab => !IsMainBarTab && !IsAddBarTab && !IsStirrupTab && !IsAntiBulgeTab;

    /// <summary>Số chấm thép hàng TRÊN của sơ đồ minh họa (vẽ động theo tab + Number nhập).</summary>
    public int DiagramTopDots => SelectedTab switch
    {
        RebarSettingTab.MainTopBar => MainNumber,
        RebarSettingTab.AddTopBar => AddNumber,
        _ => 3
    };

    /// <summary>Số chấm thép hàng DƯỚI của sơ đồ minh họa.</summary>
    public int DiagramBottomDots => SelectedTab switch
    {
        RebarSettingTab.MainBotBar => MainNumber,
        RebarSettingTab.AddBotBar => AddNumber,
        _ => 3
    };

    /// <summary>Hàng thép nào đang được làm nổi (highlight) theo tab.</summary>
    public bool DiagramHighlightTop => SelectedTab is RebarSettingTab.MainTopBar or RebarSettingTab.AddTopBar;
    public bool DiagramHighlightBottom => SelectedTab is RebarSettingTab.MainBotBar or RebarSettingTab.AddBotBar;

    /// <summary>Chuỗi rỗng chỉ để ItemsControl vẽ đúng số chấm (count = số thanh). Vẽ động theo Number.</summary>
    public IEnumerable<int> TopDotItems => Enumerable.Range(0, Math.Max(0, Math.Min(DiagramTopDots, 12)));
    public IEnumerable<int> BottomDotItems => Enumerable.Range(0, Math.Max(0, Math.Min(DiagramBottomDots, 12)));

    private void NotifyDiagram()
    {
        OnPropertyChanged(nameof(TopDotItems));
        OnPropertyChanged(nameof(BottomDotItems));
        OnPropertyChanged(nameof(DiagramHighlightTop));
        OnPropertyChanged(nameof(DiagramHighlightBottom));
    }

    partial void OnMainDiameterMmChanged(int value) => SyncEditorToParent();
    partial void OnMainNumberChanged(int value)
    {
        NotifyDiagram();
        SyncEditorToParent();
    }
    partial void OnAnchorLeftMmChanged(double value) => SyncEditorToParent();
    partial void OnAnchorRightMmChanged(double value) => SyncEditorToParent();
    partial void OnAnchorXLeftMmChanged(double value) => SyncEditorToParent();
    partial void OnAnchorXRightMmChanged(double value) => SyncEditorToParent();
    partial void OnTopBendDownLengthMmChanged(double value) => SyncEditorToParent();
    partial void OnMainStartPointChanged(int value) => SyncEditorToParent();
    partial void OnMainEndPointChanged(int value) => SyncEditorToParent();
    partial void OnMainPositionInSectionChanged(string value) => SyncEditorToParent();
    partial void OnAddLayerChanged(int value)
    {
        if (_isLoadingEditor) return;
        if (SelectedTab is RebarSettingTab.AddTopBar or RebarSettingTab.AddBotBar)
        {
            _isLoadingEditor = true;
            LoadAddState(SelectedTab == RebarSettingTab.AddTopBar
                ? AdditionalBarSide.TopAtSupport
                : AdditionalBarSide.BottomAtMidspan);
            _isLoadingEditor = false;
        }
        OnPropertyChanged(nameof(ShowAddTieC));
        EnsureDefaultTieC();
        SyncEditorToParent();
    }
    partial void OnAddDiameterMmChanged(int value) => SyncEditorToParent();
    private bool _isSyncingAddFields;

    private double GetCurrentClearSpanMm()
    {
        var start = Math.Clamp(AddStartPoint, 0, SpanRows.Count - 1);
        var end = Math.Clamp(AddEndPoint, 0, SpanRows.Count - 1);
        if (end < start) (start, end) = (end, start);
        if (SpanRows.Count == 0) return 6000.0;

        double totalLen = 0;
        var inclusiveEnd = SelectedTab == RebarSettingTab.AddBotBar ? Math.Max(start, end - 1) : end;
        for (int i = start; i <= inclusiveEnd; i++)
        {
            if (i < SpanRows.Count)
                totalLen += SpanRows[i].LengthMm;
        }
        return totalLen;
    }

    /// <summary>Làm tròn số gợi ý chiều dài/neo đến BỘI SỐ 50 (mm) cho gọn bản vẽ.</summary>
    private static double Round50(double mm) => Math.Round(mm / 50.0) * 50.0;

    private void RecalculateAddFieldsFromRatios()
    {
        if (_isLoadingEditor || _isSyncingAddFields) return;
        _isSyncingAddFields = true;
        try
        {
            var lClear = GetCurrentClearSpanMm();
            AddLeftLengthMm = Round50(lClear * AddLeftRatio);
            AddAnchorLeftMm = Round50(lClear / 2 - AddLeftLengthMm);

            AddRightLengthMm = Round50(lClear * AddRightRatio);
            AddAnchorRightMm = Round50(lClear / 2 - AddRightLengthMm);

            OnPropertyChanged(nameof(AddTotalMm));
        }
        finally { _isSyncingAddFields = false; }
    }

    partial void OnAddAnchorLeftMmChanged(double value)
    {
        if (_isLoadingEditor || _isSyncingAddFields) return;
        _isSyncingAddFields = true;
        try
        {
            var lClear = GetCurrentClearSpanMm();
            AddLeftLengthMm = Round50(lClear / 2 - value);
            AddLeftRatio = lClear <= 0 ? 0 : Math.Round(AddLeftLengthMm / lClear, 4);
            OnPropertyChanged(nameof(AddTotalMm));
        }
        finally { _isSyncingAddFields = false; }
        SyncAddEditor();
    }

    partial void OnAddAnchorRightMmChanged(double value)
    {
        if (_isLoadingEditor || _isSyncingAddFields) return;
        _isSyncingAddFields = true;
        try
        {
            var lClear = GetCurrentClearSpanMm();
            AddRightLengthMm = Round50(lClear / 2 - value);
            AddRightRatio = lClear <= 0 ? 0 : Math.Round(AddRightLengthMm / lClear, 4);
            OnPropertyChanged(nameof(AddTotalMm));
        }
        finally { _isSyncingAddFields = false; }
        SyncAddEditor();
    }

    partial void OnAddLeftLengthMmChanged(double value)
    {
        if (_isLoadingEditor || _isSyncingAddFields) return;
        _isSyncingAddFields = true;
        try
        {
            var lClear = GetCurrentClearSpanMm();
            AddAnchorLeftMm = Round50(lClear / 2 - value);
            AddLeftRatio = lClear <= 0 ? 0 : Math.Round(value / lClear, 4);
            OnPropertyChanged(nameof(AddTotalMm));
        }
        finally { _isSyncingAddFields = false; }
        SyncAddEditor();
    }

    partial void OnAddRightLengthMmChanged(double value)
    {
        if (_isLoadingEditor || _isSyncingAddFields) return;
        _isSyncingAddFields = true;
        try
        {
            var lClear = GetCurrentClearSpanMm();
            AddAnchorRightMm = Round50(lClear / 2 - value);
            AddRightRatio = lClear <= 0 ? 0 : Math.Round(value / lClear, 4);
            OnPropertyChanged(nameof(AddTotalMm));
        }
        finally { _isSyncingAddFields = false; }
        SyncAddEditor();
    }

    partial void OnAddLeftRatioChanged(double value)
    {
        if (_isLoadingEditor || _isSyncingAddFields) return;
        _isSyncingAddFields = true;
        try
        {
            var lClear = GetCurrentClearSpanMm();
            AddLeftLengthMm = Round50(lClear * value);
            AddAnchorLeftMm = Round50(lClear / 2 - AddLeftLengthMm);
            OnPropertyChanged(nameof(AddTotalMm));
        }
        finally { _isSyncingAddFields = false; }
        SyncAddEditor();
    }

    partial void OnAddRightRatioChanged(double value)
    {
        if (_isLoadingEditor || _isSyncingAddFields) return;
        _isSyncingAddFields = true;
        try
        {
            var lClear = GetCurrentClearSpanMm();
            AddRightLengthMm = Round50(lClear * value);
            AddAnchorRightMm = Round50(lClear / 2 - AddRightLengthMm);
            OnPropertyChanged(nameof(AddTotalMm));
        }
        finally { _isSyncingAddFields = false; }
        SyncAddEditor();
    }

    partial void OnAddStartPointChanged(int value)
    {
        if (_isLoadingEditor) return;
        RecalculateAddFieldsFromRatios();
        SyncAddEditor();
    }

    partial void OnAddEndPointChanged(int value)
    {
        if (_isLoadingEditor) return;
        RecalculateAddFieldsFromRatios();
        SyncAddEditor();
    }

    partial void OnAddStartTypeChanged(string value) => SyncAddEditor();
    partial void OnAddEndTypeChanged(string value) => SyncAddEditor();
    partial void OnAddDLeftMmChanged(double value) => SyncAddEditor();
    partial void OnAddDRightMmChanged(double value) => SyncAddEditor();
    partial void OnAddPositionInSectionChanged(string value) => SyncEditorToParent();
    partial void OnAddNumberChanged(int value)
    {
        NotifyDiagram();
        OnPropertyChanged(nameof(ShowAddTieC));
        EnsureDefaultTieC();
        SyncEditorToParent();
    }
    partial void OnAddTopEdgeHookDownLengthMmChanged(double value) => SyncEditorToParent();
    partial void OnStirrupDiameterMmChanged(int value) => SyncEditorToParent();
    partial void OnStirrupSpacingEndMmChanged(double value) => SyncEditorToParent();
    partial void OnStirrupSpacingMidMmChanged(double value) => SyncEditorToParent();
    partial void OnStirrupEndZoneMmChanged(double value) => SyncEditorToParent();
    partial void OnStirrupEndZoneStartMmChanged(double value) => SyncEditorToParent();
    partial void OnStirrupEndZoneEndMmChanged(double value) => SyncEditorToParent();
    partial void OnStirrupFirstDistanceMmChanged(double value) => SyncEditorToParent();
    partial void OnAntiBulgeHeightThresholdMmChanged(double value) => SyncEditorToParent();
    partial void OnAntiBulgeDiameterMmChanged(int value) => SyncEditorToParent();
    partial void OnAntiBulgeNumberChanged(int value) => SyncEditorToParent();
    partial void OnAntiBulgeTieDiameterMmChanged(int value) => SyncEditorToParent();
    partial void OnAntiBulgeSpacingMmChanged(double value) => SyncEditorToParent();
    partial void OnAntiBulgeColumnEmbedMmChanged(double value) => SyncEditorToParent();

    private void SyncEditorToParent()
    {
        if (_isLoadingEditor || _isSyncingParent) return;
        WriteBackToParent(saveEditor: !IsAddBarTab);
        RefreshRebarListFromState();
        RefreshElevationPreview();
    }

    private void SyncAddEditor()
    {
        if (_isLoadingEditor || _isSyncingParent) return;

        if (SelectedTab == RebarSettingTab.AddTopBar)
        {
            _topAddSelectedLayer = AddLayer;
            SaveAddState(AdditionalBarSide.TopAtSupport);
        }
        else if (SelectedTab == RebarSettingTab.AddBotBar)
        {
            _bottomAddSelectedLayer = AddLayer;
            SaveAddState(AdditionalBarSide.BottomAtMidspan);
        }

        WriteBackToParent(saveEditor: false);
        RefreshRebarListFromState();
        RefreshElevationPreview();
    }

    public bool ShowAddButtons => IsMainBarTab || IsAddBarTab;
    public bool ShowAddPreview => IsAddBarTab;
    public bool ShowSpanGrid => !IsAddBarTab;

    /// <summary>Ẩn panel Image ở tab Stirrup (ảnh tĩnh không khớp cấu hình đai phụ → gây nhầm).</summary>
    public bool ShowDiagramImage => !IsStirrupTab;

    /// <summary>Panel Section ở tab Stirrup: tiết diện + chấm thanh chủ đánh số 1..n (n = số thép chủ top).
    ///     Giúp biết vị trí 1,2,3,... khi chọn đai phụ. Số nhãn = số thép chủ top.</summary>
    public bool ShowSectionPanel => IsStirrupTab;

    public System.Collections.ObjectModel.ObservableCollection<SectionBarDot> SectionBars { get; } = [];
    public System.Collections.ObjectModel.ObservableCollection<SectionStirrupShape> SectionStirrups { get; } = [];

    private const double SecW = 150, SecH = 250, SecPad = 28;

    public double SectionFrameW => SecW;
    public double SectionFrameH => SecH;

    private static double SectionXOf(int k, int n) => n <= 1 ? SecW / 2 : SecPad + k * ((SecW - 2 * SecPad) / (n - 1));

    private void RefreshSectionPanel()
    {
        SectionBars.Clear();
        SectionStirrups.Clear();
        if (!IsStirrupTab) return;

        var nTop = Math.Max(1, _parent?.MainTopCount ?? 3);
        var nBot = Math.Max(1, _parent?.MainBottomCount ?? 3);
        const double dot = 12;

        for (var k = 0; k < nTop; k++)
            SectionBars.Add(new SectionBarDot(SectionXOf(k, nTop) - dot / 2, SecPad - dot / 2, dot, (k + 1).ToString()));
        for (var k = 0; k < nBot; k++)
            SectionBars.Add(new SectionBarDot(SectionXOf(k, nBot) - dot / 2, SecH - SecPad - dot / 2, dot, null));

        // Vẽ các đai phụ đã thêm (minh hoạ như video): khung kín / chữ U móc C ôm thanh top→bottom.
        var topY = SecPad;       // tâm hàng thanh top.
        var botY = SecH - SecPad; // tâm hàng thanh bottom.
        foreach (var item in AdditionalStirrupList)
        {
            var sIdx = Math.Clamp(item.StartBar - 1, 0, nTop - 1);
            if (item.Type == AdditionalStirrupType.Closed)
            {
                // LỒNG KÍN: khung chữ nhật kín ôm dải Start..End (như cũ).
                var eIdx = Math.Clamp(item.EndBar - 1, sIdx, nTop - 1);
                var x1 = SectionXOf(sIdx, nTop) - 7;
                var x2 = SectionXOf(eIdx, nTop) + 7;
                SectionStirrups.Add(new SectionStirrupShape(true, x1, topY - 7, x2 - x1, (botY - topY) + 14, "", 0, 0, 0, 0, 0, 0));
            }
            else
            {
                // ĐAI C: 2 nét dọc sát nhau tại 1 vị trí + 2 vòng móc nối 2 đầu.
                var xBar = SectionXOf(sIdx, nTop);
                const double g = 6;
                var xL = xBar - g;
                var xR = xBar + g;
                var data = $"M {xL},{topY} L {xL},{botY} M {xR},{topY} L {xR},{botY}";
                SectionStirrups.Add(new SectionStirrupShape(false, 0, 0, 0, 0, data,
                    HookTopX: xBar - g, HookTopY: topY - g,
                    HookBotX: xBar - g, HookBotY: botY - g,
                    HookR: g, HookSize: 2 * g));
            }
        }
    }

    // ── Form Main Top/Bot Bar ──
    public IReadOnlyList<int> DiameterOptions { get; } = Models.RebarDiameter.Standard.Select(d => d.Millimeters).ToList();
    public IReadOnlyList<int> NumberOptions { get; } = Enumerable.Range(1, 12).ToList();

    [ObservableProperty] private int _mainDiameterMm = 16;
    [ObservableProperty] private int _mainNumber = 3;
    [ObservableProperty] private int _mainStartPoint;
    [ObservableProperty] private int _mainEndPoint = 1;
    [ObservableProperty] private double _anchorLeftMm;
    [ObservableProperty] private double _anchorRightMm;
    [ObservableProperty] private double _anchorXLeftMm;
    [ObservableProperty] private double _anchorXRightMm;
    [ObservableProperty] private double _topBendDownLengthMm;
    [ObservableProperty] private string _mainPositionInSection = "0,1,2";

    // ── Form Add Top/Bot Bar (thép gia cường) ──
    public IReadOnlyList<int> LayerOptions { get; } = [1, 2];
    public IReadOnlyList<string> AddTypeOptions { get; } = ["Type 1: Attached to column", "Type 2: Go through span"];
    public IReadOnlyList<string> AddEndTypeOptions { get; } = ["1: Attached to column", "2: Go through the span"];

    [ObservableProperty] private int _addLayer = 1;
    [ObservableProperty] private int _addDiameterMm = 16;
    [ObservableProperty] private int _addNumber = 2;
    // Đai C giữ thép gia cường lớp 2 (≥3 cây): đường kính + khoảng cách (0 = không tạo). Mặc định D6@500.
    [ObservableProperty] private int _addTieCDiameterMm = 6;
    [ObservableProperty] private double _addTieCSpacingMm;
    /// <summary>Hiện ô đai C khi đang ở lớp 2 và số thanh ≥3.</summary>
    public bool ShowAddTieC => AddLayer == 2 && AddNumber >= 3;
    partial void OnAddTieCDiameterMmChanged(int value) => SyncEditorToParent();
    partial void OnAddTieCSpacingMmChanged(double value) => SyncEditorToParent();

    /// <summary>Đủ điều kiện đai C (layer 2, ≥3 cây) mà chưa nhập bước → tự điền MẶC ĐỊNH D6@500.</summary>
    private void EnsureDefaultTieC()
    {
        if (ShowAddTieC && AddTieCSpacingMm <= 0)
        {
            if (AddTieCDiameterMm <= 0) AddTieCDiameterMm = 6;
            AddTieCSpacingMm = 500;
        }
    }
    [ObservableProperty] private int _addStartPoint;
    [ObservableProperty] private int _addEndPoint = 1;
    [ObservableProperty] private string _addStartType = "1: Attached to column";
    [ObservableProperty] private string _addEndType = "1: Attached to column";
    [ObservableProperty] private double _addLeftRatio = 0.25;
    [ObservableProperty] private double _addRightRatio = 0.25;
    [ObservableProperty] private double _addLeftLengthMm;
    [ObservableProperty] private double _addRightLengthMm;
    [ObservableProperty] private double _addDLeftMm = 300;
    [ObservableProperty] private double _addDRightMm = 300;
    [ObservableProperty] private string _addPositionInSection = "0,1";
    [ObservableProperty] private double _addTopEdgeHookDownLengthMm = 300;
    [ObservableProperty] private string _addType = "Type 1: Attached to column";
    [ObservableProperty] private double _addAnchorLeftMm;
    [ObservableProperty] private double _addAnchorRightMm;

    public double AddTotalMm => AddLeftLengthMm + AddRightLengthMm;

    // ── Form Stirrup (phân bố cốt đai) ──
    public IReadOnlyList<int> StirrupDiameterOptions { get; } = [6, 8, 10, 12];

    [ObservableProperty] private int _stirrupDiameterMm = 6;
    [ObservableProperty] private bool _stirrupTwoEnds = true;
    [ObservableProperty] private double _stirrupSpacingEndMm = 150;
    [ObservableProperty] private double _stirrupSpacingMidMm = 200;
    [ObservableProperty] private double _stirrupEndZoneMm = 1250;
    [ObservableProperty] private double _stirrupEndZoneStartMm = 1250;
    [ObservableProperty] private double _stirrupEndZoneEndMm = 1250;
    [ObservableProperty] private double _stirrupFirstDistanceMm = 50;

    // === Đai phụ (Additional Stirrup) ===
    public System.Collections.ObjectModel.ObservableCollection<AdditionalStirrupItem> AdditionalStirrupList { get; } = [];
    [ObservableProperty] private AdditionalStirrupItem? _selectedAdditionalStirrup;
    [ObservableProperty] private int _additionalStirrupDiameterMm = 8;
    [ObservableProperty] private int _additionalStirrupStart = 2;  // 1-based, như video.
    [ObservableProperty] private int _additionalStirrupEnd = 3;
    // 2 radio icon (như video): true = đai LỒNG KÍN (icon 2), false = đai MÓC C (icon 1).
    [ObservableProperty] private bool _additionalStirrupClosed = true;

    /// <summary>Nghịch đảo cho radio "đai móc C".</summary>
    public bool AdditionalStirrupCHook
    {
        get => !AdditionalStirrupClosed;
        set => AdditionalStirrupClosed = !value;
    }

    partial void OnAdditionalStirrupClosedChanged(bool value)
    {
        OnPropertyChanged(nameof(AdditionalStirrupCHook));
        OnPropertyChanged(nameof(ShowStartEnd));
        OnPropertyChanged(nameof(ShowPosition));
    }

    /// <summary>Lồng kín → hiện Start/End. Móc C → ẩn.</summary>
    public bool ShowStartEnd => AdditionalStirrupClosed;

    /// <summary>Móc C → hiện ô vị trí lẻ (1 thanh). Lồng kín → ẩn.</summary>
    public bool ShowPosition => !AdditionalStirrupClosed;

    private AdditionalStirrupType CurrentAddStirrupType =>
        AdditionalStirrupClosed ? AdditionalStirrupType.Closed : AdditionalStirrupType.CHook;

    [RelayCommand]
    private void AddAdditionalStirrup()
    {
        AdditionalStirrupList.Add(new AdditionalStirrupItem(AdditionalStirrupDiameterMm, CurrentAddStirrupType, AdditionalStirrupStart, AdditionalStirrupEnd));
        RefreshSectionPanel();
        SyncEditorToParent();
    }

    [RelayCommand]
    private void ModifyAdditionalStirrup()
    {
        if (SelectedAdditionalStirrup is null) return;
        var idx = AdditionalStirrupList.IndexOf(SelectedAdditionalStirrup);
        if (idx < 0) return;
        AdditionalStirrupList[idx] = new AdditionalStirrupItem(AdditionalStirrupDiameterMm, CurrentAddStirrupType, AdditionalStirrupStart, AdditionalStirrupEnd);
        RefreshSectionPanel();
        SyncEditorToParent();
    }

    [RelayCommand]
    private void RemoveAdditionalStirrup()
    {
        if (SelectedAdditionalStirrup is null)
        {
            StatusMessage = "Hay chon mot dai phu trong list de xoa.";
            return;
        }
        AdditionalStirrupList.Remove(SelectedAdditionalStirrup);
        RefreshSectionPanel();
        SyncEditorToParent();
    }

    public bool StirrupUniform
    {
        get => !StirrupTwoEnds;
        set => StirrupTwoEnds = !value;
    }

    partial void OnStirrupTwoEndsChanged(bool value)
    {
        OnPropertyChanged(nameof(StirrupUniform));
        SyncEditorToParent();
    }

    private StirrupSpanState CurrentStirrupState(int spanIndex) => new(
        spanIndex,
        true,
        StirrupDiameterMm,
        StirrupTwoEnds,
        StirrupSpacingEndMm,
        StirrupSpacingMidMm,
        StirrupTwoEnds ? StirrupEndZoneStartMm : 0,
        StirrupTwoEnds ? StirrupEndZoneEndMm : 0,
        StirrupFirstDistanceMm);

    private StirrupConfig MakeStirrupConfig(StirrupSpanState state)
    {
        state = NormalizeStirrupStateForSpan(state);
        return new StirrupConfig
        {
            Diameter = new RebarDiameter(state.DiameterMm),
            Mode = state.TwoEnds ? StirrupMode.TwoEnds : StirrupMode.Uniform,
            SpacingEndMm = state.SpacingEndMm,
            SpacingMidMm = state.SpacingMidMm,
            EndZoneLengthMm = Math.Max(state.EndZoneStartMm, state.EndZoneEndMm),
            EndZoneStartMm = state.EndZoneStartMm,
            EndZoneEndMm = state.EndZoneEndMm,
            FirstDistanceFromSupportMm = state.FirstDistanceMm
        };
    }

    private void EnsureStirrupSpanItems()
    {
        var spanCount = Math.Max(1, SpanRows.Count > 0 ? SpanRows.Count : _parent.PickedSpans.Count);
        for (var i = 0; i < spanCount; i++)
        {
            if (_stirrupSpanItems.Any(s => s.SpanIndex == i)) continue;
            _stirrupSpanItems.Add(NormalizeStirrupStateForSpan(CurrentStirrupState(i)));
        }

        for (var i = 0; i < _stirrupSpanItems.Count; i++)
            _stirrupSpanItems[i] = NormalizeStirrupStateForSpan(_stirrupSpanItems[i]);

        _stirrupSpanItems.Sort((a, b) => a.SpanIndex.CompareTo(b.SpanIndex));
    }

    private void SaveStirrupEditorToSelectedSpan()
    {
        if (SelectedTab != RebarSettingTab.Stirrup) return;
        EnsureStirrupSpanItems();
        var spanIndex = TryGetSelectedStirrupSpanIndex(out var selected) ? selected : _selectedStirrupSpanIndex;
        var existing = _stirrupSpanItems.FindIndex(s => s.SpanIndex == spanIndex);
        var state = NormalizeStirrupStateForSpan(CurrentStirrupState(spanIndex));
        if (existing >= 0) _stirrupSpanItems[existing] = state;
        else _stirrupSpanItems.Add(state);
    }

    private StirrupSpanState NormalizeStirrupStateForSpan(StirrupSpanState state)
    {
        if (!state.TwoEnds)
            return state with { EndZoneStartMm = 0, EndZoneEndMm = 0 };

        var spanLength = GetSpanLengthMm(state.SpanIndex);
        if (spanLength <= 0)
            return state;

        var end1 = state.EndZoneStartMm > 0 ? state.EndZoneStartMm : spanLength / 4.0;
        var end2 = state.EndZoneEndMm > 0 ? state.EndZoneEndMm : spanLength / 4.0;

        // Khi thêm gối mới, nhịp có thể ngắn hơn cấu hình cũ. Nếu End1+End2 ăn hết nhịp,
        // trả về mặc định L/4 mỗi đầu để còn vùng giữa A2 thật.
        if (end1 + end2 >= spanLength)
        {
            end1 = spanLength / 4.0;
            end2 = spanLength / 4.0;
        }

        var maxEnd = spanLength * 0.45;
        end1 = Math.Min(end1, maxEnd);
        end2 = Math.Min(end2, maxEnd);
        if (end1 + end2 >= spanLength)
        {
            var fallback = spanLength / 4.0;
            end1 = fallback;
            end2 = fallback;
        }

        return state with
        {
            EndZoneStartMm = Math.Round(end1),
            EndZoneEndMm = Math.Round(end2)
        };
    }

    private double GetSpanLengthMm(int spanIndex)
    {
        if (spanIndex >= 0 && spanIndex < SpanRows.Count)
            return SpanRows[spanIndex].LengthMm;
        if (spanIndex >= 0 && spanIndex < _parent.PickedSpans.Count)
            return _parent.PickedSpans[spanIndex].LengthMm;
        return 0;
    }

    private bool TryGetSelectedStirrupSpanIndex(out int spanIndex)
    {
        spanIndex = -1;
        const string prefix = "stirrup-span-";
        var key = SelectedRebarListItem?.Key;
        return key is not null
               && key.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(key[prefix.Length..], out spanIndex)
               && spanIndex >= 0;
    }

    private void LoadStirrupStateIntoEditor(StirrupSpanState state)
    {
        _isLoadingEditor = true;
        _selectedStirrupSpanIndex = state.SpanIndex;
        StirrupDiameterMm = state.DiameterMm;
        StirrupTwoEnds = state.TwoEnds;
        StirrupSpacingEndMm = state.SpacingEndMm;
        StirrupSpacingMidMm = state.SpacingMidMm;
        StirrupEndZoneStartMm = state.EndZoneStartMm;
        StirrupEndZoneEndMm = state.EndZoneEndMm;
        StirrupEndZoneMm = Math.Max(state.EndZoneStartMm, state.EndZoneEndMm);
        StirrupFirstDistanceMm = state.FirstDistanceMm;
        _isLoadingEditor = false;
        OnPropertyChanged(nameof(StirrupUniform));
    }

    // ── Form Anti-bulge rebar (thép chống phình/co ngót) ──
    [ObservableProperty] private double _antiBulgeHeightThresholdMm = 550;
    [ObservableProperty] private int _antiBulgeDiameterMm = 12;
    [ObservableProperty] private int _antiBulgeNumber;
    [ObservableProperty] private int _antiBulgeTieDiameterMm = 6;
    [ObservableProperty] private double _antiBulgeSpacingMm = 500;
    [ObservableProperty] private double _antiBulgeColumnEmbedMm;

    /// <summary>Danh sách nhóm thép đang bật, đồng bộ với form hiện tại.</summary>
    public System.Collections.ObjectModel.ObservableCollection<RebarListItem> RebarList { get; } = [];

    [ObservableProperty] private RebarListItem? _selectedRebarListItem;

    partial void OnSelectedRebarListItemChanged(RebarListItem? value)
    {
        if (_isRefreshingRebarList || value is null) return;
        if (value.Key == "main-top")
        {
            _isLoadingEditor = true;
            MainDiameterMm = _mainTopDiameterMmState;
            MainNumber = _mainTopNumberState;
            MainStartPoint = _mainTopStartPointState;
            MainEndPoint = _mainTopEndPointState;
            AnchorLeftMm = _mainTopAnchorLeftMmState;
            AnchorRightMm = _mainTopAnchorRightMmState;
            AnchorXLeftMm = _mainTopAnchorXLeftMmState;
            AnchorXRightMm = _mainTopAnchorXRightMmState;
            TopBendDownLengthMm = _mainTopBendDownLengthMmState;
            MainPositionInSection = _mainTopPositionInSectionState;
            _isLoadingEditor = false;
            RefreshElevationPreview();
            return;
        }

        if (value.Key == "main-bottom")
        {
            _isLoadingEditor = true;
            MainDiameterMm = _mainBottomDiameterMmState;
            MainNumber = _mainBottomNumberState;
            MainStartPoint = _mainBottomStartPointState;
            MainEndPoint = _mainBottomEndPointState;
            AnchorLeftMm = _mainBottomAnchorLeftMmState;
            AnchorRightMm = _mainBottomAnchorRightMmState;
            AnchorXLeftMm = _mainBottomAnchorXLeftMmState;
            AnchorXRightMm = _mainBottomAnchorXRightMmState;
            MainPositionInSection = _mainBottomPositionInSectionState;
            _isLoadingEditor = false;
            RefreshElevationPreview();
            return;
        }

        if (value.Key.StartsWith("add-top-item-", StringComparison.Ordinal))
        {
            if (!TryGetSelectedTopAddItemIndex(out var index)) return;

            _isLoadingEditor = true;
            LoadAddStateIntoEditor(GetDisplayTopAddState(_topAddItems[index]));
            _isLoadingEditor = false;
            RefreshElevationPreview();
            return;
        }

        if (value.Key.StartsWith("add-bot-item-", StringComparison.Ordinal))
        {
            if (!TryGetSelectedBottomAddItemIndex(out var index)) return;

            _isLoadingEditor = true;
            LoadAddStateIntoEditor(GetDisplayBottomAddState(_bottomAddItems[index]));
            _isLoadingEditor = false;
            RefreshElevationPreview();
            return;
        }

        if (value.Key.StartsWith("stirrup-span-", StringComparison.Ordinal))
        {
            EnsureStirrupSpanItems();
            if (!TryGetSelectedStirrupSpanIndex(out var spanIndex)) return;
            var state = _stirrupSpanItems.FirstOrDefault(s => s.SpanIndex == spanIndex);
            if (state is null) return;
            LoadStirrupStateIntoEditor(state);
            RefreshElevationPreview();
        }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        if (Enum.TryParse<RebarSettingTab>(tab, out var parsed) && parsed != SelectedTab)
        {
            SaveEditorToSelectedTab();
            SelectedTab = parsed;
            LoadEditorFromSelectedTab();
        }
    }

    [RelayCommand]
    private void PickBeam()
    {
        _handler.Request = RebarCreationRequest.PickBeamInfo;
        _handler.OnBeamInfo = OnBeamInfo;
        StatusMessage = "Hãy chọn dầm trong Revit để lấy thông số nhịp…";
        _externalEvent.Raise();
    }

    private void OnBeamInfo(IReadOnlyList<SpanInfo> spans)
    {
        SpanRows.Clear();
        foreach (var s in spans)
            SpanRows.Add(new SpanRowViewModel(s));
        OnPropertyChanged(nameof(PointOptions));
        EnsureMainBarsFullRun();
        LoadEditorFromSelectedTab();
        RefreshRebarListFromState();
        RefreshElevationPreview();

        StatusMessage = spans.Count == 0
            ? "Chưa chọn được dầm hoặc không đọc được nhịp."
            : $"Đã lấy {spans.Count} nhịp. Chiều dài thép gia cường đã tính tự động (TCVN) — có thể sửa tay.";
    }

    [RelayCommand]
    private void AddRebar()
    {
        if (!IsAddBarTab)
            SaveEditorToSelectedTab();

        if (SelectedTab == RebarSettingTab.MainTopBar)
        {
            _mainTopEnabledState = true;
        }
        else if (SelectedTab == RebarSettingTab.MainBotBar)
        {
            _mainBottomEnabledState = true;
        }
        else if (IsAddBarTab)
        {
            if (SelectedTab == RebarSettingTab.AddTopBar)
                _topAddSelectedLayer = AddLayer;
            else
                _bottomAddSelectedLayer = AddLayer;

            var enabledState = new AddBarState(true, AddLayer, AddNumber, AddDiameterMm,
                AddStartPoint, AddEndPoint, AddStartType, AddEndType,
                AddLeftRatio, AddRightRatio, AddLeftLengthMm, AddRightLengthMm,
                SelectedTab == RebarSettingTab.AddTopBar ? AddDLeftMm : 0,
                SelectedTab == RebarSettingTab.AddTopBar ? AddDRightMm : 0,
                AddPositionInSection,
                AddAnchorLeftMm,
                AddAnchorRightMm);

            if (SelectedTab == RebarSettingTab.AddTopBar)
            {
                AddOrUpdateTopAdditionalItems(enabledState);
            }
            else
            {
                AddOrUpdateBottomAdditionalItems(enabledState);
            }
        }

        WriteBackToParent();
        RefreshRebarListFromState();
        RefreshElevationPreview();
    }

    [RelayCommand]
    private void ApplyStirrupAllSpans()
    {
        EnsureStirrupSpanItems();
        var spanCount = Math.Max(1, SpanRows.Count > 0 ? SpanRows.Count : _parent.PickedSpans.Count);
        _stirrupSpanItems.Clear();
        for (var i = 0; i < spanCount; i++)
            _stirrupSpanItems.Add(NormalizeStirrupStateForSpan(CurrentStirrupState(i)));
        RefreshRebarListFromState();
        RefreshElevationPreview();
    }

    [RelayCommand]
    private void ApplyStirrupRemainingSpans()
    {
        EnsureStirrupSpanItems();
        var start = TryGetSelectedStirrupSpanIndex(out var selected) ? selected : _selectedStirrupSpanIndex;
        var spanCount = Math.Max(1, SpanRows.Count > 0 ? SpanRows.Count : _parent.PickedSpans.Count);
        for (var i = start; i < spanCount; i++)
        {
            var state = NormalizeStirrupStateForSpan(CurrentStirrupState(i));
            var existing = _stirrupSpanItems.FindIndex(s => s.SpanIndex == i);
            if (existing >= 0) _stirrupSpanItems[existing] = state;
            else _stirrupSpanItems.Add(state);
        }
        RefreshRebarListFromState();
        RefreshElevationPreview();
    }

    [RelayCommand]
    private void DeleteSelectedStirrupSpan()
    {
        if (!TryGetSelectedStirrupSpanIndex(out var spanIndex)) return;
        var existing = _stirrupSpanItems.FindIndex(s => s.SpanIndex == spanIndex);
        if (existing >= 0)
            _stirrupSpanItems[existing] = _stirrupSpanItems[existing] with { Enabled = false };
        SelectedRebarListItem = null;
        RefreshRebarListFromState();
        RefreshElevationPreview();
    }

    private void AddOrUpdateTopAdditionalItems(AddBarState state)
    {
        var maxSupport = Math.Max(0, DefaultEndPoint);
        var requestedSupport = Math.Clamp(state.StartPoint, 0, maxSupport);
        var support = FindNextTopAddSupport(state.Layer, requestedSupport, maxSupport);
        var item = NormalizeTopAddItemForSupport(state, support, maxSupport) with
        {
            Enabled = true,
            StartPoint = support,
            EndPoint = support,
            DLeftMm = support == 0 ? state.DLeftMm : 0,
            DRightMm = support == maxSupport ? state.DRightMm : 0
        };

        var existingIndex = _topAddItems.FindIndex(i => i.Layer == item.Layer && i.StartPoint == support && i.EndPoint == support);
        if (existingIndex >= 0) _topAddItems[existingIndex] = item;
        else _topAddItems.Add(item);

        _topAddItems.Sort((a, b) =>
        {
            var layerCompare = a.Layer.CompareTo(b.Layer);
            return layerCompare != 0 ? layerCompare : a.StartPoint.CompareTo(b.StartPoint);
        });
        _topAddItemsMaterialized = true;

        MoveTopAddEditorToNextFreeSupport(state, support, maxSupport);
    }

    private void AddOrUpdateBottomAdditionalItems(AddBarState state)
    {
        var maxSupport = Math.Max(1, DefaultEndPoint);
        var requestedSpan = Math.Clamp(state.StartPoint, 0, maxSupport - 1);
        var span = FindNextBottomAddSpan(state.Layer, requestedSpan, maxSupport);
        var item = NormalizeBottomAddItemForSpan(state, span, maxSupport) with
        {
            Enabled = true,
            StartPoint = span,
            EndPoint = span + 1
        };

        var existingIndex = _bottomAddItems.FindIndex(i => i.Layer == item.Layer && i.StartPoint == span && i.EndPoint == span + 1);
        if (existingIndex >= 0) _bottomAddItems[existingIndex] = item;
        else _bottomAddItems.Add(item);

        _bottomAddItems.Sort((a, b) =>
        {
            var layerCompare = a.Layer.CompareTo(b.Layer);
            return layerCompare != 0 ? layerCompare : a.StartPoint.CompareTo(b.StartPoint);
        });
        _bottomAddItemsMaterialized = true;

        MoveBottomAddEditorToNextFreeSpan(state, span, maxSupport);
    }

    private AddBarState NormalizeTopAddItemForSupport(AddBarState state, int support, int maxSupport)
    {
        var leftSpan = support > 0 && support - 1 < SpanRows.Count
            ? Math.Max(1.0, SpanRows[support - 1].LengthMm)
            : 0;
        var rightSpan = support < SpanRows.Count
            ? Math.Max(1.0, SpanRows[support].LengthMm)
            : 0;

        var leftLength = ResolveRuleLength(state.LeftLengthMm, state.LeftRatio, leftSpan);
        var rightLength = ResolveRuleLength(state.RightLengthMm, state.RightRatio, rightSpan);

        return state with
        {
            StartPoint = support,
            EndPoint = support,
            LeftLengthMm = leftLength,
            RightLengthMm = rightLength,
            DLeftMm = support == 0 ? state.DLeftMm : 0,
            DRightMm = support == maxSupport ? state.DRightMm : 0
        };
    }

    private AddBarState NormalizeBottomAddItemForSpan(AddBarState state, int span, int maxSupport)
    {
        var target = state with { StartPoint = span, EndPoint = span + 1 };
        var clearLength = GetBottomItemClearLengthMm(target);
        if (clearLength <= 0) return target;

        var leftLength = ResolveRuleLength(state.LeftLengthMm, state.LeftRatio, clearLength);
        var rightLength = ResolveRuleLength(state.RightLengthMm, state.RightRatio, clearLength);
        if (leftLength <= 0 && rightLength <= 0)
        {
            leftLength = Round50(clearLength / 2);
            rightLength = Round50(clearLength / 2);
        }

        return target with
        {
            LeftLengthMm = leftLength,
            RightLengthMm = rightLength,
            AnchorLeftMm = Round50(clearLength / 2 - leftLength),
            AnchorRightMm = Round50(clearLength / 2 - rightLength)
        };
    }

    private static double ResolveRuleLength(double manualLength, double ratio, double basisLength)
    {
        if (basisLength <= 0) return 0;
        return ratio > 0
            ? Round50(Math.Max(0, ratio) * basisLength)
            : Math.Max(0, manualLength);
    }

    private int FindNextTopAddSupport(int layer, int requestedSupport, int maxSupport)
    {
        var count = maxSupport + 1;
        for (var offset = 0; offset < count; offset++)
        {
            var support = (requestedSupport + offset) % count;
            if (!_topAddItems.Any(i => i.Layer == layer && i.StartPoint == support && i.EndPoint == support))
                return support;
        }

        return requestedSupport;
    }

    private int FindNextBottomAddSpan(int layer, int requestedSpan, int maxSupport)
    {
        for (var offset = 0; offset < maxSupport; offset++)
        {
            var span = (requestedSpan + offset) % maxSupport;
            if (!_bottomAddItems.Any(i => i.Layer == layer && i.StartPoint == span && i.EndPoint == span + 1))
                return span;
        }

        return requestedSpan;
    }

    private void MoveTopAddEditorToNextFreeSupport(AddBarState sourceState, int addedSupport, int maxSupport)
    {
        var next = FindNextTopAddSupport(sourceState.Layer, Math.Min(maxSupport, addedSupport + 1), maxSupport);
        var nextState = NormalizeTopAddItemForSupport(sourceState, next, maxSupport);
        _isLoadingEditor = true;
        LoadAddStateIntoEditor(nextState);
        _isLoadingEditor = false;
    }

    private void MoveBottomAddEditorToNextFreeSpan(AddBarState sourceState, int addedSpan, int maxSupport)
    {
        var next = FindNextBottomAddSpan(sourceState.Layer, Math.Min(maxSupport - 1, addedSpan + 1), maxSupport);
        var nextState = NormalizeBottomAddItemForSpan(sourceState, next, maxSupport);
        _isLoadingEditor = true;
        LoadAddStateIntoEditor(nextState);
        _isLoadingEditor = false;
    }

    private AddBarState GetDisplayBottomAddState(AddBarState state)
    {
        var clearLength = GetBottomItemClearLengthMm(state);
        if (clearLength <= 0) return state;

        var leftLength = state.LeftLengthMm > 0
            ? state.LeftLengthMm
            : Round50(Math.Max(0, state.LeftRatio) * clearLength);
        var rightLength = state.RightLengthMm > 0
            ? state.RightLengthMm
            : Round50(Math.Max(0, state.RightRatio) * clearLength);

        if (leftLength <= 0 && rightLength <= 0)
        {
            leftLength = Round50(clearLength / 2);
            rightLength = Round50(clearLength / 2);
        }

        var anchorLeft = state.AnchorLeftMm != 0
            ? state.AnchorLeftMm
            : Round50(clearLength / 2 - leftLength);
        var anchorRight = state.AnchorRightMm != 0
            ? state.AnchorRightMm
            : Round50(clearLength / 2 - rightLength);

        return state with
        {
            LeftLengthMm = leftLength,
            RightLengthMm = rightLength,
            AnchorLeftMm = anchorLeft,
            AnchorRightMm = anchorRight
        };
    }

    private AddBarState GetDisplayTopAddState(AddBarState state)
    {
        var support = Math.Clamp(state.StartPoint, 0, Math.Max(0, DefaultEndPoint));
        var leftSpan = support > 0 && support - 1 < SpanRows.Count
            ? Math.Max(1.0, SpanRows[support - 1].LengthMm)
            : 0;
        var rightSpan = support < SpanRows.Count
            ? Math.Max(1.0, SpanRows[support].LengthMm)
            : 0;

        var leftLength = state.LeftLengthMm > 0
            ? state.LeftLengthMm
            : Round50(Math.Max(0, state.LeftRatio) * leftSpan);
        var rightLength = state.RightLengthMm > 0
            ? state.RightLengthMm
            : Round50(Math.Max(0, state.RightRatio) * rightSpan);

        return state with
        {
            LeftLengthMm = leftLength,
            RightLengthMm = rightLength,
            DLeftMm = support == 0 ? state.DLeftMm : 0,
            DRightMm = support == DefaultEndPoint ? state.DRightMm : 0
        };
    }

    private double GetBottomItemClearLengthMm(AddBarState state)
    {
        var maxSupport = Math.Max(1, DefaultEndPoint);
        var itemFirst = Math.Clamp(state.StartPoint, 0, maxSupport - 1);
        var itemLast = state.EndPoint == int.MaxValue ? maxSupport : Math.Clamp(state.EndPoint, itemFirst + 1, maxSupport);
        if (itemLast <= itemFirst) return 0;

        var clearLength = 0.0;
        for (var i = itemFirst; i < itemLast && i < SpanRows.Count; i++)
            clearLength += Math.Max(1.0, SpanRows[i].LengthMm);

        if (clearLength > 0) return clearLength;
        return (itemLast - itemFirst) * 6000.0;
    }

    private void RefreshElevationPreview()
    {
        PreviewLines.Clear();
        PreviewRects.Clear();
        PreviewTexts.Clear();

        var spans = SpanRows.Count > 0
            ? SpanRows.Select(r => Math.Max(1.0, r.LengthMm)).ToList()
            : new List<double> { 5200, 4200, 3900 };

        var total = spans.Sum();
        if (total <= 0) return;

        const double left = 38;
        const double right = 1050;
        const double topY = 62;
        const double bottomY = 128;
        const double columnTop = 44;
        const double columnBottom = 148;
        var width = right - left;
        var scale = width / total;

        var supports = new List<double> { left };
        var cursor = left;
        for (var i = 0; i < spans.Count; i++)
        {
            cursor += spans[i] * scale;
            supports.Add(cursor);
        }

        PreviewLines.Add(new PreviewLine(left, topY, right, topY, "#111111", 1.4));
        PreviewLines.Add(new PreviewLine(left, bottomY, right, bottomY, "#111111", 1.4));
        PreviewLines.Add(new PreviewLine(left, columnBottom, right, columnBottom, "#111111", 1.0));

        for (var i = 0; i < supports.Count; i++)
        {
            var x = supports[i];
            PreviewLines.Add(new PreviewLine(x, 16, x, 158, "#1F55FF", 1.2));
            PreviewLines.Add(new PreviewLine(x - 22, columnTop, x + 22, columnTop, "#111111", 1.0));
            PreviewLines.Add(new PreviewLine(x - 22, columnTop, x - 22, columnBottom, "#111111", 1.0));
            PreviewLines.Add(new PreviewLine(x + 22, columnTop, x + 22, columnBottom, "#111111", 1.0));
            PreviewTexts.Add(new PreviewText(i.ToString(), x - 7, 4, "#1F55FF", 12));
            PreviewTexts.Add(new PreviewText($"{i + 1}:", x + 6, topY + 9, "#CC3333", 11));
            PreviewTexts.Add(new PreviewText($"{i + 1}'", x + 6, bottomY + 9, "#CC3333", 11));
        }

        cursor = left;
        for (var i = 0; i < spans.Count; i++)
        {
            var x0 = cursor;
            var x1 = cursor + spans[i] * scale;
            var mid = (x0 + x1) / 2;
            PreviewTexts.Add(new PreviewText(spans[i].ToString("F0"), mid - 18, 142, "#111111", 10));
            PreviewTexts.Add(new PreviewText($"Span {i}", mid - 24, 164, "#DD5555", 13));
            cursor = x1;
        }

        if (SelectedTab == RebarSettingTab.Stirrup)
        {
            DrawStirrupPreview();
            return;
        }

        if (IsMainBarTab)
        {
            DrawMainBarPreview();
            return;
        }
        if (!IsAddBarTab) return;

        var maxSupport = supports.Count - 1;
        var first = Math.Clamp(AddStartPoint, 0, maxSupport);
        var last = Math.Clamp(AddEndPoint, 0, maxSupport);
        if (last < first) (first, last) = (last, first);

        var barY = SelectedTab == RebarSettingTab.AddTopBar ? topY + 10 : bottomY - 10;

        if (SelectedTab == RebarSettingTab.AddTopBar)
        {
            EnsureTopAddItemsFromLegacy();
            foreach (var item in _topAddItems)
                DrawTopPreviewItem(GetDisplayTopAddState(item));
        }
        else // AddBotBar
        {
            EnsureBottomAddItemsFromLegacy();
            for (var i = 0; i < _bottomAddItems.Count; i++)
                DrawBottomPreviewItem(GetDisplayBottomAddState(_bottomAddItems[i]), i);
        }

        void DrawTopPreviewItem(AddBarState state)
        {
            var itemFirst = Math.Clamp(state.StartPoint, 0, maxSupport);
            var itemLast = state.EndPoint == int.MaxValue ? maxSupport : Math.Clamp(state.EndPoint, 0, maxSupport);
            if (itemLast < itemFirst) (itemFirst, itemLast) = (itemLast, itemFirst);

            var startIsType1 = IsAttachedToColumn(state.StartType);
            var endIsType1 = IsAttachedToColumn(state.EndType);

            for (var supportIndex = itemFirst; supportIndex <= itemLast; supportIndex++)
            {
                var supportX = supports[supportIndex];
                var leftSpan = supportIndex > 0 ? spans[supportIndex - 1] : 0;
                var rightSpan = supportIndex < spans.Count ? spans[supportIndex] : 0;
                var leftLength = ResolvePreviewLength(state.LeftLengthMm, state.LeftRatio, leftSpan);
                var rightLength = ResolvePreviewLength(state.RightLengthMm, state.RightRatio, rightSpan);

                var x0 = supportX - 22 - (leftSpan > 0 ? leftLength * scale : 0);
                var x1 = supportX + 22 + (rightSpan > 0 ? rightLength * scale : 0);
                x0 = Math.Max(left, x0);
                x1 = Math.Min(right, x1);

                if (x1 - x0 <= 1) continue;

                PreviewLines.Add(new PreviewLine(x0, barY, x1, barY, "#FF2222", 2.4));

                if (supportIndex == itemFirst && startIsType1 && state.DLeftMm > 0)
                    PreviewLines.Add(new PreviewLine(x0, barY, x0, Math.Min(columnBottom, barY + state.DLeftMm * scale * 0.45), "#FF2222", 2.4));

                if (supportIndex == itemLast && endIsType1 && state.DRightMm > 0)
                    PreviewLines.Add(new PreviewLine(x1, barY, x1, Math.Min(columnBottom, barY + state.DRightMm * scale * 0.45), "#FF2222", 2.4));

                if (leftLength > 0)
                {
                    var labelLeft = supportX - 22;
                    PreviewLines.Add(new PreviewLine(x0, barY - 28, labelLeft, barY - 28, "#55B7E8", 1.0));
                    PreviewTexts.Add(new PreviewText(leftLength.ToString("F0"), (x0 + labelLeft) / 2 - 18, barY - 43, "#2596D1", 10));
                }

                if (rightLength > 0)
                {
                    var labelRight = supportX + 22;
                    PreviewLines.Add(new PreviewLine(labelRight, barY - 28, x1, barY - 28, "#55B7E8", 1.0));
                    PreviewTexts.Add(new PreviewText(rightLength.ToString("F0"), (labelRight + x1) / 2 - 18, barY - 43, "#2596D1", 10));
                }
            }
        }

        void DrawBottomPreviewItem(AddBarState state, int itemIndex)
        {
            var itemFirst = Math.Clamp(state.StartPoint, 0, maxSupport);
            var itemLast = state.EndPoint == int.MaxValue ? maxSupport : Math.Clamp(state.EndPoint, 0, maxSupport);
            if (itemLast <= itemFirst) itemLast = Math.Min(maxSupport, itemFirst + 1);
            if (itemLast <= itemFirst) return;

            var startX = supports[itemFirst];
            var endX = supports[itemLast];
            var clearStartX = startX + 22;
            var clearEndX = endX - 22;
            if (clearEndX <= clearStartX) return;

            var clearLength = 0.0;
            for (var i = itemFirst; i < itemLast && i < spans.Count; i++)
                clearLength += spans[i];

            var leftLength = state.LeftLengthMm > 0 ? state.LeftLengthMm : Math.Max(0, state.LeftRatio) * clearLength;
            var rightLength = state.RightLengthMm > 0 ? state.RightLengthMm : Math.Max(0, state.RightRatio) * clearLength;
            if (leftLength <= 0 && rightLength <= 0)
            {
                leftLength = clearLength * 0.5;
                rightLength = clearLength * 0.5;
            }

            var anchorLeft = Math.Abs(state.AnchorLeftMm);
            var anchorRight = Math.Abs(state.AnchorRightMm);
            var clearWidth = clearEndX - clearStartX;
            var dimensionTotal = anchorLeft + leftLength + rightLength + anchorRight;
            if (dimensionTotal <= 0) return;

            var p0 = clearStartX;
            var p1 = p0 + clearWidth * (anchorLeft / dimensionTotal);
            var p2 = p1 + clearWidth * (leftLength / dimensionTotal);
            var p3 = p2 + clearWidth * (rightLength / dimensionTotal);
            var p4 = clearEndX;

            if (p3 - p1 <= 1) return;

            PreviewLines.Add(new PreviewLine(p1, barY, p3, barY, "#FF2222", 2.4));

            var dimY = barY - 58;
            AddPreviewDimension(p0, p1, dimY, anchorLeft);
            AddPreviewDimension(p1, p2, dimY, leftLength / 2);
            AddPreviewDimension(p2, p3, dimY, rightLength / 2);
            AddPreviewDimension(p3, p4, dimY, anchorRight);
        }

        void DrawStirrupPreview()
        {
            EnsureStirrupSpanItems();
            var spanIndex = TryGetSelectedStirrupSpanIndex(out var selected) ? selected : _selectedStirrupSpanIndex;
            spanIndex = Math.Clamp(spanIndex, 0, spans.Count - 1);
            var state = _stirrupSpanItems.FirstOrDefault(s => s.SpanIndex == spanIndex) ?? CurrentStirrupState(spanIndex);

            var spanStartX = supports[spanIndex] + 22;
            var spanEndX = supports[spanIndex + 1] - 22;
            if (spanEndX <= spanStartX) return;

            PreviewRects.Add(new PreviewRect(spanStartX, topY + 8, spanEndX - spanStartX, bottomY - topY - 16, "#55FF9999", "#CC3333", 1.2));

            var spanLength = spans[spanIndex];
            var dimY = topY - 22;
            if (!state.TwoEnds)
            {
                AddPreviewDimension(spanStartX, spanEndX, dimY, spanLength);
                PreviewTexts.Add(new PreviewText($"D{state.DiameterMm}@{state.SpacingEndMm:F0}",
                    (spanStartX + spanEndX) / 2 - 26, dimY + 9, "#2596D1", 9));
                return;
            }

            var end1 = state.EndZoneStartMm > 0 ? state.EndZoneStartMm : spanLength / 4;
            var end2 = state.EndZoneEndMm > 0 ? state.EndZoneEndMm : spanLength / 4;
            if (end1 + end2 > spanLength)
            {
                var ratio = spanLength / (end1 + end2);
                end1 *= ratio;
                end2 *= ratio;
            }
            var mid = Math.Max(0, spanLength - end1 - end2);
            var x1 = spanStartX + (end1 / spanLength) * (spanEndX - spanStartX);
            var x2 = x1 + (mid / spanLength) * (spanEndX - spanStartX);

            AddPreviewDimension(spanStartX, x1, dimY, end1);
            AddPreviewDimension(x1, x2, dimY, mid);
            AddPreviewDimension(x2, spanEndX, dimY, end2);
            PreviewTexts.Add(new PreviewText($"D{state.DiameterMm}@{state.SpacingEndMm:F0}",
                (spanStartX + x1) / 2 - 26, dimY + 9, "#2596D1", 9));
            if (mid > 0)
                PreviewTexts.Add(new PreviewText($"D{state.DiameterMm}@{state.SpacingMidMm:F0}",
                    (x1 + x2) / 2 - 26, dimY + 9, "#2596D1", 9));
            PreviewTexts.Add(new PreviewText($"D{state.DiameterMm}@{state.SpacingEndMm:F0}",
                (x2 + spanEndX) / 2 - 26, dimY + 9, "#2596D1", 9));
        }

        void AddPreviewDimension(double x0, double x1, double y, double value)
        {
            if (Math.Abs(x1 - x0) <= 1 || value <= 0) return;
            PreviewLines.Add(new PreviewLine(x0, y, x1, y, "#55B7E8", 1.0));
            PreviewLines.Add(new PreviewLine(x0, y - 8, x0, y + 8, "#55B7E8", 1.0));
            PreviewLines.Add(new PreviewLine(x1, y - 8, x1, y + 8, "#55B7E8", 1.0));
            var text = value.ToString("F0");
            var fontSize = Math.Abs(x1 - x0) < 48 ? 9.0 : 10.0;
            PreviewTexts.Add(new PreviewText(text, (x0 + x1) / 2 - EstimateTextWidth(text, fontSize) / 2, y - 18, "#2596D1", fontSize));
        }

        static double EstimateTextWidth(string text, double fontSize) => text.Length * fontSize * 0.56;

        static double ResolvePreviewLength(double manualLength, double ratio, double spanLength)
        {
            if (manualLength > 0) return manualLength;
            if (spanLength <= 0) return 0;
            return Math.Max(0, ratio) * spanLength;
        }

        void DrawMainBarPreview()
        {
            var maxSupport = supports.Count - 1;
            var startPoint = Math.Clamp(MainStartPoint, 0, maxSupport);
            var endPoint = Math.Clamp(MainEndPoint, 0, maxSupport);
            if (endPoint < startPoint) (startPoint, endPoint) = (endPoint, startPoint);
            if (endPoint <= startPoint) endPoint = Math.Min(maxSupport, startPoint + 1);
            if (endPoint <= startPoint) return;

            var isTop = SelectedTab == RebarSettingTab.MainTopBar;
            var barY = isTop ? topY + 10 : bottomY - 10;
            var clearStartX = supports[startPoint] + 22;
            var clearEndX = supports[endPoint] - 22;
            if (clearEndX <= clearStartX) return;

            var leftX = Math.Min(clearEndX, clearStartX + Math.Max(0, AnchorXLeftMm) * scale);
            var rightX = Math.Max(leftX, clearEndX - Math.Max(0, AnchorXRightMm) * scale);
            PreviewLines.Add(new PreviewLine(leftX, barY, rightX, barY, "#FF2222", 2.4));

            if (isTop)
            {
                var leftBend = AnchorLeftMm > 0 ? AnchorLeftMm : TopBendDownLengthMm;
                var rightBend = AnchorRightMm > 0 ? AnchorRightMm : TopBendDownLengthMm;
                if (leftBend > 0)
                    PreviewLines.Add(new PreviewLine(leftX, barY, leftX, Math.Min(columnBottom, barY + leftBend * scale * 0.45), "#FF2222", 2.4));
                if (rightBend > 0)
                    PreviewLines.Add(new PreviewLine(rightX, barY, rightX, Math.Min(columnBottom, barY + rightBend * scale * 0.45), "#FF2222", 2.4));
            }
            else
            {
                var leftBend = Math.Max(0, AnchorLeftMm);
                var rightBend = Math.Max(0, AnchorRightMm);
                if (leftBend > 0)
                    PreviewLines.Add(new PreviewLine(leftX, Math.Max(columnTop, barY - leftBend * scale * 0.45), leftX, barY, "#FF2222", 2.4));
                if (rightBend > 0)
                    PreviewLines.Add(new PreviewLine(rightX, barY, rightX, Math.Max(columnTop, barY - rightBend * scale * 0.45), "#FF2222", 2.4));
            }

            var dimY = isTop ? barY - 34 : barY - 58;
            AddPreviewDimension(clearStartX, leftX, dimY, AnchorXLeftMm);
            AddPreviewDimension(rightX, clearEndX, dimY, AnchorXRightMm);
            if (isTop)
            {
                AddVerticalPreviewDimension(leftX - 18, barY, Math.Min(columnBottom, barY + Math.Max(AnchorLeftMm, TopBendDownLengthMm) * scale * 0.45), Math.Max(AnchorLeftMm, TopBendDownLengthMm));
                AddVerticalPreviewDimension(rightX + 18, barY, Math.Min(columnBottom, barY + Math.Max(AnchorRightMm, TopBendDownLengthMm) * scale * 0.45), Math.Max(AnchorRightMm, TopBendDownLengthMm));
            }
            else
            {
                AddVerticalPreviewDimension(leftX - 18, Math.Max(columnTop, barY - Math.Max(0, AnchorLeftMm) * scale * 0.45), barY, Math.Max(0, AnchorLeftMm));
                AddVerticalPreviewDimension(rightX + 18, Math.Max(columnTop, barY - Math.Max(0, AnchorRightMm) * scale * 0.45), barY, Math.Max(0, AnchorRightMm));
            }
        }

        void AddVerticalPreviewDimension(double x, double y0, double y1, double value)
        {
            if (Math.Abs(y1 - y0) <= 1 || value <= 0) return;
            PreviewLines.Add(new PreviewLine(x, y0, x, y1, "#55B7E8", 1.0));
            PreviewLines.Add(new PreviewLine(x - 8, y0, x + 8, y0, "#55B7E8", 1.0));
            PreviewLines.Add(new PreviewLine(x - 8, y1, x + 8, y1, "#55B7E8", 1.0));
            var text = value.ToString("F0");
            PreviewTexts.Add(new PreviewText(text, x - EstimateTextWidth(text, 9.0) / 2, (y0 + y1) / 2 - 6, "#2596D1", 9.0));
        }
    }

    private static bool IsAttachedToColumn(string value)
        => value.Contains("Attached", StringComparison.OrdinalIgnoreCase)
           || value.Contains("column", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void DeleteSelectedRebar()
    {
        if (SelectedRebarListItem is null)
        {
            StatusMessage = "Hay chon mot dong trong Rebar List de xoa.";
            return;
        }

        switch (SelectedRebarListItem.Key)
        {
            case "main-top":
                _mainTopEnabledState = false;
                break;
            case "main-bottom":
                _mainBottomEnabledState = false;
                break;
            case "add-top-l1":
                _topAddLayer1 = _topAddLayer1 with { Enabled = false };
                break;
            case "add-top-l2":
                _topAddLayer2 = _topAddLayer2 with { Enabled = false };
                break;
            case var key when key.StartsWith("add-top-item-", StringComparison.Ordinal)
                              && TryGetSelectedTopAddItemIndex(out var itemIndex):
                _topAddItems.RemoveAt(itemIndex);
                break;
            case "add-bot-l1":
                _bottomAddLayer1 = _bottomAddLayer1 with { Enabled = false };
                break;
            case "add-bot-l2":
                _bottomAddLayer2 = _bottomAddLayer2 with { Enabled = false };
                break;
            case var key when key.StartsWith("add-bot-item-", StringComparison.Ordinal)
                              && TryGetSelectedBottomAddItemIndex(out var bottomItemIndex):
                _bottomAddItems.RemoveAt(bottomItemIndex);
                break;
            default:
                StatusMessage = "Dong nay chua ho tro xoa.";
                return;
        }

        // Ghi parent trực tiếp cho nhóm vừa xoá, KHÔNG gọi WriteBackToParent (vì nó SaveEditorToSelectedTab
        // sẽ enable lại nhóm từ editor đang hiển thị). Đồng bộ Enabled=false xuống Quick Setting.
        _isSyncingParent = true;
        _parent.TopAdditionalEnabled = _topAddLayer1.Enabled;
        _parent.TopAdditionalLayer2Enabled = _topAddLayer2.Enabled;
        _parent.BottomAdditionalEnabled = _bottomAddLayer1.Enabled;
        _parent.BottomAdditionalLayer2Enabled = _bottomAddLayer2.Enabled;
        _isSyncingParent = false;

        SelectedRebarListItem = null;
        RefreshRebarListFromState();
        RefreshElevationPreview();
    }

    /// <summary>Gom cấu hình từ các form thành QuickSettingModel rồi raise event tạo thép (như Quick Setting).</summary>
    public void ApplyRebar()
    {
        var model = BuildModel();
        var validation = QuickSettingValidator.Validate(model);
        if (!validation.IsValid)
        {
            StatusMessage = "Lỗi cấu hình:\n" + string.Join("\n", validation.Errors);
            return;
        }

        WriteBackToParent(); // đồng bộ ngược về Quick Setting trước khi tạo thép.

        _handler.Model = model;
        _handler.Request = RebarCreationRequest.CreateRebar;
        _handler.OnCompleted = OnRebarCompleted;
        ApplyRequested = true;
        _externalEvent.Raise();
    }

    private void OnRebarCompleted(RebarCreationResult result)
    {
        if (result.Succeeded && result.Warnings.Count > 0)
        {
            StatusMessage = $"Da tao: {result.LongitudinalCount} thanh doc, {result.StirrupCount} dai, {result.AntiBulgeCount} chong phinh."
                            + "\nDu lieu/Canh bao:\n" + string.Join("\n", result.Warnings);
            return;
        }

        StatusMessage = result.Succeeded
            ? $"Đã tạo: {result.LongitudinalCount} thanh dọc, {result.StirrupCount} đai, {result.AntiBulgeCount} chống phình."
            : "Không tạo được thép:\n" + string.Join("\n", result.Warnings);
    }

    private QuickSettingModel BuildModel()
    {
        SaveEditorToSelectedTab();
        EnsureMainBarsFullRun();
        EnsureTopAddItemsFromLegacy();
        EnsureBottomAddItemsFromLegacy();

        // Chiều dài mm lấy từ bảng nhịp: nếu nhịp đầu để Auto → 0 (engine tự tính TCVN per-span); nếu
        // người dùng bỏ tick Auto và nhập tay → dùng giá trị mm đó. (Per-span chi tiết sẽ là bước sau.)
        var firstRow = SpanRows.FirstOrDefault();
        var topMm = firstRow is { TopAuto: false } ? firstRow.TopLengthMm : 0;
        var botMm = firstRow is { BottomAuto: false } ? firstRow.BottomLengthMm : 0;

        AdditionalBarConfig MakeAdd(AddBarState state, int layer, AdditionalBarSide side) => new()
        {
            Enabled = state.Enabled && state.Count > 0,
            Count = state.Count,
            Diameter = new RebarDiameter(state.DiameterMm),
            Layer = layer,
            StartPointIndex = state.StartPoint,
            EndPointIndex = state.EndPoint,
            StartType = state.StartType,
            EndType = state.EndType,
            LeftRatio = state.LeftRatio,
            RightRatio = state.RightRatio,
            LeftLengthMm = state.LeftLengthMm,
            RightLengthMm = state.RightLengthMm,
            DLeftMm = state.DLeftMm,
            DRightMm = state.DRightMm,
            AnchorLeftMm = state.AnchorLeftMm,
            AnchorRightMm = state.AnchorRightMm,
            PositionInSection = state.PositionInSection,
            LengthMm = side == AdditionalBarSide.TopAtSupport ? topMm : botMm,
            EdgeHookDownLengthMm = side == AdditionalBarSide.TopAtSupport ? Math.Max(state.DLeftMm, state.DRightMm) : 0,
            Side = side,
            // Đai C giữ thép gia cường lớp 2 (≥3 cây): chỉ layer 2.
            TieCDiameterMm = layer == 2 ? AddTieCDiameterMm : 0,
            TieCSpacingMm = layer == 2 ? AddTieCSpacingMm : 0
        };

        var hasTopItemList = _topAddItemsMaterialized || _topAddItems.Count > 0;
        var hasBottomItemList = _bottomAddItemsMaterialized || _bottomAddItems.Count > 0;

        return new QuickSettingModel
        {
            MainTop = new MainBarConfig
            {
                Enabled = _mainTopEnabledState,
                Count = _mainTopNumberState, Diameter = new RebarDiameter(_mainTopDiameterMmState),
                AnchorLengthMm = _mainTopAnchorLeftMmState,
                AnchorLeftMm = _mainTopAnchorLeftMmState,
                AnchorRightMm = _mainTopAnchorRightMmState,
                StartPointIndex = _mainTopStartPointState,
                EndPointIndex = _mainTopEndPointState,
                AnchorXLeftMm = _mainTopAnchorXLeftMmState,
                AnchorXRightMm = _mainTopAnchorXRightMmState,
                TopEndBendDownLengthMm = _mainTopBendDownLengthMmState,
                PositionInSection = _mainTopPositionInSectionState
            },
            MainBottom = new MainBarConfig
            {
                Enabled = _mainBottomEnabledState,
                Count = _mainBottomNumberState, Diameter = new RebarDiameter(_mainBottomDiameterMmState),
                AnchorLengthMm = _mainBottomAnchorLeftMmState,
                AnchorLeftMm = _mainBottomAnchorLeftMmState,
                AnchorRightMm = _mainBottomAnchorRightMmState,
                StartPointIndex = _mainBottomStartPointState,
                EndPointIndex = _mainBottomEndPointState,
                AnchorXLeftMm = _mainBottomAnchorXLeftMmState,
                AnchorXRightMm = _mainBottomAnchorXRightMmState,
                PositionInSection = _mainBottomPositionInSectionState
            },
            TopAdditional = hasTopItemList
                ? MakeAdd(_topAddLayer1, 1, AdditionalBarSide.TopAtSupport) with { Enabled = false }
                : MakeAdd(_topAddLayer1, 1, AdditionalBarSide.TopAtSupport),
            TopAdditionalLayer2 = hasTopItemList
                ? MakeAdd(_topAddLayer2, 2, AdditionalBarSide.TopAtSupport) with { Enabled = false }
                : MakeAdd(_topAddLayer2, 2, AdditionalBarSide.TopAtSupport),
            TopAdditionalItems = _topAddItems.Select(i => MakeAdd(i, i.Layer, AdditionalBarSide.TopAtSupport)).ToList(),
            BottomAdditional = hasBottomItemList
                ? MakeAdd(_bottomAddLayer1, 1, AdditionalBarSide.BottomAtMidspan) with { Enabled = false }
                : MakeAdd(_bottomAddLayer1, 1, AdditionalBarSide.BottomAtMidspan),
            BottomAdditionalLayer2 = hasBottomItemList
                ? MakeAdd(_bottomAddLayer2, 2, AdditionalBarSide.BottomAtMidspan) with { Enabled = false }
                : MakeAdd(_bottomAddLayer2, 2, AdditionalBarSide.BottomAtMidspan),
            BottomAdditionalItems = _bottomAddItems.Select(i => MakeAdd(i, i.Layer, AdditionalBarSide.BottomAtMidspan)).ToList(),
            Stirrup = new StirrupConfig
            {
                Diameter = new RebarDiameter(StirrupDiameterMm),
                Mode = StirrupTwoEnds ? StirrupMode.TwoEnds : StirrupMode.Uniform,
                SpacingEndMm = StirrupSpacingEndMm, SpacingMidMm = StirrupSpacingMidMm,
                EndZoneLengthMm = StirrupEndZoneMm,
                FirstDistanceFromSupportMm = StirrupFirstDistanceMm,
                AdditionalStirrups = AdditionalStirrupList
                    .Select(i => new AdditionalStirrupConfig
                    {
                        Diameter = new RebarDiameter(i.DiameterMm),
                        Type = i.Type,
                        StartBar = i.StartBar,
                        EndBar = i.EndBar
                    }).ToList()
            },
            AntiBulge = new AntiBulgeConfig
            {
                Enabled = AntiBulgeNumber > 0, HeightThresholdMm = AntiBulgeHeightThresholdMm,
                Diameter = new RebarDiameter(AntiBulgeDiameterMm),
                Count = AntiBulgeNumber,
                TieDiameter = new RebarDiameter(AntiBulgeTieDiameterMm),
                SpacingMm = AntiBulgeSpacingMm,
                ColumnEmbedMm = AntiBulgeColumnEmbedMm
            },
            Cover = new CoverSettings
            {
                TopMm = _parent.CoverMm,
                BottomMm = _parent.CoverMm,
                SideMm = _parent.CoverMm
            },
            InternalSupportPoints = _parent.SupportPoints,
            InternalSupports = _parent.SupportInfos,
            SecondaryBeams = _parent.SecondaryBeams,
            SpanOverrides = BuildSpanOverrides()
        };
    }

    private IReadOnlyList<SpanRebarOverride> BuildSpanOverrides()
    {
        EnsureStirrupSpanItems();
        return _stirrupSpanItems
            .Where(s => s.Enabled)
            .Select(s => new SpanRebarOverride
            {
                SpanIndex = s.SpanIndex,
                Stirrup = MakeStirrupConfig(s)
            })
            .ToList();
    }
}
