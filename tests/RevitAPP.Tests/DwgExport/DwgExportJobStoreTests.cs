using RevitAPP.Core.Models.DwgExport;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.DwgExport;

public sealed class DwgExportJobStoreTests
{
    [Fact]
    public void Job_RoundTrip_PreservesSingleOutputContract()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "job.json");
        try
        {
            var job = Job(root);
            DwgExportJobStore.WriteJobAtomic(job, path);

            var restored = DwgExportJobStore.ReadJob(path);

            Assert.Equal(job.JobId, restored.JobId);
            Assert.Equal(job.RequestedOutputPath, restored.RequestedOutputPath);
            Assert.Equal(2, restored.Sheets.Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Job_StagedPathTraversal_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var job = Job(root) with
        {
            Sheets = new[] { Job(root).Sheets[0] with { StagedFileName = "..\\outside.dwg" } }
        };

        Assert.Throws<InvalidDataException>(() => DwgExportJobStore.ValidateJob(job));
    }

    [Fact]
    public void Result_WrongJobId_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "result.json");
        try
        {
            var result = new DwgPostProcessResult(
                DwgPostProcessResult.CurrentSchemaVersion,
                Guid.NewGuid().ToString("N"),
                true,
                Path.Combine(root, "output.tmp.dwg"),
                null,
                Array.Empty<DwgPostProcessSheetResult>());
            DwgExportJobStore.WriteResultAtomic(result, path);

            Assert.Throws<InvalidDataException>(
                () => DwgExportJobStore.ReadResult(path, Guid.NewGuid().ToString("N")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static DwgExportJob Job(string staging) =>
        new(
            DwgExportJob.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            "Sample.rvt",
            "FullKC",
            DwgFileVersion.R2007,
            DwgDrawingUnit.Millimetres,
            staging,
            Path.Combine(staging, "..", "FullKC.dwg"),
            100,
            new[]
            {
                Sheet(0, "S-01", "0000-S-01.dwg"),
                Sheet(1, "S-02", "0001-S-02.dwg")
            });

    private static DwgSheetPlan Sheet(int ordinal, string number, string fileName) =>
        new(
            ordinal,
            ordinal + 1,
            number,
            "Sheet",
            fileName,
            new[] { new DwgViewportPlan(10 + ordinal, 20 + ordinal, "Plan", 50, 0, 0, 0) });
}
