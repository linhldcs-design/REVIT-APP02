using RevitAPP.Chat.Mcp;
using Xunit;

namespace RevitAPP.Tests.Chat;

public sealed class McpRequestCompletionTests
{
    [Fact]
    public void CancelledPendingRequest_CannotStartLater()
    {
        using var request = new McpRequestCompletion();

        Assert.True(request.TryCancel("timeout"));
        Assert.False(request.TryStart());
        Assert.True(request.Wait(0));
        Assert.Equal("timeout", request.Result);
    }

    [Fact]
    public void RunningRequest_CannotBeCancelledAndKeepsItsOwnResult()
    {
        using var first = new McpRequestCompletion();
        using var second = new McpRequestCompletion();

        Assert.True(first.TryStart());
        Assert.False(first.TryCancel("timeout"));
        Assert.True(second.TryStart());

        second.Complete("second");
        first.Complete("first");

        Assert.Equal("first", first.Result);
        Assert.Equal("second", second.Result);
    }
}
