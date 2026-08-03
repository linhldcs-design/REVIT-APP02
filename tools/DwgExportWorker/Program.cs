using RevitAPP.Core.Models.DwgExport;
using RevitAPP.Core.Services;
using RevitAPP.Services.DwgExport;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: RevitAPP.DwgExportWorker <job.json> <result.json>");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
var resultPath = Path.GetFullPath(args[1]);
DwgExportJob? job = null;

try
{
    job = DwgExportJobStore.ReadJob(manifestPath);
    Exception? lastError = null;
    for (var attempt = 1; attempt <= 2; attempt++)
    {
        try
        {
            WriteStatus(job, $"attempt|{attempt}|starting");
            var output = AutoCadDwgPostProcessor.Compose(job, TimeSpan.FromMinutes(4));
            DwgExportJobStore.WriteResultAtomic(
                new DwgPostProcessResult(
                    DwgPostProcessResult.CurrentSchemaVersion,
                    job.JobId,
                    true,
                    output,
                    null,
                    Array.Empty<DwgPostProcessSheetResult>()),
                resultPath);
            WriteStatus(job, $"attempt|{attempt}|completed");
            return 0;
        }
        catch (Exception exception) when (attempt == 1 && IsTransient(exception))
        {
            lastError = exception;
            WriteStatus(job, $"attempt|{attempt}|retry|{OneLine(exception.GetBaseException().Message)}");
        }
        catch (Exception exception)
        {
            lastError = exception;
            break;
        }
    }

    throw lastError ?? new InvalidOperationException("DWG worker failed without an error.");
}
catch (Exception exception)
{
    var error = exception.GetBaseException().Message;
    if (job is not null)
    {
        DwgExportJobStore.WriteResultAtomic(
            new DwgPostProcessResult(
                DwgPostProcessResult.CurrentSchemaVersion,
                job.JobId,
                false,
                null,
                error,
                Array.Empty<DwgPostProcessSheetResult>()),
            resultPath);
        WriteStatus(job, $"failed|{OneLine(error)}");
    }
    Console.Error.WriteLine(exception);
    return 1;
}

static bool IsTransient(Exception exception)
{
    for (var current = exception; current is not null; current = current.InnerException!)
    {
        if (current is TimeoutException or System.Runtime.InteropServices.COMException)
            return true;
    }
    return false;
}

static void WriteStatus(DwgExportJob job, string status)
{
    var target = Path.Combine(job.StagingDirectory, AutoCadDwgPostProcessor.ProgressFileName);
    var temporary = target + ".tmp";
    File.WriteAllText(temporary, $"{DateTime.UtcNow:O}|{status}");
    File.Move(temporary, target, true);
}

static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
