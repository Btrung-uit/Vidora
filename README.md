# Vidora

Vidora is a local-first Windows desktop application for discovering and downloading publicly accessible videos with yt-dlp. YouTube, Facebook, Instagram, and TikTok are intended sources; individual platform support remains subject to the dated release checks below.

> **Project status (2026-11-06):** Preparing a slim Windows prerelease. CI verifies code and builds a self-contained candidate without FFmpeg or yt-dlp. Users install yt-dlp through Settings and select an independently installed FFmpeg executable. Clean-machine/platform acceptance and the release-name decision remain pending; no public release is approved yet. See [release checklist](docs/release-checklist.md) and [hands-on acceptance](docs/manual-release-acceptance.md).

## Why Vidora?

Online video downloaders are often unreliable, filled with intrusive ads, or require users to send media URLs through an unknown server. Vidora takes a different approach:

- Downloads run locally on the user's computer.
- Media files go directly to a user-selected folder.
- No Vidora server handles URLs, metadata, or downloaded media.
- The interface hides command-line, codec, and post-processing complexity.

## Implemented MVP scope

- Accept public video links for discovery/download through yt-dlp. Per-platform release support remains unverified until the sample matrix passes.
- Preview the title, thumbnail, author, duration, and detected platform.
- Choose Best, 1080p, 720p, or 480p quality presets.
- Select and remember the destination folder.
- Track progress, speed, downloaded size, and estimated time remaining.
- Cancel active downloads and clean up Vidora-owned temporary files safely.
- Queue multiple videos and process one item at a time.
- Retry failed items directly in the queue, preserving previous history attempts.
- Open completed files or their folders directly from the queue.
- Clear finished queue entries without deleting downloaded files or history.
- Restore pending queue items after restarting the application.
- Keep a local download history with retry and open-file actions.
- Search history by title or URL and combine status and missing-file filters.
- Detect missing dependencies and update `yt-dlp` safely.
- Guide first-time setup with conditional tool instructions and checking/update status.
- Support light, dark, and system themes.
- Show completion and failure notifications.
- Provide actionable errors without exposing sensitive diagnostic data.

## Output file names

Downloaded files use a readable title capped at 80 characters (before extension),
shortened at word boundaries. Facebook/TikTok captions lose leading engagement
counts and hashtags and use the first sentence; Instagram's generic "Video by"
titles become "Instagram - author". Platform/media IDs are used only when no usable
title exists. Existing names get numbered suffixes, such as `Video (2).mp4`, without
overwriting files. Full source titles and URLs remain in history. Existing files
are not renamed by this change.

## Local-first architecture

```text
User
  |
  v
WPF interface
  |
  +--> Queue, history, settings ----> Local JSON files
  |
  +--> Process boundary
          |
          +--> yt-dlp ----> Platform extraction and download
          |
          +--> FFmpeg ----> Merge or remux media when required
  |
  v
User-selected local folder
```

Vidora does not provide a backend service, account system, cloud database, or remote analytics.

## System Design

- **Presentation:** WPF views and MVVM view models handle discovery, queue, history, and settings screens.
- **Core:** shared models, error types, and service contracts keep UI workflows separate from process and storage details.
- **Infrastructure:** implements URL validation, `yt-dlp`/FFmpeg process execution, progress and error parsing, dependency updates, safe file handling, notifications, and persistence.
- **Download flow:** validate a public video URL, ask `yt-dlp` for metadata, then enqueue the user's selected quality and destination. The FIFO queue runs one download at a time; process output updates progress, and cancellation stops the active process tree.
- **Local state and safety:** settings, queue, and history use versioned atomic JSON under `%LOCALAPPDATA%\Vidora`. Output paths are validated, completed files are protected from overwrite, and cleanup is limited to task-owned temporary files.

See [technical-design.md](technical-design.md) for detailed data flow, process protocol, and failure handling.

## Repository Structure

```text
src/Vidora/
├── Core/             # Models, errors, and service contracts
├── Features/         # Download, history, settings, and shell UI
├── Infrastructure/  # Processes, persistence, files, dependencies, and logging
├── Resources/        # Themes and localized UI strings
└── Properties/       # Publish configuration
tests/Vidora.Tests/   # Unit tests and discovery fixtures
docs/                 # Plans, platform matrix, and release checklist
installer/            # Windows installer definition
scripts/              # Portable ZIP build script
```

Build output and generated artifacts (`bin`, `obj`, and `artifacts`) are omitted.

## Technology

| Area | Choice |
|---|---|
| Platform | Windows 11 x64 |
| Runtime | .NET 10 LTS |
| UI | WPF |
| Presentation pattern | MVVM with `CommunityToolkit.Mvvm` |
| Download engine | `yt-dlp` |
| Media processing | FFmpeg |
| Persistence | Atomic local JSON with `System.Text.Json` |
| Testing | MSTest |
| Distribution | Self-contained Windows x64 publish; local portable ZIP; public release pending release gates |

The project intentionally avoids a hosted backend, database server, heavy application framework, and custom platform extractors.

## Design priorities

1. **Data safety** — never overwrite completed files or delete files Vidora does not own.
2. **Clear failure handling** — distinguish unsupported, unavailable, authentication, network, disk, dependency, and post-processing errors.
3. **Responsive desktop UX** — discovery, downloads, cancellation, and persistence must not block the UI thread.
4. **Maintainable integration** — all external processes pass through one tested process boundary.
5. **Privacy** — no telemetry, credential storage, cookie import, or server-side media handling in the MVP.

## MVP boundaries

The first release will not include:

- Private, paid, DRM-protected, or access-restricted media.
- Social-platform login or browser-cookie import.
- Playlist, channel, profile, or bulk downloading.
- Livestream recording.
- Audio-only downloads or format conversion presets.
- Subtitle or thumbnail-only downloads.
- Guaranteed pause and resume across platforms.
- Integrated media playback or editing.
- macOS, Linux, or mobile support.

These constraints keep the first release focused on reliable, safe single-video downloads before expanding the product.

## Roadmap

### Phase 1 — MVP foundation

- Application shell and dependency checks.
- URL discovery and metadata preview.
- Single-video download with progress and cancellation.
- Filesystem safety and process lifecycle management.
- Queue, persistence, history, errors, and diagnostics.
- `yt-dlp` updates, themes, notifications, packaging, and release validation.

### Phase 2 — Media options

- Audio-only downloads.
- Subtitle discovery, download, and optional embedding.
- Thumbnail downloads.
- Advanced format information.

### Phase 3 — Batch and long-running downloads

- Playlist and profile imports with explicit limits.
- Duplicate detection and queue reordering.
- Livestream recording and bandwidth limits.
- Resume support only where the source can guarantee a valid continuation.

### Phase 4 — Authorized access and reach

- Carefully designed browser-cookie support for content the user is already authorized to access.
- Vietnamese and English localization.
- Expanded accessibility verification.
- Additional platforms only after a separate technical evaluation.

## Run a portable build

The portable build does not require installing .NET or running an installer:

1. Extract the complete `Vidora-0.1.0-win-x64.zip` archive to a folder you can write to. Do not run Vidora from inside the ZIP.
2. Open the extracted folder and run `Vidora.exe`.
3. Keep the `licenses` folder and other extracted files beside `Vidora.exe`.
4. In Settings, explicitly install yt-dlp, then select `ffmpeg.exe` from your separately installed FFmpeg. Use the Windows build providers linked by [FFmpeg](https://ffmpeg.org/download.html). Keep its installation folder in place.

The slim archive includes the .NET runtime, but no yt-dlp or FFmpeg binary. Internet access is needed to install/update yt-dlp and discover/download videos. Settings (including the FFmpeg path), history, and logs are stored under `%LOCALAPPDATA%\Vidora` and remain after deleting the extracted app folder. To remove that data too, delete `%LOCALAPPDATA%\Vidora`.

The project can generate this archive locally using the command below. It is not yet approved for public redistribution; see the release gates above.

## Build from source

Requires .NET SDK 10.0.401 or a compatible latest patch of .NET 10 and Windows 11 x64 with the Windows desktop workload.

```powershell
dotnet restore Vidora.sln --locked-mode
dotnet build Vidora.sln -c Release --no-restore
dotnet test Vidora.sln -c Release --no-build
dotnet run --project src/Vidora/Vidora.csproj -c Release
```

In Settings, install the official yt-dlp executable (verified against its release checksum) and choose your FFmpeg executable. Changing FFmpeg validates it before saving; invalid files leave the previous configuration intact. Light/Dark/System follow Windows app preferences; High Contrast takes precedence.

To create a slim candidate from a clean checkout, use PowerShell 7: `pwsh -NoProfile -File scripts/build-portable-zip.ps1 -Version 0.1.0-beta.1`. The archive and SHA-256 are written under ignored `artifacts/portable/`; choose a new output directory when rebuilding. The package includes runtime/toolkit licenses and a file-hash manifest tied to the source commit. Dirty source is rejected unless `-AllowDirty` is explicitly used for local testing; dirty candidates are not releasable.

## Documentation

The architecture, data model, process protocol, safety rules, testing strategy, and release constraints are documented in [technical-design.md](technical-design.md).

The implementation sequence and evidence are tracked in [the MVP plan](docs/superpowers/plans/2026-10-30-vidora-mvp.md). Packaging gates are listed in [the release checklist](docs/release-checklist.md).

## Legal and responsible use

Vidora is intended for content that users own, are authorized to download, or are otherwise permitted to use. It will not bypass DRM or access controls. Users are responsible for following applicable laws, copyright requirements, and platform terms.

Third-party terms are recorded in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Slim packages do not redistribute FFmpeg or yt-dlp. No claim is made that every URL from a listed platform will always work; release support requires a passing dated sample in the platform matrix. CI candidates are verification artifacts, not approved GitHub Releases.
