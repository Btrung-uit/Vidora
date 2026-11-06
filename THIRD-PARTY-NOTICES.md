# Third-party notices

## Vidora and the slim distribution

Vidora source is licensed under MIT; see LICENSE. The self-contained Windows
candidate includes the .NET runtime and CommunityToolkit.Mvvm, but no FFmpeg or
yt-dlp executable. A tool's license is separate from Vidora's MIT license.

## Microsoft .NET runtime

The self-contained distribution includes Microsoft .NET runtime components.
Their license and third-party notices are copied from the exact restored
runtime packages into licenses/ in each archive. See https://github.com/dotnet/runtime.

## CommunityToolkit.Mvvm 8.4.2

MIT License. Copyright (c) .NET Foundation and Contributors.
The exact restored package's license and notices are included under licenses/.
See https://github.com/CommunityToolkit/dotnet/blob/main/License.md and
https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2.

## yt-dlp (separately installed at runtime)

Vidora offers installation of the official Windows executable from yt-dlp's
GitHub release, verified against that release's SHA-256 manifest. It is not
included in Vidora's slim artifacts. Upstream source uses The Unlicense;
Windows release executables include additional components with separate terms.
Consult the exact release's LICENSE and THIRD_PARTY_LICENSES.txt:
https://github.com/yt-dlp/yt-dlp and https://github.com/yt-dlp/yt-dlp/releases.

## FFmpeg (external, supplied by the user)

FFmpeg is not downloaded or redistributed by the slim packaging process.
Users select an independently installed executable. The selected build's own
license and notices apply. FFmpeg is normally LGPL 2.1 or later; enabling GPL
components changes its license, and nonfree configurations can be nonredistributable.
See https://ffmpeg.org/legal.html. Bundling a build in a future distribution
would require a separate review of its exact source, build inputs and notices.

## Inno Setup

Inno Setup is an installer build tool, not part of the portable archive.
Copyright Jordan Russell and Martijn Laan. Consult the compiler version's
license and applicable commercial-use terms: https://jrsoftware.org/isinfo.php.
