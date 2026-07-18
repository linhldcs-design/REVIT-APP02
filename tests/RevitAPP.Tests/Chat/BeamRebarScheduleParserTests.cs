using RevitAPP.Chat.Services;
using Xunit;

namespace RevitAPP.Tests.Chat;

public sealed class BeamRebarScheduleParserTests
{
    [Fact]
    public void Parse_MapsVietnameseBeamScheduleWithoutGuessing()
    {
        var headers = new[]
        {
            "ĐỎ MARK", "THÉP CHỦ TRÊN", "THÉP CHỦ DƯỚI", "THÉP TĂNG CƯỜNG GỐI",
            "THÉP TĂNG CƯỜNG NHỊP", "THÉP ĐAI"
        };
        IReadOnlyList<IReadOnlyList<object?>> rows = new[]
        {
            new object?[]
            {
                "DK1", "3D16 ( BẺ MÓC XUỐNG = CHIỀU CAO DẦM - 100 )", "3D16",
                "2D16 ( LAYER 2. HAI ĐẦU BIÊN BẺ MÓC XUỐNG = CHIỀU CAO DẦM - 100)",
                "2D16 ( LAYER 2)", "D6a100/200"
            },
            new object?[] { "DK2", "3D16 ( BẺ MÓC XUỐNG = CHIỀU CAO DẦM - 100 )", "3D16", "Không có", "Không có", "D6a100/200" }
        };

        var parsed = BeamRebarScheduleParser.Parse(headers, rows);

        var dk1 = Assert.Single(parsed, row => row.Mark == "DK1");
        Assert.Equal(3, dk1.MainTop.Count);
        Assert.Equal(16, dk1.MainTop.DiameterMm);
        Assert.Equal(100, dk1.MainTop.BendDownFromHeightMinusMm);
        Assert.True(dk1.Support.Enabled);
        Assert.Equal(2, dk1.Support.Layer);
        Assert.Equal(100, dk1.Support.BendDownFromHeightMinusMm);
        Assert.True(dk1.Midspan.Enabled);
        Assert.Equal(2, dk1.Midspan.Layer);
        Assert.Equal(6, dk1.Stirrup.DiameterMm);
        Assert.Equal(100, dk1.Stirrup.EndSpacingMm);
        Assert.Equal(200, dk1.Stirrup.MidSpacingMm);

        var dk2 = Assert.Single(parsed, row => row.Mark == "DK2");
        Assert.False(dk2.Support.Enabled);
        Assert.False(dk2.Midspan.Enabled);
    }

    [Fact]
    public void Parse_AcceptsMaintopcountHeaderAlias()
    {
        var headers = new[] { "Mark", "Maintopcount", "THÉP CHỦ DƯỚI", "THÉP TĂNG CƯỜNG GÓI", "THÉP TĂNG CƯỜNG NHỊP", "THÉP ĐAI" };
        IReadOnlyList<IReadOnlyList<object?>> rows = new[]
        {
            new object?[] { "DK1", "3D16 (BẺ MÓC XUỐNG = CHIỀU CAO DẦM - 100)", "3D16", "Không có", "Không có", "D6a100/200" }
        };

        var row = Assert.Single(BeamRebarScheduleParser.Parse(headers, rows));

        Assert.Equal(3, row.MainTop.Count);
        Assert.Equal(16, row.MainTop.DiameterMm);
    }
}
