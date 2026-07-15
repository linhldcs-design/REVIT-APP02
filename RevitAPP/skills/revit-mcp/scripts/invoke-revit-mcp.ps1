param(
    [string]$HostName = "127.0.0.1",
    [int]$Port = 8080,
    [string]$Method = "get_current_view_info",
    [string]$ParamsJson = "{}",
    [string]$CodeFile
)

$client = [System.Net.Sockets.TcpClient]::new()
$client.Connect($HostName, $Port)
$stream = $client.GetStream()

try {
    $params = $ParamsJson | ConvertFrom-Json
    if ($CodeFile) {
        $params = @{
            code = Get-Content -Path $CodeFile -Raw
            parameters = @()
        }
        $Method = "send_code_to_revit"
    }

    $payloadObj = @{
        jsonrpc = "2.0"
        method = $Method
        params = $params
        id = "codex-" + [Guid]::NewGuid().ToString("N")
    }

    $payload = $payloadObj | ConvertTo-Json -Depth 20 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $stream.Write($bytes, 0, $bytes.Length)

    $buffer = New-Object byte[] 131072
    $stream.ReadTimeout = 120000
    $chunks = New-Object System.Collections.Generic.List[byte]

    do {
        $count = $stream.Read($buffer, 0, $buffer.Length)
        if ($count -gt 0) {
            for ($i = 0; $i -lt $count; $i++) {
                $chunks.Add($buffer[$i])
            }
        }
    } while ($stream.DataAvailable)

    [System.Text.Encoding]::UTF8.GetString($chunks.ToArray())
}
finally {
    $client.Close()
}
