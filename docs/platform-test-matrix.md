# Platform test matrix

Status date: 2026-11-06. Distribution: slim Windows 11 x64 prerelease candidate.
Local full suite: 275/275 passed. Exact build/candidate evidence is recorded by PR/CI.
Automated checks do not substitute for the owner checks below.

| Check | Evidence | Result |
|---|---|---|
| Full suite | Local SDK 10.0.401, 275/275 | Pass |
| Theme / High Contrast | Injected Windows preference/SystemColors tests; OS unchanged | Pass (automated) |
| External FFmpeg / rollback / legacy settings | Unit/persistence tests | Pass (automated) |
| Slim guard / hashes / deterministic ZIP | PowerShell 7 self-checks; exact candidate CI evidence required | Automated |
| Clean Windows 11 x64 first run without SDK | Owner acceptance pending | Not run |
| yt-dlp install / external FFmpeg selection | Owner acceptance pending | Not run on clean machine |
| Vietnamese/space paths, collision, no-write destination | Owner acceptance pending | Not run on clean machine |
| Cancel/shutdown/restore/retry/clear/file preservation | Owner acceptance pending | Not run on clean machine |
| Keyboard, DPI125%/150%, live theme/Contrast, Narrator | Owner acceptance pending | Not run on clean machine |
| Installer compile/install/upgrade/uninstall | Optional; portable comes first | Not run |

## Real source samples

| Platform | Public authorized sample URL | Date/time | yt-dlp / FFmpeg | Quality | Playable output/result |
|---|---|---|---|---|---|
| YouTube | Pending | — | — | — | Not run |
| Facebook | Pending | — | — | — | Not run |
| Instagram | Pending | — | — | — | Not run |
| TikTok | Pending | — | — | — | Not run |

No four-platform support claim until all four pass. Record real versions/failures;
do not commit downloaded media, private logs or cookies. Release remains
**pending owner acceptance**; see manual-release-acceptance.md.
