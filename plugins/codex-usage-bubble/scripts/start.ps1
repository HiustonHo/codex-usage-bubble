[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installedExe = Join-Path (Join-Path $env:LOCALAPPDATA 'CodexUsageBubble') 'CodexUsageBubble.exe'

if (-not (Test-Path -LiteralPath $installedExe)) {
    throw 'Codex Usage Bubble is not installed. Run install.ps1 first.'
}

if (-not (Get-Process -Name 'CodexUsageBubble' -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $installedExe -WindowStyle Hidden
}

Get-Process -Name 'CodexUsageBubble' -ErrorAction Stop | Select-Object Id, StartTime
