# Platform test matrix

Status date: 2026-11-04

This is a release record, not a claim of completed manual testing. Automated checks were run with .NET SDK 10.0.401 on 2026-11-04. Release build: 0 warnings/0 errors; automated suite: 243/243 passed; self-contained `win-x64` publish succeeded and publish output contained no local data/test media. Inno Setup compiler was not installed, and no clean-machine installer, uninstall, accessibility, or real-platform sample run was performed.

| Check | Environment/tool versions | Result | Evidence/notes |
|---|---|---|---|
| Self-contained win-x64 publish | .NET SDK 10.0.401 | Pass | `artifacts/publish/win-x64`; no local data/test media found |
| Inno Setup installer compile | Compiler unavailable | Not run | Requires Inno Setup 7 and audited FFmpeg |
| Clean Windows 11 x64 install/uninstall | Not run | Pending | Requires compiled installer |
| First-run yt-dlp install and dependency versions | Not run | Pending | |
| Vietnamese and paths containing spaces | Not run | Pending | |
| Output collisions and unwritable destination | Not run | Pending | |
| Cancel, shutdown, queue restore and corrupt persistence | Not run | Pending | |
| Keyboard-only, 125%/150% scaling, High Contrast, screen reader | Not run | Pending | |
| YouTube public sample | Not run | Pending | No support claim until passed |
| Facebook public sample | Not run | Pending | No support claim until passed |
| Instagram public sample | Not run | Pending | No support claim until passed |
| TikTok public sample | Not run | Pending | No support claim until passed |

Release decision: **blocked** pending FFmpeg redistribution audit and all manual acceptance checks above.
