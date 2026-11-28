<#
.SYNOPSIS
    Builds a clean, non-self-extracting Portable Windows Release of LyricPulse,
    optionally Authenticode-signs the binary, verifies it with --smoke-test,
    packages a portable .zip, and writes SHA256SUMS.txt.

.DESCRIPTION
    Avoids PublishSingleFile + IncludeNativeLibrariesForSelfExtract (which extracts
    native DLLs into %TEMP%\.net\ at runtime and frequently triggers Windows Defender
    behavioral dropper false positives on unsigned open-source releases).
#>
[CmdletBinding()]
param(
    [string]$Version = "1.2.0",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [string]$CertThumbprint = $env:LYRICPULSE_SIGN_CERT_THUMBPRINT,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [switch]$SkipSmokeTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "publish"
}

$dotnetExe = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    $cmd = Get-Command dotnet -ErrorAction Stop
    $dotnetExe = $cmd.Source
}

$stageDir = Join-Path $OutputRoot "LyricPulse-v$Version-$Runtime-portable"
$zipPath = Join-Path $OutputRoot "LyricPulse-v$Version-$Runtime-portable.zip"
$checksumPath = Join-Path $OutputRoot "SHA256SUMS.txt"
$appProject = Join-Path $repoRoot "src\LyricPulse.App\LyricPulse.App.csproj"

if (Test-Path $stageDir) {
    Remove-Item -Recurse -Force $stageDir
}
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

Write-Host "[1/4] Publishing clean non-self-extracting portable Release ($Runtime)..." -ForegroundColor Cyan
& $dotnetExe publish $appProject `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -p:InformationalVersion=$Version `
    -o $stageDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $stageDir "LyricPulse.App.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected executable not found at $exePath"
}

if (-not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
    Write-Host "[2/4] Signing LyricPulse.App.exe with Authenticode certificate $CertThumbprint..." -ForegroundColor Cyan
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        throw "signtool.exe not found on PATH; install Windows SDK Signing Tools."
    }

    & $signtool.Source sign /fd SHA256 /tr $TimestampServer /td SHA256 /sha1 $CertThumbprint $exePath
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode signing failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Host "[2/4] Skipping Authenticode signing (set -CertThumbprint or LYRICPULSE_SIGN_CERT_THUMBPRINT to enable)." -ForegroundColor Yellow
}

if (-not $SkipSmokeTest) {
    Write-Host "[3/4] Running headless smoke test on portable binary..." -ForegroundColor Cyan
    $smokeProc = Start-Process -FilePath $exePath -ArgumentList "--smoke-test" -NoNewWindow -Wait -PassThru
    if ($smokeProc.ExitCode -ne 0) {
        throw "Smoke test failed with exit code $($smokeProc.ExitCode)"
    }
    Write-Host "  Smoke test passed (exit code 0)." -ForegroundColor Green
} else {
    Write-Host "[3/4] Skipping smoke test." -ForegroundColor Yellow
}

Write-Host "[4/4] Packaging portable ZIP and writing SHA256SUMS.txt..." -ForegroundColor Cyan
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

$exeHash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipName = Split-Path -Leaf $zipPath
$checksumLines = @(
    "$zipHash  $zipName"
    "$exeHash  LyricPulse.App.exe"
)
[System.IO.File]::WriteAllLines($checksumPath, $checksumLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "Portable release ready:" -ForegroundColor Green
Write-Host "  ZIP:    $zipPath"
Write-Host "  EXE:    $exePath"
Write-Host "  SHA256: $checksumPath"
