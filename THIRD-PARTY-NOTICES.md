# Third-party notices

This repository and its source do not contain redistributed yt-dlp or FFmpeg executables.

## Vidora

Vidora source is licensed under MIT; see `LICENSE`.

## CommunityToolkit.Mvvm 8.4.2

MIT License. Package: https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2

## yt-dlp

The application downloads the official Windows executable at runtime from the yt-dlp GitHub release and verifies it against that release's SHA-256 manifest. The executable is not bundled by this repository. yt-dlp is distributed under The Unlicense and may include third-party components; see the exact release's `THIRD_PARTY_LICENSES.txt` and https://github.com/yt-dlp/yt-dlp.

## FFmpeg

FFmpeg is not bundled. No Windows build has been selected, pinned, or audited for its configure flags, enabled codecs, license obligations, source availability, and checksum. FFmpeg upstream states its default is LGPL v2.1+ while optional GPL/nonfree components change the applicable terms: https://ffmpeg.org/legal.html. Packaging is blocked until a specific redistributable build and matching source/license materials are reviewed and recorded here.

## Inno Setup 7

The installer script uses Inno Setup as a build tool; the compiler is not included in Vidora artifacts. Inno Setup is copyrighted by Jordan Russell and Martijn Laan; see its official license and commercial-use terms at https://jrsoftware.org/isinfo.php.
