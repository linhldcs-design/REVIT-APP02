using System.IO;
using System.Security.Cryptography;

namespace RevitAPP.Chat.Mcp;

/// <summary>Loads or creates the 256-bit bearer token used by the loopback MCP endpoint.</summary>
internal static class McpAccessTokenStore
{
    private const int TokenBytes = 32;
    private const int MinimumTokenCharacters = 32;
    private const string EnvironmentVariable = "REVITAPP_MCP_TOKEN";

    public static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitAPP",
        "mcp-access-token.txt");

    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim();
        if (configured is { Length: > 0 })
            return Validate(configured, EnvironmentVariable);

        if (File.Exists(TokenPath))
            return Validate(File.ReadAllText(TokenPath).Trim(), TokenPath);

        var directory = Path.GetDirectoryName(TokenPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var bytes = new byte[TokenBytes];
        using (var generator = RandomNumberGenerator.Create())
            generator.GetBytes(bytes);
        var token = BitConverter.ToString(bytes).Replace("-", string.Empty);
        File.WriteAllText(TokenPath, token);
        return token;
    }

    private static string Validate(string token, string source)
    {
        if (token.Length < MinimumTokenCharacters)
            throw new InvalidOperationException(
                $"MCP token from '{source}' must contain at least {MinimumTokenCharacters} characters.");
        return token;
    }
}
