param(
    [int]$Timeout = 30,
    [switch]$NoQuickplay = $false,
    [switch]$Verbose = $false
)

# run-quick.ps1 - build + launch DwarfCorp in quickplay mode, capture stdout/stderr + AppData
# log + breadcrumbs + latest crash log, kill on timeout.
# Usage:
#   powershell -File run-quick.ps1 -Timeout 30               # quickplay mode (default)
#   powershell -File run-quick.ps1 -Timeout 30 -NoQuickplay  # normal launch (needs manual clicks)
#   powershell -File run-quick.ps1 -Timeout 30 -Verbose      # + DWARFCORP_VERBOSE=1

$ErrorActionPreference = 'Continue'
Set-Location -Path $PSScriptRoot

Write-Host "Building DwarfCorp (Debug)..."
dotnet build DwarfCorp/DwarfCorpFNA.csproj -c Debug --nologo -v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

$logPath    = Join-Path $PSScriptRoot "run-quick.log"
$errLogPath = Join-Path $PSScriptRoot "run-quick.err.log"
Remove-Item $logPath, $errLogPath -ErrorAction SilentlyContinue

$exePath = Join-Path $PSScriptRoot "DwarfCorp/bin/FNA/Debug/DwarfCorp.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at $exePath"
    exit 2
}

if (-not $NoQuickplay) { $env:DWARFCORP_QUICKPLAY = "1" } else { Remove-Item Env:DWARFCORP_QUICKPLAY -ErrorAction SilentlyContinue }
if ($Verbose) { $env:DWARFCORP_VERBOSE = "1" } else { Remove-Item Env:DWARFCORP_VERBOSE -ErrorAction SilentlyContinue }
$env:DWARFCORP_AUTOEXIT_SECONDS = $Timeout

Write-Host "Launching $exePath (quickplay=$(-not $NoQuickplay) verbose=$Verbose autoexit=$Timeout s)..."

$p = Start-Process -FilePath $exePath -RedirectStandardOutput $logPath -RedirectStandardError $errLogPath -PassThru -NoNewWindow

$grace = 5
$p.WaitForExit(($Timeout + $grace) * 1000) | Out-Null

if (-not $p.HasExited) {
    Write-Warning "Process did not exit after $($Timeout + $grace)s - forcing"
    try { Stop-Process -InputObject $p -Force } catch { }
    $exitCode = -1
} else {
    $exitCode = $p.ExitCode
}

Write-Host ""
Write-Host "===== run-quick.log tail (80) ====="
if (Test-Path $logPath) { Get-Content $logPath -Tail 80 }

Write-Host ""
Write-Host "===== run-quick.err.log tail (30) ====="
if (Test-Path $errLogPath) { Get-Content $errLogPath -Tail 30 }

$appDataDir = Join-Path $env:APPDATA "DwarfCorp"
$appDataLog = Join-Path $appDataDir "log.txt"
$breadcrumbsLast = Join-Path $appDataDir "Logging\breadcrumbs_last.txt"
$breadcrumbsCurrent = Join-Path $appDataDir "Logging\breadcrumbs_current.txt"

if (Test-Path $appDataLog) {
    Write-Host ""
    Write-Host "===== APPDATA log.txt tail (120) ====="
    Get-Content $appDataLog -Tail 120
}

if (Test-Path $breadcrumbsCurrent) {
    Write-Host ""
    Write-Host "===== breadcrumbs_current.txt (live tail during run) ====="
    Get-Content $breadcrumbsCurrent
}
if (Test-Path $breadcrumbsLast) {
    Write-Host ""
    Write-Host "===== breadcrumbs_last.txt (from most recent ProcessExit) ====="
    Get-Content $breadcrumbsLast
}

$crashLogsDir = Join-Path $appDataDir "Logging"
if (Test-Path $crashLogsDir) {
    $latestCrash = Get-ChildItem $crashLogsDir -Filter "*_Crashlog.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestCrash -and $latestCrash.LastWriteTime -gt (Get-Date).AddMinutes(-5)) {
        Write-Host ""
        Write-Host "===== LATEST CRASH LOG: $($latestCrash.Name) ====="
        Get-Content $latestCrash.FullName
    }
}

Write-Host ""
Write-Host "======================================================"
Write-Host "Exit code: $exitCode  (0=clean, nonzero=crash)"
Write-Host "Full logs: $logPath and $errLogPath"
Write-Host "======================================================"
exit 0
