#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package-content.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('vidora-package-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$stage = Join-Path $fixture 'stage'
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($name in @('Vidora.exe','LICENSE','THIRD-PARTY-NOTICES.md','build-manifest.json')) { Set-Content -LiteralPath (Join-Path $stage $name) -Value '{}' }
Assert-SlimPackageContent $stage
foreach ($name in @('ffmpeg.exe','ffprobe.exe','yt-dlp.exe','history.json','settings.json','cookies.txt','video.mp4','private.pem','.env')) {
    $path = Join-Path $stage $name
    Set-Content -LiteralPath $path -Value 'fixture'
    $rejected = $false
    try { Assert-SlimPackageContent $stage } catch { $rejected = $true }
    Remove-Item -LiteralPath $path
    if (-not $rejected) { throw "Guard accepted $name" }
}
$secret = Join-Path $stage 'app.json'
Set-Content -LiteralPath $secret -Value '{"access_token":"private-value"}'
$rejected = $false
try { Assert-SlimPackageContent $stage } catch { $rejected = $true }
Remove-Item -LiteralPath $secret
if (-not $rejected) { throw 'Guard accepted secret JSON' }
$first = Join-Path $fixture 'first.zip'
$second = Join-Path $fixture 'second.zip'
Write-SlimPackageArchive $stage $first
(Get-Item -LiteralPath (Join-Path $stage 'Vidora.exe')).LastWriteTime = [datetime]::Now.AddDays(-3)
Write-SlimPackageArchive $stage $second
if ((Get-FileHash $first).Hash -ne (Get-FileHash $second).Hash) { throw 'ZIP depends on source timestamps' }
$archive = [IO.Compression.ZipFile]::OpenRead($first)
try { if ($archive.Entries.Count -ne 4 -or 'Vidora.exe' -notin $archive.Entries.FullName) { throw 'ZIP content mismatch' } } finally { $archive.Dispose() }
$rejected = $false
try { Write-SlimPackageArchive $stage $first } catch { $rejected = $true }
if (-not $rejected) { throw 'Existing archive overwritten' }
$expected = (Get-FileHash $first).Hash.ToLowerInvariant() + '  first.zip'
if ((Get-Content "$first.sha256" -Raw).Trim() -ne $expected) { throw 'Checksum mismatch' }
Assert-CleanPackageSource -Dirty $false
Assert-CleanPackageSource -Dirty $true -AllowDirty
$rejected = $false
try { Assert-CleanPackageSource -Dirty $true } catch { $rejected = $true }
if (-not $rejected) { throw 'Dirty source accepted by default' }
$manifestPath = Join-Path $stage 'build-manifest.json'
$manifest = @{ version='0.1.0-beta.1'; distribution='slim'; sourceDirty=$false; files=@(Get-ChildItem $stage -File | Where-Object FullName -ne $manifestPath | ForEach-Object {
    @{ path=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
}) }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
$null = Assert-SlimPackageManifest $stage
foreach ($case in @('dirty','extra','missing','duplicate','modified','traversal')) {
    $testManifest = $manifest | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    switch ($case) {
        'dirty' { $testManifest.sourceDirty = $true }
        'extra' { Set-Content (Join-Path $stage 'extra.dll') 'unlisted' }
        'missing' { $testManifest.files = @($testManifest.files | Select-Object -Skip 1) }
        'duplicate' { $testManifest.files += $testManifest.files[0] }
        'modified' { $testManifest.files[0].sha256 = '0' * 64 }
        'traversal' { $testManifest.files[0].path = '../outside.dll' }
    }
    $testManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    $rejected = $false
    try { $null = Assert-SlimPackageManifest $stage } catch { $rejected = $true }
    if ($case -eq 'extra') { Remove-Item -LiteralPath (Join-Path $stage 'extra.dll') }
    if (-not $rejected) { throw "Manifest accepted invalid case: $case" }
}
Write-Output "PASS: guards, deterministic ZIP, layout, overwrite refusal, checksum, dirty-source gate and exact installer inventory. Fixtures: $fixture"
