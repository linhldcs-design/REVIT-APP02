using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.DwgExport;

public sealed class DwgDimensionAnnotationScriptBuilderTests
{
    [Fact]
    public void Build_BatchesDimensionsBySheetAndDoesNotEmbedUnicodeSourceStyle()
    {
        var script = DwgDimensionAnnotationScriptBuilder.Build(
            new[]
            {
                new DwgSheetDimensionAnnotationPlan(
                    "KC-11-03",
                    75,
                    new[]
                    {
                        new DwgDimensionAnnotationTarget("A10", "Kích thước móng", 1d),
                        new DwgDimensionAnnotationTarget("A11", "Kích thước móng", 1d / 3d)
                    })
            },
            "DONE_123");

        Assert.Contains("(setq ra_scale (ra_set_scale 75))", script);
        Assert.Contains("(handent \"A10\")", script);
        Assert.Contains("(handent \"A11\")", script);
        Assert.Contains("_.ANNOUPDATE", script);
        Assert.Contains("_.-OBJECTSCALE", script);
        Assert.Contains("RA_ANNO_0000", script);
        Assert.Contains("RA_DIMTXT_0000", script);
        Assert.Contains("(ra_unique_name \"STYLE\" \"RA_DIMTXT_0000\")", script);
        Assert.Contains("(ra_unique_name \"DIMSTYLE\" \"RA_ANNO_0000\")", script);
        Assert.Contains("(ra_set_style ra_e ra_ds_0000)", script);
        Assert.Contains("\"_Annotative\" \"_Yes\" \"_No\" \"2.5\" \"0.8\"", script);
        Assert.Contains("(setvar \"USERS5\" \"DONE_123\")", script);
        Assert.DoesNotContain("Kích thước móng", script);
        Assert.Equal(1, Count(script, "(setq ra_scale (ra_set_scale 75))"));
    }

    [Fact]
    public void Build_GroupsSheetsSharingReferenceScaleIntoOneNativeBatch()
    {
        var script = DwgDimensionAnnotationScriptBuilder.Build(
            new[]
            {
                new DwgSheetDimensionAnnotationPlan(
                    "A",
                    75,
                    new[] { new DwgDimensionAnnotationTarget("A10", "Style", 1d) }),
                new DwgSheetDimensionAnnotationPlan(
                    "B",
                    75,
                    new[] { new DwgDimensionAnnotationTarget("B10", "Style", 1d) })
            },
            "DONE");

        Assert.Equal(1, Count(script, "(setq ra_scale (ra_set_scale 75))"));
        Assert.Equal(1, Count(script, "_.ANNOUPDATE"));
        Assert.Equal(1, Count(script, "_.-OBJECTSCALE"));
    }

    [Fact]
    public void Build_DuplicateHandle_Throws()
    {
        var plans = new[]
        {
            new DwgSheetDimensionAnnotationPlan(
                "A",
                75,
                new[] { new DwgDimensionAnnotationTarget("ABC", "Style A", 1d) }),
            new DwgSheetDimensionAnnotationPlan(
                "B",
                25,
                new[] { new DwgDimensionAnnotationTarget("abc", "Style B", 1d) })
        };

        Assert.Throws<ArgumentException>(() =>
            DwgDimensionAnnotationScriptBuilder.Build(plans, "DONE"));
    }

    [Fact]
    public void Build_InvalidScaleOrHandle_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DwgDimensionAnnotationScriptBuilder.Build(
                new[]
                {
                    new DwgSheetDimensionAnnotationPlan(
                        "A",
                        0,
                        Array.Empty<DwgDimensionAnnotationTarget>())
                },
                "DONE"));

        Assert.Throws<ArgumentException>(() =>
            DwgDimensionAnnotationScriptBuilder.Build(
                new[]
                {
                    new DwgSheetDimensionAnnotationPlan(
                        "A",
                        75,
                        new[] { new DwgDimensionAnnotationTarget("NOT-A-HANDLE", "Style", 1d) })
                },
                "DONE"));
    }

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;
}
