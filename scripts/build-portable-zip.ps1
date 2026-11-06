#Requires -Version 7.0
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-beta.1',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\portable'),
    [switch]$AllowDirty
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package-content.ps1')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source commit.' }
$dirty = @(& git -C $repo status --porcelain).Count -gt 0
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source worktree status.' }
Assert-CleanPackageSource -Dirty $dirty -AllowDirty:$AllowDirty
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$name = "Vidora-$Version-win-x64"
$zip = Join-Path $output "$name.zip"
if ((Test-Path -LiteralPath $zip) -or (Test-Path -LiteralPath "$zip.sha256")) { throw "Output exists; choose a fresh output directory: $zip" }
# Fresh staging for each attempt; retain all evidence on failure and never delete existing output.
$work = Join-Path $output ("staging-" + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $work $name
New-Item -ItemType Directory -Path $stage | Out-Null
$buildArtifacts = Join-Path $work 'build'
dotnet publish (Join-Path $repo 'src\Vidora\Vidora.csproj') -c Release -r win-x64 --self-contained true --artifacts-path $buildArtifacts -o $stage -p:Version=$Version -p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { throw "Publish failed; evidence retained at $work" }
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'), (Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination $stage
$assetFiles = @(Get-ChildItem -LiteralPath (Join-Path $buildArtifacts 'obj\Vidora') -Filter project.assets.json -File -Recurse)
if ($assetFiles.Count -ne 1) { throw 'Expected the exact assets file for this isolated publish.' }
$assets = Get-Content -LiteralPath $assetFiles[0].FullName -Raw | ConvertFrom-Json
$frameworks = (Get-Content -LiteralPath (Join-Path $stage 'Vidora.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks
$licensePackages = @($frameworks | ForEach-Object { "$($_.name.ToLowerInvariant()).runtime.win-x64/$($_.version)" })
$licensePackages += @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'CommunityToolkit.Mvvm/*' } | ForEach-Object { $_.ToLowerInvariant() })
foreach ($package in $licensePackages) {
    $packageDir = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ $package } | Where-Object { Test-Path -LiteralPath $_ -PathType Container }) | Select-Object -First 1
    if (-not $packageDir) { throw "License package not found: $package" }
    $licenses = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE|ThirdPartyNotices|THIRD-PARTY-NOTICES)(\..*)?$' })
    if ($licenses.Count -eq 0) { throw "Missing license: $package" }
    $destination = Join-Path $stage ('licenses/' + $package)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $licenses | Copy-Item -Destination $destination
}
@"
# Vidora portable — Windows 11 x64

Extract the entire ZIP, then run Vidora.exe. The .NET runtime is included.
FFmpeg and yt-dlp are NOT included. Use Vidora settings to install official
verified yt-dlp and select your own FFmpeg executable.
Internet is required for tool installation, metadata and downloads.
Settings, history, queue, logs and tools use %LOCALAPPDATA%\Vidora.
Delete the extracted folder to remove the app; local data is kept separately.
See LICENSE and THIRD-PARTY-NOTICES.md for license information.
"@ | Set-Content -LiteralPath (Join-Path $stage 'README-PORTABLE.md') -Encoding utf8
$manifest = [ordered]@{ version=$Version; runtime='win-x64'; selfContained=$true; sourceCommit=$commit; sourceDirty=$dirty; distribution='slim'; files=@() }
$manifestPath = Join-Path $stage 'build-manifest.json'
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8
Assert-SlimPackageContent $stage
$manifest.files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Where-Object FullName -ne $manifestPath | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-SlimPackageArchive $stage $zip
Write-Output "Candidate: $zip"
Write-Output "Staging retained: $stage"
