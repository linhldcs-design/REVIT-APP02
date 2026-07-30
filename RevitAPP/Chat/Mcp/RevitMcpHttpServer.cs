using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RevitAPP.Chat.Mcp;
using Serilog;

namespace RevitAPP.Chat.Mcp;

/// <summary>
/// Minimal loopback-only Streamable HTTP transport. Each request is one JSON-RPC POST to /mcp.
/// </summary>
public sealed class RevitMcpHttpServer : IDisposable
{
    public const int DefaultPort = 8765;
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;
    private const int WorkerCount = 4;
    private const int QueueCapacity = 8;

    private readonly McpProtocolDispatcher _dispatcher;
    private readonly int _toolCount;
    private readonly int _port;
    private readonly string _accessToken;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly BlockingCollection<TcpClient> _clients =
        new(new ConcurrentQueue<TcpClient>(), QueueCapacity);
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private Task[] _workers = Array.Empty<Task>();

    public RevitMcpHttpServer(
        McpProtocolDispatcher dispatcher,
        int toolCount,
        string accessToken,
        int port = DefaultPort)
    {
        _dispatcher = dispatcher;
        _toolCount = toolCount;
        _accessToken = string.IsNullOrWhiteSpace(accessToken)
            ? throw new ArgumentException("MCP access token is required.", nameof(accessToken))
            : accessToken;
        _port = port;
    }

    public bool Start()
    {
        if (_listener is not null) return true;
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _workers = Enumerable.Range(0, WorkerCount)
                .Select(_ => Task.Run(WorkerLoop))
                .ToArray();
            _acceptLoop = Task.Run(AcceptLoop);
            Log.Information("RevitAPP MCP listening on http://127.0.0.1:{Port}/mcp with {ToolCount} tools",
                _port, _toolCount);
            return true;
        }
        catch (Exception exception)
        {
            _listener = null;
            Log.Warning(exception, "RevitAPP MCP could not bind loopback port {Port}", _port);
            return false;
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _listener?.Stop(); }
        catch { /* shutdown is best effort */ }
        _clients.CompleteAdding();
        try { _acceptLoop?.Wait(2_000); }
        catch { /* listener stop interrupts blocking accept */ }
        try { Task.WaitAll(_workers, 2_000); }
        catch { /* in-flight Revit requests may finish during process shutdown */ }
        _listener = null;
    }

    private void AcceptLoop()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                var client = _listener!.AcceptTcpClient();
                if (!_clients.TryAdd(client))
                {
                    TryWriteError(client, 503, "MCP server is busy. Retry later.");
                    client.Dispose();
                }
            }
            catch (SocketException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "RevitAPP MCP failed to accept a connection");
            }
        }
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (var client in _clients.GetConsumingEnumerable())
                HandleClient(client);
        }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
        {
            // Revit is shutting down.
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 10_000;
                client.SendTimeout = 30 * 60 * 1000;
                using var stream = client.GetStream();
                var request = ReadRequest(stream);
                if (!IsAllowedOrigin(request.Headers))
                {
                    WriteResponse(stream, 403, "{\"error\":\"Invalid Origin header.\"}");
                    return;
                }

                if (request.Method == "GET" && request.Path == "/health")
                {
                    WriteResponse(stream, 200,
                        $"{{\"status\":\"ok\",\"server\":\"revitapp\",\"toolCount\":{_toolCount}}}");
                    return;
                }

                if (request.Method != "POST" || request.Path is not ("/mcp" or "/mcp/"))
                {
                    WriteResponse(stream, 405, "{\"error\":\"Use POST /mcp.\"}");
                    return;
                }

                request.Headers.TryGetValue("Authorization", out var authorization);
                if (!McpHttpSecurity.IsBearerAuthorized(authorization, _accessToken))
                {
                    WriteResponse(stream, 401, "{\"error\":\"Bearer token required.\"}",
                        new Dictionary<string, string>
                        {
                            ["WWW-Authenticate"] = "Bearer realm=\"RevitAPP MCP\""
                        });
                    return;
                }

                if (!request.Headers.TryGetValue("Content-Type", out var contentType) ||
                    !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(stream, 415, "{\"error\":\"Content-Type must be application/json.\"}");
                    return;
                }

                request.Headers.TryGetValue("MCP-Protocol-Version", out var protocolVersion);
                if (!McpProtocolDispatcher.IsSupportedProtocolVersion(protocolVersion) &&
                    !(string.IsNullOrWhiteSpace(protocolVersion) &&
                      McpProtocolDispatcher.CanOmitProtocolVersionHeader(request.Body)))
                {
                    WriteResponse(stream, 400, "{\"error\":\"Unsupported MCP-Protocol-Version.\"}");
                    return;
                }

                var result = _dispatcher.DispatchAsync(request.Body).GetAwaiter().GetResult();
                WriteResponse(stream, result.StatusCode, result.Body,
                    new Dictionary<string, string>
                    {
                        ["MCP-Protocol-Version"] = McpProtocolDispatcher.CurrentProtocolVersion
                    });
            }
            catch (InvalidDataException exception)
            {
                TryWriteError(client, 400, exception.Message);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "RevitAPP MCP request failed");
                TryWriteError(client, 500, "MCP request failed.");
            }
        }
    }

    private static HttpRequest ReadRequest(NetworkStream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = stream.Read(chunk, 0, chunk.Length);
            if (read <= 0) throw new InvalidDataException("HTTP request ended before headers.");
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxHeaderBytes + MaxBodyBytes)
                throw new InvalidDataException("HTTP request is too large.");
            headerEnd = FindHeaderEnd(buffer.GetBuffer(), (int)buffer.Length);
            if (headerEnd < 0 && buffer.Length > MaxHeaderBytes)
                throw new InvalidDataException("HTTP headers are too large.");
        }

        var bytes = buffer.ToArray();
        var headerText = Encoding.ASCII.GetString(bytes, 0, headerEnd);
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) throw new InvalidDataException("Invalid HTTP request line.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var name = line[..separator].Trim();
            if (headers.ContainsKey(name))
                throw new InvalidDataException($"Duplicate HTTP header: {name}.");
            headers[name] = line[(separator + 1)..].Trim();
        }

        var contentLength = headers.TryGetValue("Content-Length", out var value) &&
                            int.TryParse(value, out var parsed)
            ? parsed
            : 0;
        if (contentLength < 0 || contentLength > MaxBodyBytes)
            throw new InvalidDataException("HTTP body is too large.");

        var bodyOffset = headerEnd + 4;
        var body = new byte[contentLength];
        var bufferedBody = Math.Min(contentLength, bytes.Length - bodyOffset);
        if (bufferedBody > 0) Array.Copy(bytes, bodyOffset, body, 0, bufferedBody);
        var received = bufferedBody;
        while (received < contentLength)
        {
            var read = stream.Read(body, received, contentLength - received);
            if (read <= 0) throw new InvalidDataException("HTTP body ended early.");
            received += read;
        }

        return new HttpRequest(
            requestLine[0].ToUpperInvariant(),
            requestLine[1].Split('?')[0],
            headers,
            Encoding.UTF8.GetString(body));
    }

    private static int FindHeaderEnd(byte[] bytes, int length)
    {
        for (var index = 0; index <= length - 4; index++)
            if (bytes[index] == 13 && bytes[index + 1] == 10 &&
                bytes[index + 2] == 13 && bytes[index + 3] == 10)
                return index;
        return -1;
    }

    private static bool IsAllowedOrigin(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Origin", out var origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static void WriteResponse(
        NetworkStream stream,
        int statusCode,
        string? body,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var bodyBytes = body is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);
        var headerText =
            $"HTTP/1.1 {statusCode} {Reason(statusCode)}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n";
        if (extraHeaders is not null)
            foreach (var pair in extraHeaders)
                headerText += $"{pair.Key}: {pair.Value}\r\n";
        var headers = Encoding.ASCII.GetBytes(headerText + "\r\n");
        stream.Write(headers, 0, headers.Length);
        if (bodyBytes.Length > 0) stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }

    private static void TryWriteError(TcpClient client, int statusCode, string message)
    {
        try
        {
            if (client.Connected)
                WriteResponse(client.GetStream(), statusCode,
                    $"{{\"error\":{Newtonsoft.Json.JsonConvert.SerializeObject(message)}}}");
        }
        catch { /* the client may already be disconnected */ }
    }

    private static string Reason(int statusCode) => statusCode switch
    {
        200 => "OK",
        202 => "Accepted",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        415 => "Unsupported Media Type",
        503 => "Service Unavailable",
        _ => "Internal Server Error"
    };

    private sealed record HttpRequest(
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Headers,
        string Body);
}
