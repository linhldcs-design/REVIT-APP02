using RevitAPP.Chat.Services;
using Xunit;

namespace RevitAPP.Tests.Chat;

public sealed class ExcelWorkbookReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RevitAPP-ExcelTests-" + Guid.NewGuid().ToString("N"));

    public ExcelWorkbookReaderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Csv_InspectAndRead_PreservesHeadersNumbersAndRows()
    {
        var path = Path.Combine(_directory, "foundations.csv");
        File.WriteAllText(path, "Mark,X,Y,Level\r\nM1,1200.5,3500,MONG\r\nM2,2400,5100,MONG\r\n");

        var sheet = Assert.Single(ExcelWorkbookReader.Inspect(path));
        Assert.Equal(4, sheet.ColumnCount);

        var table = ExcelWorkbookReader.Read(path, null, 1, 2, 100, 100);
        Assert.Equal(new[] { "Mark", "X", "Y", "Level" }, table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("M1", table.Rows[0][0]);
        Assert.Equal(1200.5, Convert.ToDouble(table.Rows[0][1]));
    }

    [Fact]
    public void Find_ReturnsOnlySupportedSpreadsheetFiles()
    {
        File.WriteAllText(Path.Combine(_directory, "data.csv"), "A\r\n1");
        File.WriteAllText(Path.Combine(_directory, "ignore.txt"), "x");

        var files = ExcelWorkbookReader.Find(_directory, false, 50);

        Assert.Single(files);
        Assert.EndsWith("data.csv", files[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
