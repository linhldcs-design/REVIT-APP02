using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RevitAPP.Chat.Services;

public sealed record BeamScheduleBar(int Count, int DiameterMm, double BendDownFromHeightMinusMm = 0);
public sealed record BeamScheduleAdditional(bool Enabled, int Count = 0, int DiameterMm = 0, int Layer = 1,
    double BendDownFromHeightMinusMm = 0);
public sealed record BeamScheduleStirrup(int DiameterMm, double EndSpacingMm, double MidSpacingMm);
public sealed record BeamRebarScheduleRow(string Mark, BeamScheduleBar MainTop, BeamScheduleBar MainBottom,
    BeamScheduleAdditional Support, BeamScheduleAdditional Midspan, BeamScheduleStirrup Stirrup);

/// <summary>Parser xác định cho bảng thống kê thép dầm tiếng Việt; không để LLM tự suy diễn ký hiệu kỹ thuật.</summary>
public static class BeamRebarScheduleParser
{
    private static readonly Regex BarPattern = new(@"(?<count>\d+)\s*[DØΦ]\s*(?<diameter>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LayerPattern = new(@"LAYER\s*(?<layer>[12])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HeightMinusPattern = new(@"(?:CHIEU\s*CAO\s*DAM|\bH\b)\s*-\s*(?<value>\d+(?:[.,]\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StirrupPattern = new(@"[DØΦ]\s*(?<diameter>\d+)\s*[A@]\s*(?<end>\d+(?:[.,]\d+)?)\s*/\s*(?<mid>\d+(?:[.,]\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<BeamRebarScheduleRow> Parse(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var columns = headers.Select((header, index) => (Name: Normalize(header), Index: index)).ToList();
        var markColumn = Find(columns, name => name.Contains("MARK"));
        var mainTopColumn = Find(columns, name => name.Contains("THEP CHU TREN") || name.Contains("MAINTOPCOUNT"));
        var mainBottomColumn = Find(columns, name => name.Contains("THEP CHU DUOI"));
        var supportColumn = Find(columns, name => name.Contains("TANG CUONG GOI"));
        var midspanColumn = Find(columns, name => name.Contains("TANG CUONG NHIP"));
        var stirrupColumn = Find(columns, name => name.Contains("THEP DAI"));

        var result = new List<BeamRebarScheduleRow>();
        foreach (var row in rows)
        {
            var mark = Cell(row, markColumn).Trim();
            if (string.IsNullOrWhiteSpace(mark)) continue;
            result.Add(new BeamRebarScheduleRow(
                mark,
                ParseMain(Cell(row, mainTopColumn), "thép chủ trên", allowBend: true),
                ParseMain(Cell(row, mainBottomColumn), "thép chủ dưới", allowBend: false),
                ParseAdditional(Cell(row, supportColumn)),
                ParseAdditional(Cell(row, midspanColumn)),
                ParseStirrup(Cell(row, stirrupColumn))));
        }

        return result;
    }

    private static int Find(IEnumerable<(string Name, int Index)> columns, Func<string, bool> predicate)
    {
        var match = columns.FirstOrDefault(column => predicate(column.Name));
        if (match.Name is null) throw new FormatException("Bảng Excel thiếu cột bắt buộc hoặc tên tiêu đề không đúng mẫu.");
        return match.Index;
    }

    private static BeamScheduleBar ParseMain(string value, string label, bool allowBend)
    {
        var match = BarPattern.Match(Normalize(value));
        if (!match.Success) throw new FormatException($"Không đọc được {label}: '{value}'.");
        return new BeamScheduleBar(Int(match, "count"), Int(match, "diameter"),
            allowBend ? ParseHeightMinus(value) : 0);
    }

    private static BeamScheduleAdditional ParseAdditional(string value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("KHONG CO"))
            return new BeamScheduleAdditional(false);
        var match = BarPattern.Match(normalized);
        if (!match.Success) throw new FormatException($"Không đọc được thép tăng cường: '{value}'.");
        var layerMatch = LayerPattern.Match(normalized);
        var layer = layerMatch.Success ? Int(layerMatch, "layer") : 1;
        return new BeamScheduleAdditional(true, Int(match, "count"), Int(match, "diameter"), layer,
            ParseHeightMinus(value));
    }

    private static BeamScheduleStirrup ParseStirrup(string value)
    {
        var match = StirrupPattern.Match(Normalize(value));
        if (!match.Success) throw new FormatException($"Không đọc được thép đai: '{value}'. Mẫu đúng: D6a100/200.");
        return new BeamScheduleStirrup(Int(match, "diameter"), Number(match, "end"), Number(match, "mid"));
    }

    private static double ParseHeightMinus(string value)
    {
        var match = HeightMinusPattern.Match(Normalize(value));
        return match.Success ? Number(match, "value") : 0;
    }

    private static int Int(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    private static double Number(Match match, string group) =>
        double.Parse(match.Groups[group].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
    private static string Cell(IReadOnlyList<object?> row, int index) =>
        index < row.Count ? Convert.ToString(row[index], CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;

    private static string Normalize(string value)
    {
        var decomposed = value.Replace('Đ', 'D').Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(character));
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }
}
