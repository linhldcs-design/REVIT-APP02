param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$Secret = [Environment]::GetEnvironmentVariable('REVITAPP_LICENSE_SHARED_SECRET')
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Secret)) {
    throw 'Repository secret REVITAPP_LICENSE_SHARED_SECRET is required.'
}

# The production secret is a 64-character Base64URL token. Reject BOM,
# whitespace, control characters, and accidental encoding artifacts.
if ($Secret -cnotmatch '^[A-Za-z0-9_-]{64}\z') {
    throw 'REVITAPP_LICENSE_SHARED_SECRET must be exactly 64 Base64URL characters with no BOM or whitespace.'
}

$literal = ConvertTo-Json $Secret -Compress
$source = @"
namespace RevitAPP.Licensing;
internal static class ReleaseLicenseSecrets
{
    internal const string SharedSecret = $literal;
}
"@

$fullPath = [IO.Path]::GetFullPath($OutputPath)
$directory = [IO.Path]::GetDirectoryName($fullPath)
if (-not [string]::IsNullOrEmpty($directory)) {
    [IO.Directory]::CreateDirectory($directory) | Out-Null
}
[IO.File]::WriteAllText($fullPath, $source, [Text.UTF8Encoding]::new($false))
Write-Host 'Release license secret validated and generated (64 Base64URL characters).'
