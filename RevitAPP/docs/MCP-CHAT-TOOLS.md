# RevitAPP MCP — Chat AI tools

RevitAPP exposes the same tool registry through two surfaces:

- **Chat AI** remains available from the `LDL-STRUCTURAL` ribbon.
- **MCP** is an additional loopback endpoint for external AI clients.

No tool is moved out of Chat AI. Tool names, descriptions, JSON schemas, license checks,
transaction ownership, and Revit main-thread execution are shared.

## Endpoint

Start Revit with RevitAPP loaded, then connect the MCP client to:

```text
http://127.0.0.1:8765/mcp
```

Every `/mcp` request requires a bearer token. RevitAPP creates a 256-bit token on first start at:

```text
%LOCALAPPDATA%\RevitAPP\mcp-access-token.txt
```

The token can be overridden before Revit starts with `REVITAPP_MCP_TOKEN` (minimum 32 characters).
Never commit or share this token.

Health check:

```powershell
Invoke-RestMethod http://127.0.0.1:8765/health
```

The port can be changed before Revit starts:

```powershell
$env:REVITAPP_MCP_PORT = "8766"
```

## Client configuration

Use a Streamable HTTP MCP server entry. The exact outer configuration object depends on the client:

```json
{
  "mcpServers": {
    "revitapp": {
      "type": "streamable-http",
      "url": "http://127.0.0.1:8765/mcp",
      "headers": {
        "Authorization": "Bearer <contents of mcp-access-token.txt>",
        "MCP-Protocol-Version": "2025-11-25"
      }
    }
  }
}
```

The endpoint implements stable MCP `2025-11-25`. Tool discovery is dynamic: `tools/list` reads the
current `ChatToolRegistry`, so Chat AI and MCP cannot drift into two different tool lists.

## Manual protocol checks

Initialize a legacy client:

```powershell
$body = @{
  jsonrpc = "2.0"
  id = 1
  method = "initialize"
  params = @{
    protocolVersion = "2025-11-25"
    capabilities = @{}
    clientInfo = @{ name = "manual-test"; version = "1.0.0" }
  }
} | ConvertTo-Json -Depth 8
$token = Get-Content "$env:LOCALAPPDATA\RevitAPP\mcp-access-token.txt" -Raw

Invoke-RestMethod `
  -Method Post `
  -Uri http://127.0.0.1:8765/mcp `
  -Headers @{
    Authorization = "Bearer $($token.Trim())"
    "MCP-Protocol-Version" = "2025-11-25"
  } `
  -ContentType application/json `
  -Body $body
```

List all tools:

```powershell
$body = @{
  jsonrpc = "2.0"
  id = 2
  method = "tools/list"
  params = @{}
} | ConvertTo-Json -Depth 8

Invoke-RestMethod `
  -Method Post `
  -Uri http://127.0.0.1:8765/mcp `
  -Headers @{
    Authorization = "Bearer $($token.Trim())"
    "MCP-Protocol-Version" = "2025-11-25"
  } `
  -ContentType application/json `
  -Body $body
```

## Safety and execution

- The server binds only to `127.0.0.1`; it is not exposed to the LAN.
- All MCP calls require the local bearer token and use constant-time token comparison.
- Browser `Origin` headers are validated to reduce DNS-rebinding risk.
- Request headers and bodies have fixed size limits.
- Four workers process at most eight queued connections; overload returns HTTP 503.
- Revit API tools are serialized and marshalled through the same `ExternalEvent` bridge as Chat AI.
- Every non-read-only MCP tool shows a Yes/No dialog inside Revit before execution.
- Each Revit request owns a correlated completion state; timeout cannot overwrite a later request.
- Existing license gates and per-tool transaction ownership remain active.
- A single MCP tool response is capped at 25,000 characters; use tool filters or `limit` when available.

If Revit is closed, the MCP endpoint is unavailable by design.
