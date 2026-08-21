[CmdletBinding()]
param()

$processes = Get-Process -Name 'CodexUsageBubble' -ErrorAction SilentlyContinue
if ($processes) {
    $processes | Stop-Process -Force
}

[pscustomobject]@{
    Stopped = [bool]$processes
}
