using System.Text.RegularExpressions;

namespace RevitAPP.Core.Services;

/// <summary>
/// Names the floor types the slab import creates.
///
/// A type is copied from the one the user chose and given the thickness the plan states, so its
/// name has to say both: what it is made of, and how thick it is.
/// </summary>
public static class CadSlabTypeNaming
{
    // A thickness already written into the name: "150mm", "150 mm", or a bare number in brackets
    // as Revit writes it for its own types.
    private static readonly Regex ThicknessRegex = new(
        @"\s*\(?\s*\d{2,4}\s*mm\s*\)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex EmptyBracketsRegex = new(
        @"\s*\(\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The name for a type copied from <paramref name="seedName"/> at a new thickness. Any
    /// thickness already in the seed's name is taken off first, so copying "Concrete 150mm" at
    /// 200 mm gives "Concrete 200mm" rather than "Concrete 150mm 200mm".
    /// </summary>
    public static string ForThickness(string seedName, int thicknessMm)
    {
        var stem = Stem(seedName);
        return stem.Length > 0 ? $"{stem} {thicknessMm}mm" : $"Sàn {thicknessMm}mm";
    }

    /// <summary>
    /// What a type is called with the thickness taken out of its name.
    /// </summary>
    public static string Stem(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        // A thickness taken out of the middle of a name leaves the words on either side touching,
        // so it goes out as a space and the run of spaces is closed up afterwards.
        var trimmed = ThicknessRegex.Replace(name, " ");
        trimmed = EmptyBracketsRegex.Replace(trimmed, string.Empty);
        return string.Join(" ", trimmed.Split(
            new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
