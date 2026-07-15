using Autodesk.Revit.DB;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services.Rebar;

/// <summary>
///     Tạo thép gia cường cắt theo % nhịp: thép trên căn quanh gối (mô men âm), thép dưới căn giữa nhịp
///     (mô men dương). LengthPercent = 0 → chạy suốt nhịp. Dùng lại <see cref="LongitudinalBarCreator"/>
///     để dựng thanh thẳng theo đoạn [startT, endT].
/// </summary>
public sealed class AdditionalBarCreator
{
    private readonly LongitudinalBarCreator _longitudinal;

    public AdditionalBarCreator(LongitudinalBarCreator longitudinal) => _longitudinal = longitudinal;

    /// <summary>
    ///     Tạo một lớp thép gia cường. <paramref name="leftHalfFeet"/>/<paramref name="rightHalfFeet"/> =
    ///     nửa bề rộng cột 2 đầu nhịp → thép bot tính trên L THÔNG THỦY (mép cột → mép cột).
    /// </summary>
    public int Create(Element host, SpanFrame frame, AdditionalBarConfig config, bool atTop,
        double layerOffsetFeet, double leftHalfFeet, double rightHalfFeet, int mainCount, List<string> warnings)
    {
        if (!config.Enabled || config.Count <= 0) return 0;

        return config.Side == AdditionalBarSide.BottomAtMidspan
            ? CreateMidspan(host, frame, config, atTop, layerOffsetFeet, leftHalfFeet, rightHalfFeet, mainCount, warnings)
            : CreateAtSupports(host, frame, config, atTop, layerOffsetFeet, config.LengthPercent / 100.0, mainCount, warnings);
    }

    // Thép dưới giữa nhịp: tính trên L THÔNG THỦY (khoảng hở giữa 2 mép cột). TCVN: từ 1/8 L đến 6/8 L
    // tính TỪ MÉP CỘT TRÁI. config.LengthMm > 0 → đoạn dài LengthMm căn giữa khoảng thông thủy.
    private int CreateMidspan(Element host, SpanFrame frame, AdditionalBarConfig config, bool atTop,
        double layerOffsetFeet, double leftHalfFeet, double rightHalfFeet, int mainCount, List<string> warnings)
    {
        var spanLen = frame.LengthFeet;
        if (spanLen <= 1e-6) return 0;

        // Tham số dọc của 2 mép cột trên span (span đo tim-tim). Mép trái = leftHalf/L; mép phải = 1 - rightHalf/L.
        var clearStartT = leftHalfFeet / spanLen;
        var clearEndT = 1 - rightHalfFeet / spanLen;
        var clearFracOfSpan = clearEndT - clearStartT; // tỉ lệ L thông thủy / span tim-tim.
        if (clearFracOfSpan <= 1e-6) return 0;

        double startT, endT;
        if (config.LeftLengthMm > 0 || config.RightLengthMm > 0 || config.AnchorLeftMm != 0 || config.AnchorRightMm != 0)
        {
            // Ý NGHĨA SỐ TỪ UI (ràng buộc: AnchorLeft = clear/2 − LeftLength):
            //   AnchorLeft = khoảng từ ĐẦU THANH TRÁI đến MÉP CỘT TRÁI (neo thụt vào trong nhịp).
            //   AnchorRight = khoảng từ ĐẦU THANH PHẢI đến MÉP CỘT PHẢI.
            // → Đầu thanh trái = mép cột trái + AnchorLeft; đầu phải = mép cột phải − AnchorRight.
            // (KHÔNG dùng Length trực tiếp ở đây — Length chỉ là hệ quả; dùng Anchor để định 2 đầu cho khớp UI.)
            var anchorLeftFeet = config.AnchorLeftMm / 304.8;
            var anchorRightFeet = config.AnchorRightMm / 304.8;

            var clearStartFeet = clearStartT * spanLen;
            var clearEndFeet = clearEndT * spanLen;

            var startFeet = clearStartFeet + anchorLeftFeet;
            var endFeet = clearEndFeet - anchorRightFeet;

            startT = startFeet / spanLen;
            endT = endFeet / spanLen;
        }
        else if (config.LengthMm > 0)
        {
            // Đoạn dài cố định (mm) căn giữa khoảng thông thủy.
            var fracOfSpan = Math.Min(clearFracOfSpan, config.LengthMm / 304.8 / spanLen);
            var midT = (clearStartT + clearEndT) / 2;
            startT = midT - fracOfSpan / 2;
            endT = midT + fracOfSpan / 2;
        }
        else
        {
            // 2 đầu thép bot CÁCH MÉP CỘT BẰNG NHAU: mỗi đầu lùi 1/8 L thông thủy → thép từ 1/8 đến 7/8,
            // đối xứng giữa khoảng thông thủy (mép cột trái → mép cột phải).
            startT = clearStartT + clearFracOfSpan * (1.0 / 8.0);
            endT = clearStartT + clearFracOfSpan * (7.0 / 8.0);
        }

        return _longitudinal.CreateSegment(host, frame, config.Diameter, config.Count, atTop,
            layerOffsetFeet, startT, endT, config.PositionInSection, mainCount, warnings,
            forceFixedNumberAcrossWidth: config.Layer >= 2);
    }

    // Thép trên quanh gối: hai đoạn dài frac/2 ×L tại mỗi đầu nhịp (đối xứng vào gối đầu và cuối).
    private int CreateAtSupports(Element host, SpanFrame frame, AdditionalBarConfig config, bool atTop,
        double layerOffsetFeet, double frac, int mainCount, List<string> warnings)
    {
        if (frac >= 1.0)
            return _longitudinal.CreateSegment(host, frame, config.Diameter, config.Count, atTop,
                layerOffsetFeet, 0, 1, config.PositionInSection, mainCount, warnings,
                forceFixedNumberAcrossWidth: config.Layer >= 2);

        var half = frac / 2;
        var created = 0;
        created += _longitudinal.CreateSegment(host, frame, config.Diameter, config.Count, atTop,
            layerOffsetFeet, 0, half, config.PositionInSection, mainCount, warnings,
            forceFixedNumberAcrossWidth: config.Layer >= 2);
        created += _longitudinal.CreateSegment(host, frame, config.Diameter, config.Count, atTop,
            layerOffsetFeet, 1 - half, 1, config.PositionInSection, mainCount, warnings,
            forceFixedNumberAcrossWidth: config.Layer >= 2);
        return created;
    }
}
