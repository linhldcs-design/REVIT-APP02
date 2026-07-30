using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Mcp;
using RevitAPP.Chat.Models;
using Xunit;

namespace RevitAPP.Tests.Chat;

public sealed class McpProtocolDispatcherTests
{
    private static McpProtocolDispatcher CreateDispatcher(Func<string, JObject, Task<string>>? execute = null)
    {
        var schema = new ToolSchema("get_view", "Read the active view.", new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject()
        });
        var tools = new[]
        {
            new McpToolDescriptor(schema, ReadOnly: true, Destructive: false, Idempotent: true, OpenWorld: false)
        };
        return new McpProtocolDispatcher(
            tools,
            execute ?? ((_, _) => Task.FromResult("{\"success\":true,\"view\":\"Level 1\"}")),
            "1.3.2");
    }

    [Fact]
    public async Task Initialize_StableClient_ReturnsToolCapability()
    {
        var request = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25"}}
            """;

        var result = await CreateDispatcher().DispatchAsync(request);
        var body = JObject.Parse(result.Body!);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("2025-11-25", (string?)body["result"]?["protocolVersion"]);
        Assert.NotNull(body["result"]?["capabilities"]?["tools"]);
    }

    [Fact]
    public async Task Initialize_UnsupportedVersion_FallsBackToStableVersion()
    {
        var result = await CreateDispatcher().DispatchAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-07-28"}}""");
        var body = JObject.Parse(result.Body!);

        Assert.Equal(McpProtocolDispatcher.CurrentProtocolVersion,
            (string?)body["result"]?["protocolVersion"]);
    }

    [Fact]
    public async Task ToolsList_UsesNeutralSchemaAndAnnotations()
    {
        var result = await CreateDispatcher().DispatchAsync(
            """{"jsonrpc":"2.0","id":"tools","method":"tools/list","params":{}}""");
        var tool = JObject.Parse(result.Body!)["result"]?["tools"]?[0];

        Assert.Equal("get_view", (string?)tool?["name"]);
        Assert.Equal("object", (string?)tool?["inputSchema"]?["type"]);
        Assert.True((bool?)tool?["annotations"]?["readOnlyHint"]);
        Assert.True((bool?)tool?["annotations"]?["idempotentHint"]);
    }

    [Fact]
    public async Task ToolsCall_ReturnsTextAndStructuredContent()
    {
        var result = await CreateDispatcher().DispatchAsync(
            """
            {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_view","arguments":{}}}
            """);
        var body = JObject.Parse(result.Body!);

        Assert.False((bool?)body["result"]?["isError"]);
        Assert.Equal("Level 1", (string?)body["result"]?["structuredContent"]?["view"]);
        Assert.Contains("\"success\":true", (string?)body["result"]?["content"]?[0]?["text"]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"selection\"")]
    [InlineData("42")]
    public async Task ToolsCall_NonObjectArguments_ReturnsInvalidParams(string arguments)
    {
        var executed = false;
        var dispatcher = CreateDispatcher((_, _) =>
        {
            executed = true;
            return Task.FromResult("""{"success":true}""");
        });

        var request = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["method"] = "tools/call",
            ["params"] = new JObject
            {
                ["name"] = "get_view",
                ["arguments"] = JToken.Parse(arguments)
            }
        };
        var result = await dispatcher.DispatchAsync(request.ToString());
        var body = JObject.Parse(result.Body!);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(-32602, (int?)body["error"]?["code"]);
        Assert.False(executed);
    }

    [Fact]
    public async Task ToolsCall_MissingArguments_UsesEmptyObject()
    {
        JObject? received = null;
        var dispatcher = CreateDispatcher((_, input) =>
        {
            received = input;
            return Task.FromResult("""{"success":true}""");
        });

        var result = await dispatcher.DispatchAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_view"}}""");

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(received);
        Assert.Empty(received);
    }

    [Fact]
    public async Task Notification_ReturnsAcceptedWithoutBody()
    {
        var result = await CreateDispatcher().DispatchAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Equal(202, result.StatusCode);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task ServerDiscover_AdvertisesOnlyImplementedVersions()
    {
        var result = await CreateDispatcher().DispatchAsync(
            """{"jsonrpc":"2.0","id":3,"method":"server/discover","params":{}}""");
        var root = JObject.Parse(result.Body!);
        var versions = ((JArray)root["result"]!["supportedVersions"]!)
            .Values<string>().Where(value => value is not null).Cast<string>().ToArray();

        Assert.Contains(McpProtocolDispatcher.CurrentProtocolVersion, versions);
        Assert.Single(versions);
        Assert.DoesNotContain("2026-07-28", versions);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-03-26", false)]
    [InlineData("2026-07-28", false)]
    public void ProtocolVersionHeader_IsValidated(string? version, bool expected) =>
        Assert.Equal(expected, McpProtocolDispatcher.IsSupportedProtocolVersion(version));

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""", true)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"server/discover"}""", true)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", false)]
    [InlineData("not-json", false)]
    public void ProtocolVersionHeader_CanOnlyBeOmittedForHandshake(string request, bool expected) =>
        Assert.Equal(expected, McpProtocolDispatcher.CanOmitProtocolVersionHeader(request));
}
