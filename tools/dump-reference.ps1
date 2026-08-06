<#
.SYNOPSIS
    Decompiles an installed Celeste into celeste_reference, the authority for every gameplay
    question this repository asks.

.DESCRIPTION
    The port is written by reading the game's own source beside it, so this folder is not
    optional for development -- but it is Celeste's code, so it is gitignored and everybody
    makes their own. ILSpy in project mode writes it out one namespace per folder, which is
    where Celeste\ and Monocle\ come from.

    An Everest-patched install is the one to decompile: orig_Update and the rest of the
    method names AGENTS.md cites are MonoMod's, and a vanilla assembly has none of them.

.PARAMETER Destination
    Where to write it. Defaults to celeste_reference beside this repository.

.PARAMETER CelestePath
    An install to decompile. Defaults to CELESTE_PATH, then celeste-path.txt at the repo root.

.EXAMPLE
    tools\dump-reference.ps1
    tools\dump-reference.ps1 -Destination D:\scratch\celeste-source
#>
[CmdletBinding()]
param(
    [string]$Destination,
    [string]$CelestePath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path $repo 'celeste_reference' }

$pathFile = Join-Path $repo 'celeste-path.txt'
if (-not $CelestePath) { $CelestePath = $env:CELESTE_PATH }
if (-not $CelestePath -and (Test-Path $pathFile)) {
    $CelestePath = (Get-Content $pathFile | Where-Object { $_.Trim() -and -not $_.StartsWith('#') } |
        Select-Object -First 1).Trim()
}
if (-not $CelestePath) {
    throw "No Celeste install. Put its folder in $pathFile, or pass -CelestePath."
}

# Celeste.dll on current builds, Celeste.exe on the older .NET Framework ones.
$assembly = Join-Path $CelestePath 'Celeste.dll'
if (-not (Test-Path $assembly)) { $assembly = Join-Path $CelestePath 'Celeste.exe' }
if (-not (Test-Path $assembly)) { throw "No Celeste.dll or Celeste.exe in $CelestePath." }

$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing ilspycmd...'
    # The newest package does not install on every SDK, so fall back to one that does.
    dotnet tool install -g ilspycmd 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { dotnet tool install -g ilspycmd --version 8.2.0.7535 }
    if ($LASTEXITCODE -ne 0) { throw 'could not install ilspycmd' }
}

Write-Host "Decompiling $assembly. This takes about half a minute."
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
ilspycmd $assembly -p -o $Destination -r $CelestePath
if ($LASTEXITCODE -ne 0) { throw "ilspycmd failed with exit code $LASTEXITCODE" }

$celeste = (Get-ChildItem (Join-Path $Destination 'Celeste') -Filter *.cs).Count
$monocle = (Get-ChildItem (Join-Path $Destination 'Monocle') -Filter *.cs).Count
Write-Host "$Destination : $celeste files in Celeste\, $monocle in Monocle\"
