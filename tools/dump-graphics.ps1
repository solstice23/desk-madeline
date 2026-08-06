<#
.SYNOPSIS
    Unpacks an installed Celeste's atlases into celeste_graphics_dump, one png per sprite.

.DESCRIPTION
    The dump is the game's artwork as loose files, laid out so that
    Graphics/Atlases/<atlas>/<path>.png is sprite <path> of atlas <atlas> -- the same names
    the code asks for, and the same ones docs/celeste-atlas-index.tsv lists. It is far too
    large to keep in the repository, so make your own when you need to look at a sprite.

    Nothing in the app or the checks requires it. AtlasChecks compares against it when it is
    there and says so when it is not.

.PARAMETER Destination
    Where to write it. Defaults to celeste_graphics_dump beside this repository.

.PARAMETER CelestePath
    An install to unpack. Defaults to CELESTE_PATH, then celeste-path.txt at the repo root,
    then wherever CelesteInstall finds one.

.EXAMPLE
    tools\dump-graphics.ps1
    tools\dump-graphics.ps1 -Destination D:\scratch\celeste-art
#>
[CmdletBinding()]
param(
    [string]$Destination,
    [string]$CelestePath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if ($CelestePath) { $env:CELESTE_PATH = $CelestePath }
# The app does not read celeste-path.txt -- it is a development file -- so hand it over as
# the variable it does read, the way tools\dump-reference.ps1 does.
if (-not $env:CELESTE_PATH) {
    $pathFile = Join-Path $repo 'celeste-path.txt'
    if (Test-Path $pathFile) {
        $env:CELESTE_PATH = (Get-Content $pathFile |
            Where-Object { $_.Trim() -and -not $_.StartsWith('#') } | Select-Object -First 1).Trim()
    }
}
if ($Destination) { $env:GRAPHICSDUMP_OUT = $Destination }
$env:GRAPHICSDUMP = '1'

try {
    Write-Host 'Unpacking Celeste''s atlases. This takes a minute and writes a few hundred MB.'
    dotnet run --project (Join-Path $repo 'tests\DeskMadeline.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw "the dump failed with exit code $LASTEXITCODE" }
}
finally {
    Remove-Item Env:GRAPHICSDUMP -ErrorAction SilentlyContinue
    Remove-Item Env:GRAPHICSDUMP_OUT -ErrorAction SilentlyContinue
}
