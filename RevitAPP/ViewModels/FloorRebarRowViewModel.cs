using CommunityToolkit.Mvvm.ComponentModel;
using RevitAPP.Core.Models;
using RevitAPP.Core.Services;

namespace RevitAPP.ViewModels;

/// <summary>ViewModel cho một dòng (một tầng cột) trong bảng cấu hình thép.</summary>
public sealed partial class FloorRebarRowViewModel : ObservableObject
{
    private readonly ColumnStorey _storey;

    public FloorRebarRowViewModel(ColumnStorey storey, RebarBarTypeOption mainBar, RebarBarTypeOption stirrup,
        double autoBeamDepthMm = 0)
    {
        _storey = storey;
        _mainBarType = mainBar;
        _stirrupType = stirrup;
        _distributionBarType = stirrup; // mặc định = đai (đường kính nhỏ)
        _beamDepthMm = Math.Round(autoBeamDepthMm); // tự dò per-tầng, làm tròn mm
    }

    public ColumnStorey Storey => _storey;
    public string LevelName => _storey.LevelName;
    public string SectionLabel => $"{_storey.Section.WidthMm:0}×{_storey.Section.HeightMm:0}";

    /// <summary>Nhãn chỉ đọc chiều cao dầm tự dò (mm); 0 = không có dầm.</summary>
    public string BeamDepthLabel => BeamDepthMm > 0 ? $"{BeamDepthMm:0} mm" : "(không có dầm)";

    [ObservableProperty] private RebarBarTypeOption _mainBarType;
    [ObservableProperty] private int _barsX = 3;
    [ObservableProperty] private int _barsY = 3;
    [ObservableProperty] private RebarBarTypeOption _stirrupType;
    [ObservableProperty] private double _spacingEndMm = 100;
    [ObservableProperty] private double _spacingMidMm = 200;
    [ObservableProperty] private double _confineZoneLenMm;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BeamDepthLabel))]
    private double _beamDepthMm;
    [ObservableProperty] private bool _useDistributionBar;
    [ObservableProperty] private RebarBarTypeOption _distributionBarType;
    [ObservableProperty] private SectionStirrupType _stirrupSectionType = SectionStirrupType.ClosedTie;
    [ObservableProperty] private string? _error;

    public FloorRebarConfig ToConfig() => new(
        MainBarType.DiameterMm, BarsX, BarsY, StirrupType.DiameterMm,
        SpacingEndMm, SpacingMidMm, ConfineZoneLenMm, BeamDepthMm,
        UseDistributionBar, DistributionBarType.DiameterMm, StirrupSectionType);

    /// <summary>Dry-run qua calculator để bắt lỗi hình học; trả về true nếu hợp lệ.</summary>
    public bool Validate(double coverMm)
    {
        try
        {
            var config = ToConfig();
            TcvnRebarCalculator.BuildStirrupLoop(_storey.Section, coverMm, config.StirrupDiameterMm);
            TcvnRebarCalculator.BuildMainBarPositions(_storey.Section, config, coverMm);
            TcvnRebarCalculator.ComputeZones(_storey.ClearHeightMm, _storey.Section, config);
            Error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return false;
        }
    }

    public void CopyFrom(FloorRebarRowViewModel source)
    {
        MainBarType = source.MainBarType;
        BarsX = source.BarsX;
        BarsY = source.BarsY;
        StirrupType = source.StirrupType;
        SpacingEndMm = source.SpacingEndMm;
        SpacingMidMm = source.SpacingMidMm;
        ConfineZoneLenMm = source.ConfineZoneLenMm;
        // KHÔNG copy BeamDepthMm — mỗi tầng giữ chiều cao dầm tự dò riêng.
        UseDistributionBar = source.UseDistributionBar;
        DistributionBarType = source.DistributionBarType;
        StirrupSectionType = source.StirrupSectionType;
    }
}
