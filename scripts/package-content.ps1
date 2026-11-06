#Requires -Version 7.0
function Assert-CleanPackageSource([bool]$Dirty, [switch]$AllowDirty) {
    if ($Dirty -and -not $AllowDirty) { throw 'Source worktree is dirty. Commit changes first; use -AllowDirty only for a local verification candidate.' }
}

function Assert-SlimPackageManifest([string]$Directory) {
    $root = (Resolve-Path -LiteralPath $Directory).Path
    Assert-SlimPackageContent $root
    $manifestPath = Join-Path $root 'build-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.distribution -ne 'slim' -or $manifest.version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$' -or
        $manifest.sourceDirty -isnot [bool] -or $manifest.sourceDirty) { throw 'Installer requires a clean-source slim manifest.' }
    $actual = @(Get-ChildItem -LiteralPath $root -File -Force -Recurse | Where-Object FullName -ne $manifestPath |
        ForEach-Object { [IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/') } | Sort-Object)
    $listed = @($manifest.files | ForEach-Object { $_.path })
    if ($listed.Count -ne $actual.Count -or @($listed | Sort-Object -Unique).Count -ne $listed.Count -or
        @(Compare-Object -ReferenceObject $actual -DifferenceObject $listed).Count -gt 0) { throw 'Manifest file inventory does not match package.' }
    foreach ($file in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $root $file.path))
        if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes staging' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Manifest mismatch: $($file.path)" }
    }
    return $manifest
}

function Assert-SlimPackageContent([string]$Directory) {
    $root = (Resolve-Path -LiteralPath $Directory).Path
    $allowedExtensions = @('.exe','.dll','.json','.md','.txt','.pdb','.config','.xml','.pri','.dat','.bin')
    foreach ($entry in Get-ChildItem -LiteralPath $root -Force -Recurse) {
        $relative = [IO.Path]::GetRelativePath($root, $entry.FullName)
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Links forbidden: $relative" }
        if ($relative -match '(?i)(^|[\\/])(tools|logs|history|queue|settings|downloads|temp|\.git|\.env|secrets?)([\\/.]|$)' -or
            $entry.Name -match '(?i)(ffmpeg|ffprobe|ffplay|yt[-_]dlp|cookies|credentials|token|password)' -or
            (-not $entry.PSIsContainer -and $entry.Extension -and $entry.Extension.ToLowerInvariant() -notin $allowedExtensions) -or
            (-not $entry.PSIsContainer -and -not $entry.Extension -and $entry.Name -ne 'LICENSE')) { throw "Forbidden package content: $relative" }
        if (-not $entry.PSIsContainer -and $entry.Extension -in @('.json','.config','.xml','.txt','.md')) {
            $content = Get-Content -LiteralPath $entry.FullName -Raw
            if ($content -match '(?i)(-----BEGIN .*PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16}|Bearer\s+[A-Za-z0-9._-]{20,}|"(?:password|access_token|refresh_token|client_secret)"\s*:\s*"[^"\s]+")') { throw "Possible secret: $relative" }
        }
    }
    foreach ($required in @('Vidora.exe','LICENSE','THIRD-PARTY-NOTICES.md','build-manifest.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) { throw "Missing package file: $required" }
    }
}
function Write-SlimPackageArchive([string]$Directory, [string]$Destination) {
    Assert-SlimPackageContent $Directory
    if ((Test-Path -LiteralPath $Destination) -or (Test-Path -LiteralPath "$Destination.sha256")) { throw "Archive exists: $Destination" }
    # Fixed entry order and timestamps avoid ZIP variation from publish file times.
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName) {
            $name = [IO.Path]::GetRelativePath($Directory,$file.FullName).Replace('\','/')
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $stream = $entry.Open()
            $source = [IO.File]::OpenRead($file.FullName)
            try { $source.CopyTo($stream) } finally { $source.Dispose(); $stream.Dispose() }
        }
    } finally { $archive.Dispose() }
    $hash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$Destination.sha256" -Value "$hash  $([IO.Path]::GetFileName($Destination))" -Encoding ascii
}
