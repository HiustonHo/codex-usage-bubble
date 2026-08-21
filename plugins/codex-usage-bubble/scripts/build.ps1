[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $pluginRoot 'src\CodexUsageBubble.cs'
$output = Join-Path $pluginRoot 'assets\CodexUsageBubble.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

& $compiler /nologo /target:winexe /optimize+ /out:$output /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $source
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
