using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadSlabTypeNamingTests
{
    [Theory]
    [InlineData("Concrete 150mm", 200, "Concrete 200mm")]
    [InlineData("Concrete 150 mm", 200, "Concrete 200mm")]
    [InlineData("Sàn bê tông", 120, "Sàn bê tông 120mm")]
    [InlineData("160mm Concrete With 50mm Metal Deck (210 mm)", 120,
        "Concrete With Metal Deck 120mm")]
    public void ForThickness_NamesTheCopyAfterWhatItIsMadeOf(
        string seedName, int thicknessMm, string expected)
    {
        Assert.Equal(expected, CadSlabTypeNaming.ForThickness(seedName, thicknessMm));
    }

    [Fact]
    public void ForThickness_CopyingTwice_DoesNotStackThicknesses()
    {
        // The name a copy is given has to survive being copied again, or a floor imported twice
        // ends up called "Concrete 150mm 200mm 250mm".
        var once = CadSlabTypeNaming.ForThickness("Concrete 150mm", 200);
        var twice = CadSlabTypeNaming.ForThickness(once, 250);

        Assert.Equal("Concrete 250mm", twice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("200mm")]
    public void ForThickness_WithNothingLeftToNameItAfter_StillNamesTheThickness(string seedName)
    {
        Assert.Equal("Sàn 120mm", CadSlabTypeNaming.ForThickness(seedName, 120));
    }

    [Theory]
    [InlineData("Concrete 150mm", "Concrete")]
    [InlineData("Concrete (150 mm)", "Concrete")]
    [InlineData("Concrete", "Concrete")]
    [InlineData("Generic 12\"", "Generic 12\"")]
    public void Stem_TakesOffAThicknessAndLeavesTheRest(string name, string expected)
    {
        Assert.Equal(expected, CadSlabTypeNaming.Stem(name));
    }

    [Fact]
    public void Stem_LeavesANumberThatIsNotAThickness()
    {
        // A type may carry a number that says nothing about its thickness.
        Assert.Equal("Floor Type 3", CadSlabTypeNaming.Stem("Floor Type 3"));
    }
}
