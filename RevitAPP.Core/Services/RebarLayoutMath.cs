namespace RevitAPP.Core.Services;

/// <summary>
/// Nhân bản thanh thép theo đúng ngữ nghĩa hai kiểu layout của Revit. Builder chỉ dựng MỘT thanh gốc
/// rồi để Revit rải ra cả bó; preview không có Revit nên phải tự rải. Đây là điểm chân lý duy nhất
/// cho phép tính đó — cả hai phía dùng chung để preview đếm đúng số thanh và đúng bước như mô hình thật.
/// </summary>
public static class RebarLayoutMath
{
    /// <summary>Đoạn ngắn hơn giá trị này coi như bằng 0 (mm).</summary>
    private const double LengthEpsilonMm = 1e-6;

    /// <summary>
    /// Vị trí các thanh khi rải "bước tối đa": phủ HẾT đoạn <paramref name="arrayLengthMm"/> với bước
    /// thực ≤ <paramref name="spacingMm"/>, chia đều, có thanh ở cả hai đầu.
    /// Bước thực co lại cho chia chẵn — ví dụ đoạn 1000 bước 300 cho 5 thanh cách nhau 250, chứ không
    /// phải 4 thanh cách nhau 300 rồi hở 100 ở cuối.
    /// </summary>
    /// <returns>Khoảng cách từ đầu đoạn (mm), tăng dần, phần tử đầu luôn 0.</returns>
    public static IReadOnlyList<double> MaximumSpacingStations(double arrayLengthMm, double spacingMm)
    {
        if (double.IsNaN(spacingMm) || spacingMm <= 0)
            throw new ArgumentException("Bước rải phải là số dương.", nameof(spacingMm));
        if (double.IsNaN(arrayLengthMm) || arrayLengthMm <= LengthEpsilonMm)
            return [];

        // Trừ hao epsilon để đoạn chia chẵn không bị làm tròn lên thành dư một khoảng.
        var intervals = (int)Math.Ceiling(arrayLengthMm / spacingMm - 1e-9);
        if (intervals < 1) intervals = 1;

        var stations = new double[intervals + 1];
        for (var i = 0; i <= intervals; i++)
            stations[i] = i * arrayLengthMm / intervals;
        return stations;
    }

    /// <summary>
    /// Vị trí các thanh khi rải "số lượng cố định": đúng <paramref name="count"/> thanh, đều nhau,
    /// thanh đầu tại 0 và thanh cuối tại <paramref name="arrayLengthMm"/>.
    /// Một thanh đơn nằm tại 0 — khớp cách builder đặt thanh gốc vào giữa tiết diện khi chỉ có 1 cây.
    /// </summary>
    /// <returns>Khoảng cách từ thanh gốc (mm), tăng dần.</returns>
    public static IReadOnlyList<double> FixedNumberOffsets(int count, double arrayLengthMm)
    {
        if (count <= 0) return [];
        if (count == 1) return [0d];
        if (double.IsNaN(arrayLengthMm) || arrayLengthMm <= LengthEpsilonMm)
            return Enumerable.Repeat(0d, count).ToArray();

        var offsets = new double[count];
        for (var i = 0; i < count; i++)
            offsets[i] = i * arrayLengthMm / (count - 1);
        return offsets;
    }
}
