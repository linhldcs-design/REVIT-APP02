using System.Threading;

namespace RevitAPP.Chat.Mcp;

/// <summary>
/// Correlates one MCP request with exactly one Revit ExternalEvent execution.
/// A timed-out request can only be cancelled before execution starts.
/// </summary>
public sealed class McpRequestCompletion : IDisposable
{
    private const int Pending = 0;
    private const int Running = 1;
    private const int Completed = 2;
    private const int Cancelled = 3;

    private readonly ManualResetEventSlim _done = new(false);
    private int _state;
    private string _result = string.Empty;

    public string Result => _result;
    public bool IsRunning => Volatile.Read(ref _state) == Running;

    public bool TryStart() =>
        Interlocked.CompareExchange(ref _state, Running, Pending) == Pending;

    public bool TryCancel(string result)
    {
        if (Interlocked.CompareExchange(ref _state, Cancelled, Pending) != Pending)
            return false;
        _result = result;
        _done.Set();
        return true;
    }

    public void Complete(string result)
    {
        if (Interlocked.CompareExchange(ref _state, Completed, Running) != Running)
            throw new InvalidOperationException("MCP request is not running.");
        _result = result;
        _done.Set();
    }

    public bool Wait(int timeoutMs) => _done.Wait(timeoutMs);
    public void Wait() => _done.Wait();
    public void Dispose() => _done.Dispose();
}
