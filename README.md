# Vidora

Vidora is a local-first Windows desktop application for downloading publicly accessible videos from YouTube, Facebook, Instagram, and TikTok without relying on unstable third-party downloader websites.

> **Project status:** Technical design complete. Implementation has not started yet.

## Why Vidora?

Online video downloaders are often unreliable, filled with intrusive ads, or require users to send media URLs through an unknown server. Vidora takes a different approach:

- Downloads run locally on the user's computer.
- Media files go directly to a user-selected folder.
- No Vidora server handles URLs, metadata, or downloaded media.
- The interface hides command-line, codec, and post-processing complexity.

## Planned MVP

- Download public videos from YouTube, Facebook, Instagram, and TikTok.
- Preview the title, thumbnail, author, duration, and detected platform.
- Choose Best, 1080p, 720p, or 480p quality presets.
- Select and remember the destination folder.
- Track progress, speed, downloaded size, and estimated time remaining.
- Cancel active downloads and clean up Vidora-owned temporary files safely.
- Queue multiple videos and process one item at a time.
- Restore pending queue items after restarting the application.
- Keep a local download history with retry and open-file actions.
- Detect missing dependencies and update `yt-dlp` safely.
- Support light, dark, and system themes.
- Show completion and failure notifications.
- Provide actionable errors without exposing sensitive diagnostic data.

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
| Distribution | Self-contained installer, with a portable ZIP planned |

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

## Documentation

The architecture, data model, process protocol, safety rules, testing strategy, and release constraints are documented in [technical-design.md](technical-design.md).

An implementation plan will be added before production code is written.

## Legal and responsible use

Vidora is intended for content that users own, are authorized to download, or are otherwise permitted to use. It will not bypass DRM or access controls. Users are responsible for following applicable laws, copyright requirements, and platform terms.

`yt-dlp`, FFmpeg, and other third-party components retain their respective licenses. Exact dependency versions, attribution, and notices will be included before the first distributable release.
