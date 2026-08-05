namespace RevitAPP.Core.Services;

/// <summary>
/// Continues a grid naming sequence from an anchor grid's own name, so numeric axes
/// (1, 2, 3…) and lettered axes (A, B, C…) each carry on in their own style without
/// the caller having to state which is which.
/// </summary>
public static class CadGridNameSequencer
{
    /// <summary>
    /// Produces <paramref name="count"/> names following <paramref name="anchorName"/>.
    /// Returns null when the anchor name has no recognisable sequence, in which case the
    /// caller must let Revit assign names rather than inventing a wrong scheme.
    /// </summary>
    public static IReadOnlyList<string>? Following(string anchorName, int count)
    {
        if (count <= 0) return Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(anchorName)) return null;

        var trimmed = anchorName.Trim();
        return Numeric(trimmed, count) ?? Alphabetic(trimmed, count);
    }

    /// <summary>
    /// Handles names ending in digits, keeping any prefix: "1" → "2", "X-08" → "X-09".
    /// The digit width is preserved so zero-padded schemes stay aligned.
    /// </summary>
    private static IReadOnlyList<string>? Numeric(string anchorName, int count)
    {
        var digitStart = anchorName.Length;
        while (digitStart > 0 && char.IsDigit(anchorName[digitStart - 1])) digitStart--;
        if (digitStart == anchorName.Length) return null;

        var digits = anchorName.Substring(digitStart);
        if (!long.TryParse(digits, out var value)) return null;

        var prefix = anchorName.Substring(0, digitStart);
        var width = digits.Length;
        var names = new List<string>(count);
        for (var step = 1; step <= count; step++)
            names.Add(prefix + (value + step).ToString().PadLeft(width, '0'));
        return names;
    }

    /// <summary>
    /// Handles pure letter names in spreadsheet order: A → B, Z → AA. Mixed-case anchors
    /// keep the anchor's casing.
    /// </summary>
    private static IReadOnlyList<string>? Alphabetic(string anchorName, int count)
    {
        if (!anchorName.All(char.IsLetter)) return null;

        var isLower = char.IsLower(anchorName[0]);
        var index = 0L;
        foreach (var character in anchorName.ToUpperInvariant())
            index = index * 26 + (character - 'A' + 1);

        var names = new List<string>(count);
        for (var step = 1; step <= count; step++)
        {
            var name = ToAlphabetic(index + step);
            names.Add(isLower ? name.ToLowerInvariant() : name);
        }

        return names;
    }

    private static string ToAlphabetic(long index)
    {
        var characters = new Stack<char>();
        while (index > 0)
        {
            var remainder = (int)((index - 1) % 26);
            characters.Push((char)('A' + remainder));
            index = (index - 1) / 26;
        }

        return new string(characters.ToArray());
    }
}
