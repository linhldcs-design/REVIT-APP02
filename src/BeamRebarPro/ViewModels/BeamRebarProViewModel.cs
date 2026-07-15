using Autodesk.Revit.UI;
using BeamRebarPro.Models;
using BeamRebarPro.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BeamRebarPro.ViewModels;

/// <summary>
///     ViewModel for the modeless Quick Setting dialog.
/// </summary>
public sealed partial class BeamRebarProViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly ExternalEvent _externalEvent;
    private readonly RebarCreationHandler _handler;
    private IReadOnlyList<Point3> _supportPoints = [];
    private IReadOnlyList<SupportInfo> _supportInfos = [];
    private IReadOnlyList<SecondaryBeamInfo> _secondaryBeams = [];

    public event Action? RequestClose;

    public BeamRebarProViewModel(ILogger logger)
    {
        _logger = logger;
        _handler = new RebarCreationHandler
        {
            OnCompleted = OnRebarCompleted,
            OnSupportsSelected = OnSupportsSelected
        };
        _externalEvent = ExternalEvent.Create(_handler);
    }

    [ObservableProperty] private int _mainTopCount = 3;
    [ObservableProperty] private int _mainTopDiameterMm = 16;
    [ObservableProperty] private int _mainBottomCount = 3;
    [ObservableProperty] private int _mainBottomDiameterMm = 16;
    [ObservableProperty] private double _mainAnchorLengthMm;
    [ObservableProperty] private double _mainTopBendDownLengthMm;
    [ObservableProperty] private bool _mainHookEnabled;

    [ObservableProperty] private bool _topAdditionalEnabled;
    [ObservableProperty] private int _topAdditionalCount = 2;
    [ObservableProperty] private int _topAdditionalDiameterMm = 16;
    [ObservableProperty] private double _topAdditionalPercent = 30;
    [ObservableProperty] private double _topAdditionalEdgeHookDownLengthMm = 300;
    [ObservableProperty] private bool _topAdditionalLayer2Enabled;
    [ObservableProperty] private int _topAdditionalLayer2Count = 2;
    [ObservableProperty] private int _topAdditionalLayer2DiameterMm = 20;
    [ObservableProperty] private double _topAdditionalLayer2Percent = 30;
    [ObservableProperty] private bool _bottomAdditionalEnabled;
    [ObservableProperty] private int _bottomAdditionalCount = 2;
    [ObservableProperty] private int _bottomAdditionalDiameterMm = 16;
    [ObservableProperty] private double _bottomAdditionalPercent = 60;
    [ObservableProperty] private bool _bottomAdditionalLayer2Enabled;
    [ObservableProperty] private int _bottomAdditionalLayer2Count = 2;
    [ObservableProperty] private int _bottomAdditionalLayer2DiameterMm = 20;
    [ObservableProperty] private double _bottomAdditionalLayer2Percent = 60;

    [ObservableProperty] private int _stirrupDiameterMm = 6;
    [ObservableProperty] private bool _stirrupTwoEnds = true;
    [ObservableProperty] private double _stirrupSpacingEndMm = 150;
    [ObservableProperty] private double _stirrupSpacingMidMm = 200;
    [ObservableProperty] private double _stirrupFirstDistanceMm = 50;

    [ObservableProperty] private bool _antiBulgeEnabled;
    [ObservableProperty] private int _antiBulgeDiameterMm = 12;
    [ObservableProperty] private double _antiBulgeColumnEmbedMm;
    [ObservableProperty] private double _coverMm = 25;

    [ObservableProperty] private bool _createDrawingSheetEnabled;
    [ObservableProperty] private string _sheetNumber = string.Empty;
    [ObservableProperty] private string _sheetName = string.Empty;
    [ObservableProperty] private string _viewTemplateName = "BS-A1-25-BEAM-DX";
    [ObservableProperty] private int _supportBeamCount;
    [ObservableProperty] private int _secondaryBeamCount;

    [ObservableProperty] private string _statusMessage = "Cau hinh Quick Setting, sau do tao thep va chon dam trong Revit.";

    private IReadOnlyList<Autodesk.Revit.DB.FamilyInstance>? _pickedBeams;

    /// <summary>Nhịp đã đọc từ dầm chọn lúc bấm Ribbon (để hiện thông số / dùng cho Detail).</summary>
    public IReadOnlyList<Models.SpanInfo> PickedSpans { get; private set; } = [];

    public IReadOnlyList<Point3> SupportPoints => _supportPoints;

    public IReadOnlyList<SupportInfo> SupportInfos => _supportInfos;

    public IReadOnlyList<SecondaryBeamInfo> SecondaryBeams => _secondaryBeams;

    /// <summary>Cấu hình ĐẦY ĐỦ đã lưu của dầm (theo ID) — để Detail load lại mọi field (anchor X/Y,
    ///     position, start/end point, items...). null nếu dầm chưa từng cấu hình.</summary>
    public QuickSettingModel? SavedConfig { get; private set; }

    /// <summary>Command gọi lúc bấm Ribbon đã chọn dầm: lưu dầm + nhịp, dùng lại khi tạo thép.</summary>
    public void SetPickedBeams(IReadOnlyList<Autodesk.Revit.DB.FamilyInstance> beams, IReadOnlyList<Models.SpanInfo> spans)
    {
        _pickedBeams = beams;
        PickedSpans = spans;
        _handler.PreselectedBeams = beams;
        StatusMessage = $"Đã chọn dầm: {spans.Count} nhịp. Cấu hình rồi nhấn 'Tạo thép'.";
    }

    public IReadOnlyList<RebarDiameter> DiameterOptions => RebarDiameter.Standard;
    public IReadOnlyList<int> DiameterMmOptions { get; } = RebarDiameter.Standard.Select(d => d.Millimeters).ToList();
    public IReadOnlyList<int> CountOptions { get; } = Enumerable.Range(0, 11).ToList();
    public IReadOnlyList<string> ViewTemplateOptions { get; } = ["BS-A1-25-BEAM-DX"];

    public string SupportBeamSummary => $"{SupportBeamCount} Beams work like support are selected!";
    public string SecondaryBeamSummary => $"{SecondaryBeamCount} secondary beams are selected!";

    /// <summary>Đai đều suốt nhịp (Uniform) — nghịch đảo của <see cref="StirrupTwoEnds"/>. Hai radio
    ///     A1 trong UI loại trừ nhau: chọn cái này tự bỏ cái kia.</summary>
    public bool StirrupUniform
    {
        get => !StirrupTwoEnds;
        set => StirrupTwoEnds = !value;
    }

    partial void OnStirrupTwoEndsChanged(bool value) => OnPropertyChanged(nameof(StirrupUniform));

    partial void OnSupportBeamCountChanged(int value) => OnPropertyChanged(nameof(SupportBeamSummary));

    partial void OnSecondaryBeamCountChanged(int value) => OnPropertyChanged(nameof(SecondaryBeamSummary));

    [RelayCommand]
    private void CreateRebar()
    {
        var model = BuildModel();
        var validation = QuickSettingValidator.Validate(model);
        if (!validation.IsValid)
        {
            StatusMessage = "Loi cau hinh:\n" + string.Join("\n", validation.Errors);
            return;
        }

        _handler.Model = model;
        _handler.Request = RebarCreationRequest.CreateRebar;
        StatusMessage = "Hay chon dam trong Revit...";
        _externalEvent.Raise();
    }

    [RelayCommand]
    private void SelectSupportBeams()
    {
        _handler.Request = RebarCreationRequest.SelectSupports;
        _handler.OnBeamInfo = OnSpansRecomputed; // nhận nhịp tính lại sau khi thêm gối.
        StatusMessage = "Hay chon cot/goi nam tren tuyen dam trong Revit...";
        _externalEvent.Raise();
    }

    /// <summary>Cập nhật danh sách nhịp sau khi thêm gối thủ công (mục 9) → Detail + preview thấy nhịp mới.</summary>
    private void OnSpansRecomputed(IReadOnlyList<SpanInfo> spans)
    {
        PickedSpans = spans;
        OnPropertyChanged(nameof(PickedSpans));
    }

    [RelayCommand]
    private void SelectSecondaryBeams()
    {
        _handler.Request = RebarCreationRequest.SelectSecondary;
        _handler.OnSecondarySelected = OnSecondarySelected;
        StatusMessage = "Hay chon cac dam phu gac len dam chinh trong Revit...";
        _externalEvent.Raise();
    }

    private void OnSecondarySelected(IReadOnlyList<SecondaryBeamInfo> beams)
    {
        _secondaryBeams = beams;
        SecondaryBeamCount = beams.Count;
        StatusMessage = beams.Count == 0
            ? "Chua chon dam phu nao."
            : $"Da chon {beams.Count} dam phu. Dai dam chinh se tranh + tang cuong quanh vi tri nay.";
    }

    [RelayCommand]
    private void MultiBeams()
    {
        StatusMessage = "Multi beams: co the chon nhieu dam; neu 1 dam dai qua nhieu cot, hay chon cot/goi o muc 9 truoc.";
    }

    [RelayCommand]
    private void MoreSettings()
    {
        StatusMessage = "More setting: dung phan detail span overrides de chinh tung nhip.";
    }

    [RelayCommand]
    private void GoToDetailRebarForms()
    {
        // Tái dùng handler + ExternalEvent + tham chiếu chính VM này để thông số liên lạc thật giữa
        // Quick Setting và Detail (đọc giá trị hiện tại khi mở, ghi ngược khi tạo thép).
        var detailVm = new BeamRebarDetailViewModel(this, _handler, _externalEvent);
        var view = new Views.BeamRebarDetailView(detailVm)
        {
            Owner = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
        };
        // Khi Detail đóng, khôi phục callback của Quick Setting để handler không trỏ nhầm.
        view.Closed += (_, _) => _handler.OnCompleted = OnRebarCompleted;
        view.Show();
    }

    private void OnSupportsSelected(IReadOnlyList<SupportInfo> supports)
    {
        _supportInfos = supports;
        _supportPoints = supports.Select(s => s.Location).ToList();
        SupportBeamCount = supports.Count;
        StatusMessage = supports.Count == 0
            ? "Chua chon goi/cot nao. Mot dam dai tam thoi duoc xem la 1 nhip."
            : $"Da chon {supports.Count} goi/cot noi bo. Mot dam dai se duoc chia thanh {supports.Count + 1} nhip.";
    }

    private void OnRebarCompleted(RebarCreationResult result)
    {
        if (!result.Succeeded)
        {
            StatusMessage = "Khong tao duoc thep:\n" + string.Join("\n", result.Warnings);
            _logger.Warning("BeamRebarPro create rebar failed: {Warnings}", string.Join("; ", result.Warnings));
            return;
        }

        var msg = $"Da tao: {result.LongitudinalCount} thanh doc, {result.StirrupCount} dai, " +
                  $"{result.AntiBulgeCount} thep chong phinh.";
        if (result.Warnings.Count > 0)
            msg += "\nCanh bao:\n" + string.Join("\n", result.Warnings);
        StatusMessage = msg;
        RequestClose?.Invoke();
    }

    private QuickSettingModel BuildModel()
    {
        var hook = new HookConfig { Enabled = MainHookEnabled, Angle = HookAngle.Deg135 };

        var model = new QuickSettingModel
        {
            MainTop = new MainBarConfig
            {
                Count = MainTopCount, Diameter = new RebarDiameter(MainTopDiameterMm),
                AnchorLengthMm = MainAnchorLengthMm, TopEndBendDownLengthMm = MainTopBendDownLengthMm,
                HookStart = hook, HookEnd = hook
            },
            MainBottom = new MainBarConfig
            {
                Count = MainBottomCount, Diameter = new RebarDiameter(MainBottomDiameterMm),
                AnchorLengthMm = MainAnchorLengthMm, HookStart = hook, HookEnd = hook
            },
            TopAdditional = new AdditionalBarConfig
            {
                Enabled = TopAdditionalEnabled, Count = TopAdditionalCount,
                Diameter = new RebarDiameter(TopAdditionalDiameterMm),
                LengthPercent = TopAdditionalPercent,
                EdgeHookDownLengthMm = TopAdditionalEdgeHookDownLengthMm,
                Side = AdditionalBarSide.TopAtSupport
            },
            TopAdditionalLayer2 = new AdditionalBarConfig
            {
                Enabled = TopAdditionalLayer2Enabled, Count = TopAdditionalLayer2Count,
                Diameter = new RebarDiameter(TopAdditionalLayer2DiameterMm),
                Layer = 2, LengthPercent = TopAdditionalLayer2Percent,
                EdgeHookDownLengthMm = TopAdditionalEdgeHookDownLengthMm,
                Side = AdditionalBarSide.TopAtSupport
            },
            BottomAdditional = new AdditionalBarConfig
            {
                Enabled = BottomAdditionalEnabled, Count = BottomAdditionalCount,
                Diameter = new RebarDiameter(BottomAdditionalDiameterMm),
                LengthPercent = BottomAdditionalPercent, Side = AdditionalBarSide.BottomAtMidspan
            },
            BottomAdditionalLayer2 = new AdditionalBarConfig
            {
                Enabled = BottomAdditionalLayer2Enabled, Count = BottomAdditionalLayer2Count,
                Diameter = new RebarDiameter(BottomAdditionalLayer2DiameterMm),
                Layer = 2, LengthPercent = BottomAdditionalLayer2Percent, Side = AdditionalBarSide.BottomAtMidspan
            },
            Stirrup = new StirrupConfig
            {
                Diameter = new RebarDiameter(StirrupDiameterMm),
                Mode = StirrupTwoEnds ? StirrupMode.TwoEnds : StirrupMode.Uniform,
                SpacingEndMm = StirrupSpacingEndMm, SpacingMidMm = StirrupSpacingMidMm,
                FirstDistanceFromSupportMm = StirrupFirstDistanceMm
            },
            AntiBulge = new AntiBulgeConfig
            {
                Enabled = AntiBulgeEnabled,
                Diameter = new RebarDiameter(AntiBulgeDiameterMm),
                Count = 2,
                TieDiameter = new RebarDiameter(6),
                SpacingMm = 500,
                ColumnEmbedMm = AntiBulgeColumnEmbedMm
            },
            Cover = new CoverSettings { TopMm = CoverMm, BottomMm = CoverMm, SideMm = CoverMm },
            InternalSupportPoints = _supportPoints,
            InternalSupports = _supportInfos,
            SecondaryBeams = _secondaryBeams
        };

        return model;
    }

    /// <summary>Đổ cấu hình đã lưu (theo ID dầm) vào các field UI để khỏi nhập lại khi pick lại dầm cũ.</summary>
    public void LoadSavedConfig(QuickSettingModel m)
    {
        SavedConfig = m; // giữ model đầy đủ để Detail load mọi field.
        MainTopCount = m.MainTop.Count;
        MainTopDiameterMm = m.MainTop.Diameter.Millimeters;
        MainAnchorLengthMm = m.MainTop.AnchorLengthMm;
        MainTopBendDownLengthMm = m.MainTop.TopEndBendDownLengthMm;
        MainBottomCount = m.MainBottom.Count;
        MainBottomDiameterMm = m.MainBottom.Diameter.Millimeters;

        TopAdditionalEnabled = m.TopAdditional.Enabled;
        TopAdditionalCount = m.TopAdditional.Count;
        TopAdditionalDiameterMm = m.TopAdditional.Diameter.Millimeters;
        TopAdditionalPercent = m.TopAdditional.LengthPercent;
        TopAdditionalEdgeHookDownLengthMm = m.TopAdditional.EdgeHookDownLengthMm;
        TopAdditionalLayer2Enabled = m.TopAdditionalLayer2.Enabled;
        TopAdditionalLayer2Count = m.TopAdditionalLayer2.Count;
        TopAdditionalLayer2DiameterMm = m.TopAdditionalLayer2.Diameter.Millimeters;
        TopAdditionalLayer2Percent = m.TopAdditionalLayer2.LengthPercent;

        BottomAdditionalEnabled = m.BottomAdditional.Enabled;
        BottomAdditionalCount = m.BottomAdditional.Count;
        BottomAdditionalDiameterMm = m.BottomAdditional.Diameter.Millimeters;
        BottomAdditionalPercent = m.BottomAdditional.LengthPercent;
        BottomAdditionalLayer2Enabled = m.BottomAdditionalLayer2.Enabled;
        BottomAdditionalLayer2Count = m.BottomAdditionalLayer2.Count;
        BottomAdditionalLayer2DiameterMm = m.BottomAdditionalLayer2.Diameter.Millimeters;
        BottomAdditionalLayer2Percent = m.BottomAdditionalLayer2.LengthPercent;

        StirrupDiameterMm = m.Stirrup.Diameter.Millimeters;
        StirrupTwoEnds = m.Stirrup.Mode == StirrupMode.TwoEnds;
        StirrupSpacingEndMm = m.Stirrup.SpacingEndMm;
        StirrupSpacingMidMm = m.Stirrup.SpacingMidMm;
        StirrupFirstDistanceMm = m.Stirrup.FirstDistanceFromSupportMm;

        AntiBulgeEnabled = m.AntiBulge.Enabled;
        AntiBulgeDiameterMm = m.AntiBulge.Diameter.Millimeters;
        AntiBulgeColumnEmbedMm = m.AntiBulge.ColumnEmbedMm;
        CoverMm = m.Cover.TopMm;

        StatusMessage = "Đã tải lại cấu hình đã lưu của dầm này. Có thể chỉnh rồi tạo thép.";
    }
}
