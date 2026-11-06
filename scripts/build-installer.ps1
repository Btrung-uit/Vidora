#Requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$IsccPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package-content.ps1')
$stage = (Resolve-Path -LiteralPath $PublishDirectory).Path
$manifest = Assert-SlimPackageManifest $stage
if (-not (Test-Path -LiteralPath (Join-Path $stage 'licenses') -PathType Container)) { throw 'Dependency licenses missing' }
& $IsccPath "/DPublishDir=$stage" "/DAppVersion=$($manifest.version)" '/DValidatedSlimInput=1' (Join-Path $PSScriptRoot '..\installer\Vidora.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
