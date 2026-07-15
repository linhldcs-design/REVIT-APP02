namespace RevitAPP.Core.Services;

/// <summary>
///     Tính vị trí tag head (hệ crop-local) cho annotation mặt cắt ngang: tất cả cùng X (thẳng hàng phải),
///     Y rải đều theo thứ tự trên→dưới, chừa mép trên/dưới. Học từ view mẫu DK1-15 (MCP):
///     tag X = cropMax.X + 0.886ft; Y trải từ cropMin.Y+margin đến cropMax.Y−margin (margin ~0.427ft).
/// </summary>
public static class CrossTagLayout
{
    /// <summary>Offset X của cột tag so với mép phải crop (feet) — fallback cũ.</summary>
    public const double TagColumnOffsetXFeet = 0.886;

    /// <summary>Offset X của cột tag so với MÉP PHẢI DẦM (feet), khớp đích DK2-1 (head cách mép dầm ~1.378ft).</summary>
    public const double TagColumnOffsetFromBeamFeet = 1.378;

    /// <summary>Tag thép chủ trên nằm cao hơn đỉnh dầm 20 mm.</summary>
    public const double TopTagAboveBeamFeet = 20.0 / 304.8;

    /// <summary>Tag thép chủ dưới nằm thấp hơn đáy dầm 50 mm.</summary>
    public const double BottomTagBelowBeamFeet = 50.0 / 304.8;

    /// <summary>
    ///     Tạo slot Y cân đối theo chiều cao dầm thật: tag đầu neo trên đỉnh 20 mm, tag cuối neo dưới đáy 50 mm,
    ///     các tag giữa chia đều. Kết quả theo thứ tự trên xuống dưới.
    /// </summary>
    public static double[] TagYsFromBeamBounds(int count, double beamTopY, double beamBottomY)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (!MathCompat.IsFinite(beamTopY) || !MathCompat.IsFinite(beamBottomY))
            throw new ArgumentOutOfRangeException(nameof(beamTopY));
        if (beamTopY < beamBottomY) (beamTopY, beamBottomY) = (beamBottomY, beamTopY);

        if (count == 0) return Array.Empty<double>();

        var top = beamTopY + TopTagAboveBeamFeet;
        if (count == 1) return new[] { top };

        var bottom = beamBottomY - BottomTagBelowBeamFeet;
        var step = (top - bottom) / (count - 1);
        var result = new double[count];
        for (var i = 0; i < count; i++) result[i] = top - step * i;
        return result;
    }

    /// <summary>Đệm mép trên & dưới khi rải tag theo chiều cao crop (feet), khớp mẫu.</summary>
    public const double VerticalMarginFeet = 0.427;

    /// <summary>Khoảng cách Y tối thiểu giữa 2 tag liền kề để không chồng (feet), khớp gap đích DK2 (~0.5).</summary>
    public const double MinTagGapFeet = 0.5;

    /// <summary>
    ///     Chống chồng tag: nhận Y thép thật đã sắp GIẢM DẦN (trên→dưới), đẩy giãn để mọi cặp liền kề cách
    ///     nhau ≥ <see cref="MinTagGapFeet"/>, giữ tâm khối không đổi (giãn đều 2 phía). Trả Y đã điều chỉnh
    ///     cùng thứ tự. Leader vẫn thẳng vì lệch nhỏ + Attached.
    /// </summary>
    public static double[] SpreadNoOverlap(IReadOnlyList<double> rebarYsDescending, double minGap = MinTagGapFeet)
    {
        var n = rebarYsDescending.Count;
        var y = rebarYsDescending.ToArray();
        if (n <= 1) return y;

        // Pass 1 (trên→dưới): đẩy mỗi tag xuống nếu quá sát tag trên.
        for (var i = 1; i < n; i++)
            if (y[i - 1] - y[i] < minGap) y[i] = y[i - 1] - minGap;

        // Giữ tâm: dịch cả cụm để trung điểm khớp trung điểm ban đầu (tránh trôi hết xuống dưới).
        var origMid = (rebarYsDescending[0] + rebarYsDescending[n - 1]) * 0.5;
        var newMid = (y[0] + y[n - 1]) * 0.5;
        var shift = origMid - newMid;
        for (var i = 0; i < n; i++) y[i] += shift;

        return y;
    }

    /// <summary>
    ///     Head Y bám Y THÉP thật (leader gọn) nhưng chống chồng + nằm trong crop. Nhận Y thép giảm dần,
    ///     đẩy cặp liền kề cách ≥ minGap, rồi CLAMP toàn cụm vào [cropMinY+margin, cropMaxY−margin]
    ///     (không tụt âm, không vượt đỉnh). Nếu cụm cao hơn vùng khả dụng → rải đều lấp đầy vùng.
    /// </summary>
    public static double[] SpreadClampedToCrop(IReadOnlyList<double> rebarYsDescending,
        double cropMinY, double cropMaxY, double minGap = MinTagGapFeet)
    {
        var n = rebarYsDescending.Count;
        var y = rebarYsDescending.ToArray();
        if (n == 0) return y;

        var top = cropMaxY - VerticalMarginFeet;
        var bottom = cropMinY + VerticalMarginFeet;
        if (n == 1) { y[0] = Math.Min(Math.Max(y[0], bottom), top); return y; }

        // Nếu vùng khả dụng không đủ chứa n tag với minGap → rải đều lấp đầy (không thể bám thép).
        var needed = minGap * (n - 1);
        if (top - bottom <= needed)
        {
            var step = (top - bottom) / (n - 1);
            for (var i = 0; i < n; i++) y[i] = top - step * i;
            return y;
        }

        // Pass xuống: đẩy tag dưới nếu quá sát tag trên.
        for (var i = 1; i < n; i++)
            if (y[i - 1] - y[i] < minGap) y[i] = y[i - 1] - minGap;

        // Clamp trong crop: nếu tràn xuống dưới bottom → dịch cả cụm lên vừa đủ.
        if (y[n - 1] < bottom) { var up = bottom - y[n - 1]; for (var i = 0; i < n; i++) y[i] += up; }
        if (y[0] > top) { var down = y[0] - top; for (var i = 0; i < n; i++) y[i] -= down; }
        return y;
    }

    /// <summary>
    ///     Trả (localX, localY) cho tag thứ <paramref name="index"/> trong tổng <paramref name="count"/> tag,
    ///     xếp trên→dưới. cropMinY/cropMaxX/cropMaxY theo hệ crop-local của view.
    /// </summary>
    public static (double X, double Y) TagHeadLocal(int index, int count, double cropMinY, double cropMaxY, double cropMaxX)
    {
        var x = cropMaxX + TagColumnOffsetXFeet;

        var top = cropMaxY - VerticalMarginFeet;
        var bottom = cropMinY + VerticalMarginFeet;
        if (count <= 1) return (x, (top + bottom) * 0.5);

        var step = (top - bottom) / (count - 1);
        var y = top - step * index; // index 0 = trên cùng
        return (x, y);
    }
}
