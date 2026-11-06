# Platform test matrix

Status date: 2026-11-06. Distribution: slim Windows 11 x64 prerelease candidate.
Local full suite after naming/display fixes: 286/286 passed. Exact build/candidate evidence is recorded by PR/CI.
Automated checks do not substitute for the owner checks below.

Owner validation update (2026-11-06): the owner ran the slim candidate in Windows
Sandbox and supplied screenshots plus explicit confirmation of audible sound and
visible video for YouTube, Facebook, Instagram and TikTok samples. Settings show
yt-dlp 2026.10.26 and FFmpeg 9.0.2-essentials_build-www.gyan.dev. The exact Windows
build, display scaling and build-manifest source commit were not supplied; do not
infer them from successful playback. These are owner-run results, not an agent-run test.

| Check | Evidence | Result |
|---|---|---|
| Full suite | Local SDK 10.0.401, 286/286 | Pass |
| Theme / High Contrast | Injected Windows preference/SystemColors tests; OS unchanged | Pass (automated) |
| External FFmpeg / rollback / legacy settings | Unit/persistence tests | Pass (automated) |
| Slim guard / hashes / deterministic ZIP | PowerShell 7 self-checks; exact candidate CI evidence required | Automated |
| Windows Sandbox launch and tool readiness | Owner screenshots; application ran and downloaded without reported startup errors | Pass (owner-run; SDK absence not independently checked) |
| External FFmpeg selection and persistence | Owner re-opened app; correct FFmpeg path and Ready status retained | Pass (owner-run) |
| yt-dlp first-install flow | Ready/version shown; full installation sequence not captured | Partial evidence |
| Vietnamese/space paths, collision, no-write destination | Owner acceptance pending | Not run on clean machine |
| Re-open app and restore history/queue | Owner screenshots after restart | Pass (owner-run) |
| Retry from history, cancel, clear finished queue, keep files/history | Owner sequence plus subsequent file/history screenshots | Pass (owner-run) |
| History title/URL search, cancelled/completed filters, missing-file filter | Owner screenshots; missing output disables Open file | Pass (owner-run) |
| Shutdown during active download / pending queue resume / failed-item retry | Not demonstrated by supplied sequence | Pending |
| Keyboard, DPI125%/150%, live theme/Contrast, Narrator | Owner acceptance pending | Not run on clean machine |
| Installer compile/install/upgrade/uninstall | Optional; portable comes first | Not run |

## Real source samples

| Platform | Owner-run sample | Date/time | yt-dlp / FFmpeg | Selected preset | Playable output/result |
|---|---|---|---|---|---|
| YouTube | https://www.youtube.com/watch?v=2xhYVB7BJPc | 2026-11-06; time not recorded | 2026.10.26 / 9.0.2 essentials | 480p and 1080p | Pass: owner confirmed sound/picture; playback screenshots in middle and near end |
| Facebook | https://www.facebook.com/reel/2887140408330408 | 2026-11-06; time not recorded | Same reported session | Best | Pass: completed download screenshot, player screenshot and owner sound/picture confirmation |
| Instagram | farkin_around reel; URL visible in owner download screenshot | 2026-11-06; time not recorded | Same reported session | Best | Pass: completed download screenshot, player screenshot and owner sound/picture confirmation |
| TikTok | https://www.tiktok.com/@mindang.com/video/7704953353523956999 | 2026-11-06; time not recorded | Same reported session | Best | Pass: completed download screenshot, player screenshot and owner sound/picture confirmation |

The four listed samples passed. This supports a dated sample-level compatibility
statement, not a guarantee for every URL on those platforms. Selected presets are
not a probe of actual output dimensions/codecs, and FFmpeg stream merging was not
independently verified. No downloaded media or private logs are committed.

The following screenshot findings have implementation fixes pending validation of
the new build: raw enum quality labels, bytes/speed without units, fractional
duration display, and overly long output names. Previous owner-run results apply
to the previously tested candidate; they do not automatically validate these changes.
The owner retained Vidora as the name on 2026-11-06. Release remains
**pending remaining acceptance and publication approval**; see manual-release-acceptance.md.
