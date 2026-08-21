[CmdletBinding()]
param(
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$pluginRoot = Split-Path -Parent $PSScriptRoot
$sourceExe = Join-Path $pluginRoot 'assets\CodexUsageBubble.exe'
$installDirectory = Join-Path $env:LOCALAPPDATA 'CodexUsageBubble'
$installedExe = Join-Path $installDirectory 'CodexUsageBubble.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runCommand = '"' + $installedExe + '"'

if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Bundled executable not found: $sourceExe"
}

Get-Process -Name 'CodexUsageBubble' -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
New-ItemProperty -Path $runKey -Name 'CodexUsageBubble' -PropertyType String -Value $runCommand -Force | Out-Null

if (-not $NoStart) {
    Start-Process -FilePath $installedExe -WindowStyle Hidden
}

[pscustomobject]@{
    InstalledExe = $installedExe
    AutoStart = $true
    Started = -not $NoStart
}
