using RevitAPP.Core.Models;

namespace RevitAPP.Core.Services;

/// <summary>
///     Tính toán hình học cốt thép cột theo TCVN — thuần (không phụ thuộc Revit API), test được bằng xUnit.
///     Mọi đơn vị là mm. Toạ độ 2D lấy gốc tại tâm tiết diện cột.
/// </summary>
public static class TcvnRebarCalculator
{
    private const double DefaultMinConfineZoneMm = 450d;

    /// <summary>
    ///     4 đỉnh loop thép đai (theo tâm thanh đai), đóng kín, gốc tại tâm tiết diện.
    ///     Kích thước loop = (B − 2·cover − d_đai) × (H − 2·cover − d_đai).
    /// </summary>
    public static IReadOnlyList<Point2D> BuildStirrupLoop(ColumnSection section, double coverMm, double stirrupDiameterMm)
    {
        EnsureRectangular(section);
        if (coverMm < 0) throw new ArgumentException("Lớp bảo vệ không được âm.", nameof(coverMm));
        if (stirrupDiameterMm <= 0) throw new ArgumentException("Đường kính thép đai phải > 0.", nameof(stirrupDiameterMm));

        var loopWidth = section.WidthMm - 2 * coverMm - stirrupDiameterMm;
        var loopHeight = section.HeightMm - 2 * coverMm - stirrupDiameterMm;
        if (loopWidth <= 0 || loopHeight <= 0)
            throw new ArgumentException("Lớp bảo vệ + đường kính đai lớn hơn tiết diện cột — không tạo được đai.");

        var hw = loopWidth / 2d;
        var hh = loopHeight / 2d;

        return new[]
        {
            new Point2D(-hw, -hh),
            new Point2D(hw, -hh),
            new Point2D(hw, hh),
            new Point2D(-hw, hh)
        };
    }

    /// <summary>
    ///     Vị trí tâm các thanh thép chủ phân bố theo chu vi tiết diện chữ nhật.
    ///     BarsX thanh trên mỗi cạnh phương X (trên & dưới), BarsY trên mỗi cạnh phương Y (trái & phải),
    ///     góc dùng chung. Tổng = 2·BarsX + 2·BarsY − 4.
    ///     Inset từ mép bê tông tới tâm thanh = cover + d_đai + d_chủ/2.
    /// </summary>
    public static IReadOnlyList<Point2D> BuildMainBarPositions(ColumnSection section, FloorRebarConfig config, double coverMm)
    {
        EnsureRectangular(section);
        if (config.BarsX < 2) throw new ArgumentException("Số thanh thép chủ phương X phải ≥ 2.", nameof(config));
        if (config.BarsY < 2) throw new ArgumentException("Số thanh thép chủ phương Y phải ≥ 2.", nameof(config));
        if (config.MainBarDiameterMm <= 0) throw new ArgumentException("Đường kính thép chủ phải > 0.", nameof(config));
        if (config.StirrupDiameterMm <= 0) throw new ArgumentException("Đường kính thép đai phải > 0.", nameof(config));

        var inset = coverMm + config.StirrupDiameterMm + config.MainBarDiameterMm / 2d;
        var hw = section.WidthMm / 2d - inset;
        var hh = section.HeightMm / 2d - inset;
        if (hw <= 0 || hh <= 0)
            throw new ArgumentException("Tiết diện quá nhỏ so với lớp bảo vệ + đường kính thép — thép chủ ra ngoài lõi.");

        var points = new List<Point2D>(2 * config.BarsX + 2 * config.BarsY - 4);

        // Cạnh dưới (y = -hh) và cạnh trên (y = +hh): BarsX thanh, kể cả góc.
        for (var i = 0; i < config.BarsX; i++)
        {
            var x = Lerp(-hw, hw, i, config.BarsX);
            points.Add(new Point2D(x, -hh));
            points.Add(new Point2D(x, hh));
        }

        // Cạnh trái (x = -hw) và phải (x = +hw): chỉ các thanh trong (loại 2 góc đã có).
        for (var j = 1; j < config.BarsY - 1; j++)
        {
            var y = Lerp(-hh, hh, j, config.BarsY);
            points.Add(new Point2D(-hw, y));
            points.Add(new Point2D(hw, y));
        }

        return points;
    }

    /// <summary>
    ///     Chia 3 vùng đai theo chiều cao đặt đai (= H_thông_thuỷ − chiều cao dầm, để đai không
    ///     băng qua dầm): chân — thân — đầu. Vùng đầu/chân dài l0 = max(H_đặt/6, max(B,H), 450);
    ///     thân là phần còn lại. Cột thấp (2·l0 ≥ H_đặt) → l0 = H_đặt/2, vùng thân rỗng.
    /// </summary>
    public static StirrupZones ComputeZones(double clearHeightMm, ColumnSection section, FloorRebarConfig config,
        double minConfineZoneMm = DefaultMinConfineZoneMm, double confineClearanceDivisor = 6d)
    {
        EnsureRectangular(section);
        if (clearHeightMm <= 0) throw new ArgumentException("Chiều cao thông thuỷ phải > 0.", nameof(clearHeightMm));
        if (config.SpacingEndMm <= 0) throw new ArgumentException("Khoảng cách đai vùng đầu/chân phải > 0.", nameof(config));
        if (config.SpacingMidMm <= 0) throw new ArgumentException("Khoảng cách đai vùng thân phải > 0.", nameof(config));
        if (config.BeamDepthMm < 0) throw new ArgumentException("Chiều cao dầm không được âm.", nameof(config));
        if (confineClearanceDivisor <= 0) throw new ArgumentException("Hệ số chia vùng gia cường phải > 0.", nameof(confineClearanceDivisor));

        // Cắt vùng đặt đai dưới đáy dầm để thép đai không băng qua dầm.
        var placeHeight = clearHeightMm - config.BeamDepthMm;
        if (placeHeight <= 0)
            throw new ArgumentException("Chiều cao dầm ≥ chiều cao tầng — không còn chỗ đặt đai.", nameof(config));

        var l0 = config.ConfineZoneLenMm > 0
            ? config.ConfineZoneLenMm
            : Math.Max(Math.Max(placeHeight / confineClearanceDivisor, Math.Max(section.WidthMm, section.HeightMm)), minConfineZoneMm);

        // Cột thấp: không đủ chỗ cho 2 vùng gia cường + thân.
        if (2 * l0 >= placeHeight)
            l0 = placeHeight / 2d;

        var midLength = placeHeight - 2 * l0;

        var bottom = new StirrupZone(0d, l0, config.SpacingEndMm, ZoneCount(l0, config.SpacingEndMm));
        var middle = new StirrupZone(l0, midLength, config.SpacingMidMm, ZoneCount(midLength, config.SpacingMidMm));
        var top = new StirrupZone(placeHeight - l0, l0, config.SpacingEndMm, ZoneCount(l0, config.SpacingEndMm));

        return new StirrupZones(bottom, middle, top);
    }

    /// <summary>
    ///     Cao độ đáy/đỉnh của một thanh thép chủ (mm), có xét nối chồng và nối so le 50%.
    ///     Khi <paramref name="stagger"/> bật, 1/2 số thanh (chỉ số lẻ) dịch lên một đoạn
    ///     <paramref name="staggerShiftMm"/> ĐỒNG ĐỀU cho mọi thanh → mối nối gom đúng 2 cao độ
    ///     (mỗi mặt cắt 50% thanh), không bị tán mác dù thép phụ có Ø khác.
    ///     <paramref name="lapMm"/> là chiều dài nối chồng riêng của thanh (theo Ø của nó).
    ///     Thanh tầng dưới cùng bắt đầu tại đáy; thanh tầng trên cùng kết thúc tại đỉnh.
    /// </summary>
    public static (double BottomMm, double TopMm) MainBarSpan(
        double baseMm, double topMm, double lapMm, double staggerShiftMm, double lapZoneOffsetMm,
        bool isBottomStorey, bool isTopStorey, bool stagger, int barIndex, bool staggerBottom = false)
    {
        // lapZoneOffsetMm: nâng mối nối lên cách đáy tầng đoạn L (giữa cột = H/2). Tầng dưới cùng
        // giữ chân tại đáy; tầng trên cùng giữ đỉnh tại đỉnh.
        var shift = stagger && barIndex % 2 == 1 ? staggerShiftMm : 0d;
        // Tầng dưới cùng: chân tại đáy. NHƯNG khi có thép chờ móng (staggerBottom), chân thép chủ
        // tầng 1 cũng phải so le theo cùng parity để mối nối với thép chờ nằm xen kẽ 2 cao độ.
        var bottom = isBottomStorey
            ? baseMm + (staggerBottom ? shift : 0d)
            : baseMm + lapZoneOffsetMm + shift;
        var top = isTopStorey ? topMm : topMm + lapZoneOffsetMm + shift + lapMm;
        return (bottom, top);
    }

    /// <summary>
    ///     Phân loại từng thanh thép chủ: thanh góc (4 góc) luôn dùng đường kính thép chủ;
    ///     thanh giữa cạnh dùng đường kính thép phụ nếu bật <see cref="FloorRebarConfig.UseDistributionBar"/>.
    /// </summary>
    public static IReadOnlyList<PlacedBar> BuildClassifiedMainBars(ColumnSection section, FloorRebarConfig config, double coverMm)
    {
        var positions = BuildMainBarPositions(section, config, coverMm);
        var inset = coverMm + config.StirrupDiameterMm + config.MainBarDiameterMm / 2d;
        var hw = section.WidthMm / 2d - inset;
        var hh = section.HeightMm / 2d - inset;

        var distDia = config.UseDistributionBar && config.DistributionBarDiameterMm > 0
            ? config.DistributionBarDiameterMm
            : config.MainBarDiameterMm;

        var bars = new List<PlacedBar>(positions.Count);
        foreach (var p in positions)
        {
            var isCorner = Math.Abs(Math.Abs(p.Xmm) - hw) < 1e-6 && Math.Abs(Math.Abs(p.Ymm) - hh) < 1e-6;
            bars.Add(new PlacedBar(p, isCorner ? config.MainBarDiameterMm : distDia, isCorner));
        }

        return bars;
    }

    /// <summary>
    ///     Tính chẵn/lẻ "so le" của một thanh theo VỊ TRÍ trên chu vi (kiểu bàn cờ): 2 thanh kề nhau
    ///     trên cùng một cạnh luôn khác parity → nối chồng so le 50/50 trong từng cạnh (chuẩn TCVN).
    ///     Trả về 0 hoặc 1; thanh parity 1 sẽ được dịch mối nối lên một đoạn lap.
    /// </summary>
    public static int StaggerParity(Point2D position, ColumnSection section, FloorRebarConfig config, double coverMm)
    {
        var inset = coverMm + config.StirrupDiameterMm + config.MainBarDiameterMm / 2d;
        var hw = section.WidthMm / 2d - inset;
        var hh = section.HeightMm / 2d - inset;
        var stepX = config.BarsX > 1 ? 2d * hw / (config.BarsX - 1) : 1d;
        var stepY = config.BarsY > 1 ? 2d * hh / (config.BarsY - 1) : 1d;
        var ix = (int)Math.Round((position.Xmm + hw) / stepX);
        var iy = (int)Math.Round((position.Ymm + hh) / stepY);
        return (ix + iy) & 1;
    }

    /// <summary>Diện tích cốt thép dọc As (cm²) dựa trên phân loại thanh góc/giữa.</summary>
    public static double ReinforcementAreaCm2(ColumnSection section, FloorRebarConfig config, double coverMm)
    {
        var bars = BuildClassifiedMainBars(section, config, coverMm);
        var areaMm2 = bars.Sum(b => Math.PI / 4d * b.DiameterMm * b.DiameterMm);
        return areaMm2 / 100d; // mm² → cm²
    }

    /// <summary>Hàm lượng cốt thép μ (%) = As / (B·H) × 100.</summary>
    public static double ReinforcementRatioPercent(ColumnSection section, FloorRebarConfig config, double coverMm)
    {
        EnsureRectangular(section);
        var asMm2 = ReinforcementAreaCm2(section, config, coverMm) * 100d;
        return asMm2 / (section.WidthMm * section.HeightMm) * 100d;
    }

    /// <summary>
    ///     Toạ độ X của các móc chéo (crosstie) — tại các thanh giữa cạnh trên/dưới (không phải góc).
    ///     Mỗi crosstie giằng từ thanh mặt trên xuống thanh mặt dưới cùng X.
    /// </summary>
    public static IReadOnlyList<double> CrosstieXPositions(ColumnSection section, FloorRebarConfig config, double coverMm)
    {
        EnsureRectangular(section);
        var inset = coverMm + config.StirrupDiameterMm + config.MainBarDiameterMm / 2d;
        var hw = section.WidthMm / 2d - inset;
        if (hw <= 0) return Array.Empty<double>();

        var xs = new List<double>();
        for (var i = 1; i < config.BarsX - 1; i++) // bỏ 2 góc
            xs.Add(Lerp(-hw, hw, i, config.BarsX));
        return xs;
    }

    /// <summary>
    ///     Toạ độ Y của các móc chéo phương Y — tại các thanh giữa cạnh trái/phải (không phải góc).
    ///     Mỗi crosstie giằng từ thanh mặt trái sang thanh mặt phải cùng Y.
    /// </summary>
    public static IReadOnlyList<double> CrosstieYPositions(ColumnSection section, FloorRebarConfig config, double coverMm)
    {
        EnsureRectangular(section);
        var inset = coverMm + config.StirrupDiameterMm + config.MainBarDiameterMm / 2d;
        var hh = section.HeightMm / 2d - inset;
        if (hh <= 0) return Array.Empty<double>();

        var ys = new List<double>();
        for (var i = 1; i < config.BarsY - 1; i++) // bỏ 2 góc
            ys.Add(Lerp(-hh, hh, i, config.BarsY));
        return ys;
    }

    /// <summary>
    ///     Điểm cuối móc bẻ 90° tại đỉnh thép chủ — bẻ vào trong lõi theo phương có toạ độ lớn hơn
    ///     (cạnh dài hơn tính từ tâm), đoạn dài <paramref name="hookLenMm"/>.
    /// </summary>
    public static Point2D TopHookLegEnd(Point2D position, double hookLenMm)
    {
        if (Math.Abs(position.Xmm) >= Math.Abs(position.Ymm) && position.Xmm != 0)
            return new Point2D(position.Xmm - Math.Sign(position.Xmm) * hookLenMm, position.Ymm);
        if (position.Ymm != 0)
            return new Point2D(position.Xmm, position.Ymm - Math.Sign(position.Ymm) * hookLenMm);
        return new Point2D(position.Xmm - hookLenMm, position.Ymm); // thanh đúng tâm: bẻ theo −X
    }

    /// <summary>
    ///     Độ lệch ngang lớn nhất e (mm) của thanh thép góc giữa cột dưới và cột trên khi thu tiết diện.
    ///     = chênh lệch vị trí tâm thanh góc theo phương lớn hơn (X hoặc Y).
    /// </summary>
    public static double SectionStepOffsetMm(ColumnSection lower, FloorRebarConfig lowerConfig,
        ColumnSection upper, FloorRebarConfig upperConfig, double coverMm)
    {
        var insetLower = coverMm + lowerConfig.StirrupDiameterMm + lowerConfig.MainBarDiameterMm / 2d;
        var insetUpper = coverMm + upperConfig.StirrupDiameterMm + upperConfig.MainBarDiameterMm / 2d;
        var eX = Math.Abs((lower.WidthMm / 2d - insetLower) - (upper.WidthMm / 2d - insetUpper));
        var eY = Math.Abs((lower.HeightMm / 2d - insetLower) - (upper.HeightMm / 2d - insetUpper));
        return Math.Max(eX, eY);
    }

    /// <summary>
    ///     Phân loại cách nối tại ranh giới cột thu tiết diện theo e và điều kiện uốn.
    ///     <paramref name="sameBarLayout"/> = số thanh X/Y hai tầng giống nhau (để map 1-1 khi uốn vát).
    /// </summary>
    public static SectionTransition ClassifyTransition(double offsetMm, bool sameBarLayout, SectionTransitionOptions options)
    {
        if (offsetMm < 1d) return SectionTransition.None;
        if (sameBarLayout && offsetMm <= options.BendIfOffsetLeMm) return SectionTransition.Crank;
        return SectionTransition.Dowel;
    }

    /// <summary>
    ///     Kẹp vị trí thanh thép vào trong vùng cho phép của tiết diện (cột bóp): |x| ≤ B/2−inset,
    ///     |y| ≤ H/2−inset. Dùng để uốn thanh cột dưới vào nằm trong cột trên nhỏ hơn.
    /// </summary>
    public static Point2D ClampBarIntoSection(Point2D position, ColumnSection upper, FloorRebarConfig upperConfig, double coverMm)
    {
        var inset = coverMm + upperConfig.StirrupDiameterMm + upperConfig.MainBarDiameterMm / 2d;
        var maxX = Math.Max(0, upper.WidthMm / 2d - inset);
        var maxY = Math.Max(0, upper.HeightMm / 2d - inset);
        var x = MathCompat.Clamp(position.Xmm, -maxX, maxX);
        var y = MathCompat.Clamp(position.Ymm, -maxY, maxY);
        return new Point2D(x, y);
    }

    /// <summary>Chiều dài nối chồng L_neo = factor × d_chủ (mặc định 40d).</summary>
    public static double LapLengthMm(double mainBarDiameterMm, double lapFactor)
    {
        if (mainBarDiameterMm <= 0) throw new ArgumentException("Đường kính thép chủ phải > 0.", nameof(mainBarDiameterMm));
        if (lapFactor <= 0) throw new ArgumentException("Hệ số nối chồng phải > 0.", nameof(lapFactor));
        return mainBarDiameterMm * lapFactor;
    }

    private static int ZoneCount(double lengthMm, double spacingMm)
    {
        if (lengthMm <= 0) return 0;
        return (int)Math.Floor(lengthMm / spacingMm) + 1;
    }

    private static double Lerp(double start, double end, int index, int count)
        => count <= 1 ? start : start + (end - start) * index / (count - 1);

    private static void EnsureRectangular(ColumnSection section)
    {
        if (section.Shape != ColumnShape.Rectangular)
            throw new ArgumentException("v1 chỉ hỗ trợ cột chữ nhật/vuông.", nameof(section));
        if (section.WidthMm <= 0 || section.HeightMm <= 0)
            throw new ArgumentException("Kích thước tiết diện phải > 0.", nameof(section));
    }
}
