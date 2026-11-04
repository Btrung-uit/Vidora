param(
    [string]$YtDlpPath = (Join-Path $env:LOCALAPPDATA 'Vidora\tools\yt-dlp.exe'),
    [string]$FfmpegPath = (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\Gyan.FFmpeg.Essentials_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\portable')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $repo 'artifacts\publish\win-x64'
$version = '0.1.0'
$expectedYtDlpSha256 = 'f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d'
$expectedFfmpegSha256 = '72A489ECCD008C2EC2C0A5856C5C75BC3D8BBFA90166C4566865C246445E6AA3'

foreach ($binary in @($YtDlpPath, $FfmpegPath)) {
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
        throw "Required runtime binary not found: $binary"
    }
}
if ((Get-FileHash -LiteralPath $YtDlpPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedYtDlpSha256) {
    throw 'Bundled yt-dlp hash does not match the verified 2026.10.26 release.'
}
if ((Get-FileHash -LiteralPath $FfmpegPath -Algorithm SHA256).Hash.ToUpperInvariant() -ne $expectedFfmpegSha256) {
    throw 'FFmpeg hash does not match the installed Gyan Essentials 9.0.1 build.'
}

dotnet publish (Join-Path $repo 'src\Vidora\Vidora.csproj') -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$stage = Join-Path $OutputDirectory "Vidora-$version-win-x64"
New-Item -ItemType Directory -Path (Join-Path $stage 'tools\ffmpeg') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'tools') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'licenses\ffmpeg') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'licenses\yt-dlp') -Force | Out-Null
Get-ChildItem -LiteralPath $publish -Force | Copy-Item -Destination $stage -Recurse -Force
Copy-Item -LiteralPath $YtDlpPath -Destination (Join-Path $stage 'tools\yt-dlp.exe') -Force
Copy-Item -LiteralPath $FfmpegPath -Destination (Join-Path $stage 'tools\ffmpeg\ffmpeg.exe') -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination $stage -Force

$ffmpegPackage = Split-Path (Split-Path $FfmpegPath -Parent) -Parent
Copy-Item -LiteralPath (Join-Path $ffmpegPackage 'LICENSE') -Destination (Join-Path $stage 'licenses\ffmpeg\LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $ffmpegPackage 'README.txt') -Destination (Join-Path $stage 'licenses\ffmpeg\README.txt') -Force
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.10.26/LICENSE' -OutFile (Join-Path $stage 'licenses\yt-dlp\LICENSE')
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.10.26/THIRD_PARTY_LICENSES.txt' -OutFile (Join-Path $stage 'licenses\yt-dlp\THIRD_PARTY_LICENSES.txt')
@'

## This portable archive's bundled binaries

- yt-dlp 2026.10.26 Windows executable, SHA-256 `f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d`; release checksum was verified. Its bundled licenses are in `licenses/yt-dlp/`.
- FFmpeg 9.0.1 Essentials build from Gyan.dev, GPL v3, SHA-256 `72A489ECCD008C2EC2C0A5856C5C75BC3D8BBFA90166C4566865C246445E6AA3`. The exact source commit and build configuration are recorded in `licenses/ffmpeg/README.txt`; GPL text is in `licenses/ffmpeg/LICENSE`.

This locally generated archive does not include all corresponding source and build inputs for the statically linked FFmpeg build. Do not publicly redistribute it until complete corresponding source and all applicable third-party notices have been assembled and reviewed.
'@ | Add-Content -LiteralPath (Join-Path $stage 'THIRD-PARTY-NOTICES.md') -Encoding utf8

$readme = @'
# Vidora portable (Windows x64)

1. Extract the complete ZIP archive to a folder you can write to. Do not run the app from inside the ZIP.
2. Run `Vidora.exe` from the extracted folder. Keep the `tools` and `licenses` folders beside it.

The app includes the .NET runtime, yt-dlp, and FFmpeg. The first launch copies the bundled yt-dlp into `%LOCALAPPDATA%\Vidora\tools` so later updates do not modify this extracted folder. Queue, settings, history, logs, and temporary files are stored under `%LOCALAPPDATA%\Vidora`.

Internet is required for video metadata/downloads and future yt-dlp updates. FFmpeg and yt-dlp are bundled at the versions recorded in `THIRD-PARTY-NOTICES.md`; see `licenses` for their notices.

To remove the app, delete this extracted folder. To also erase local settings/history, delete `%LOCALAPPDATA%\Vidora`.
'@
Set-Content -LiteralPath (Join-Path $stage 'README-PORTABLE.md') -Value $readme -Encoding utf8

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip = Join-Path $OutputDirectory "Vidora-$version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
Get-Item -LiteralPath $zip | Select-Object FullName,Length
Get-Content -LiteralPath "$zip.sha256"
