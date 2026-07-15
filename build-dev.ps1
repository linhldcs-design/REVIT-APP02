# build-dev.ps1 — Build nhanh cho hot-reload qua Add-in Manager (KHÔNG cần tắt Revit).
# Cách dùng: chạy script này → vào Revit → Add-in Manager → Reload DLL bên dưới.
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "RevitAPP"
$dll = Join-Path $proj "bin\Debug.R25\RevitAPP.dll"

Write-Host "Building (DeployAddin=false, khong dung ban Addins dang bi Revit khoa)..." -ForegroundColor Cyan
Push-Location $proj
dotnet build -c Debug.R25 -p:DeployAddin=false --nologo -v minimal
$ok = $?
Pop-Location

if (-not $ok) { Write-Host "BUILD FAIL" -ForegroundColor Red; exit 1 }

$info = Get-Item $dll
Write-Host ""
Write-Host "BUILD OK -> $($info.LastWriteTime)  ($([math]::Round($info.Length/1MB,2)) MB)" -ForegroundColor Green
Write-Host "DLL hot-reload:" -ForegroundColor Yellow
Write-Host "  $dll" -ForegroundColor White
# Copy duong dan vao clipboard de dan nhanh vao Add-in Manager
$dll | Set-Clipboard
Write-Host "(Da copy duong dan vao clipboard)" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Trong Revit: Add-Ins > Add-in Manager > tab 'Manual Mode' > dan duong dan > Load/Reload > chay 'Draw Column Rebar'." -ForegroundColor Cyan
