[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'CodexUsageBubble'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process -Name 'CodexUsageBubble' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path $runKey -Name 'CodexUsageBubble' -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

[pscustomobject]@{
    Removed = $true
    InstallDirectory = $installDirectory
}
