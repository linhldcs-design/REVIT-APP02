using RevitAPP.Chat.Mcp;
using Xunit;

namespace RevitAPP.Tests.Chat;

public sealed class McpHttpSecurityTests
{
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer wrong")]
    public void IsBearerAuthorized_RejectsMissingOrWrongCredential(string? header) =>
        Assert.False(McpHttpSecurity.IsBearerAuthorized(header, Token));

    [Fact]
    public void IsBearerAuthorized_AcceptsMatchingBearerCredential() =>
        Assert.True(McpHttpSecurity.IsBearerAuthorized($"Bearer {Token}", Token));

    [Fact]
    public void IsBearerAuthorized_RejectsPrefixMatch() =>
        Assert.False(McpHttpSecurity.IsBearerAuthorized($"Bearer {Token}extra", Token));
}
