<#
.SYNOPSIS
  Splits a concept sheet into one clean image per view or object, ready for Meshy.
.EXAMPLE
  .\SplitSheet.ps1 -Sheet ..\..\Docs\Concepts\02-bolt-turnaround-v1.png -Out ..\..\Docs\Concepts\views\bolt -Mode Turnaround
  .\SplitSheet.ps1 -Sheet ..\..\Docs\Concepts\25-arena-parts-v1.png -Out ..\..\Docs\Concepts\views\arena-part -Mode Objects
#>
param(
    [Parameter(Mandatory)] [string] $Sheet,
    [Parameter(Mandatory)] [string] $Out,
    [ValidateSet('Turnaround', 'Objects')] [string] $Mode = 'Turnaround',
    [int] $Size = 1024,
    [int] $Join = 24,
    [double] $MinFraction = 0.12
)
$ErrorActionPreference = 'Stop'
if (-not ('SheetSplitter' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'SheetSplitter.cs') -ReferencedAssemblies System.Drawing
}
$sheetPath = (Resolve-Path $Sheet).Path
$outPrefix = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
New-Item -ItemType Directory -Force (Split-Path $outPrefix) | Out-Null
if ($Mode -eq 'Turnaround') { [SheetSplitter]::SplitTurnaround($sheetPath, $outPrefix, $Size) }
else { [SheetSplitter]::SplitObjects($sheetPath, $outPrefix, $Size, $Join, $MinFraction) }

