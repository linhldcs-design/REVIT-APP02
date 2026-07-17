using System.Diagnostics;
using System.IO;

namespace WallRebar.Services.Rebar;

internal static class WallRebarDiagnostics
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WallRebar", "performance.log");

    public static IDisposable Measure(string operation, string? detail = null)
    {
        Write($"START {operation} {detail}");
        return new Scope(operation);
    }

    public static void Mark(string message) => Write(message);

    private static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:O} | {message}{Environment.NewLine}");
        }
        catch { }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _operation;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public Scope(string operation) => _operation = operation;
        public void Dispose()
        {
            _stopwatch.Stop();
            Write($"END {_operation} | {_stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
