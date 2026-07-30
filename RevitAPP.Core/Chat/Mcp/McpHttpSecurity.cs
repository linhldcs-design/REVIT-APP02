using System.Text;

namespace RevitAPP.Chat.Mcp;

/// <summary>Pure helpers for authenticating the loopback MCP HTTP transport.</summary>
public static class McpHttpSecurity
{
    private const string BearerPrefix = "Bearer ";

    public static bool IsBearerAuthorized(string? authorizationHeader, string expectedToken)
    {
        if (authorizationHeader is null ||
            string.IsNullOrWhiteSpace(authorizationHeader) ||
            string.IsNullOrWhiteSpace(expectedToken) ||
            !authorizationHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = authorizationHeader[BearerPrefix.Length..].Trim();
        return FixedTimeEquals(candidate, expectedToken);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        var difference = leftBytes.Length ^ rightBytes.Length;
        var count = Math.Max(leftBytes.Length, rightBytes.Length);
        for (var index = 0; index < count; index++)
        {
            var leftByte = index < leftBytes.Length ? leftBytes[index] : 0;
            var rightByte = index < rightBytes.Length ? rightBytes[index] : 0;
            difference |= leftByte ^ rightByte;
        }
        return difference == 0;
    }
}
