param(
    [int]$Timeout = 20
)

# run-quick.ps1 - build + launch DwarfCorp, capture stdout/stderr, kill on timeout.
# Sets DWARFCORP_AUTOEXIT_SECONDS so the game self-terminates.
# Usage: powershell -File run-quick.ps1 -Timeout 25
# After it finishes, read run-quick.log and run-quick.err.log in the repo root.

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

Write-Host "Launching $exePath (autoexit in $Timeout s)..."
$env:DWARFCORP_AUTOEXIT_SECONDS = $Timeout

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
Write-Host "Log:     $logPath"
Write-Host "Err log: $errLogPath"
Write-Host "Exit:    $exitCode"
exit 0
