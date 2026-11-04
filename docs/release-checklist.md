# Release checklist

## Current gate

- [ ] Select and audit a redistributable FFmpeg Windows x64 build. Record exact version, source URL/revision, configure flags, SHA-256, applicable license text, corresponding source, and redistribution obligations in `THIRD-PARTY-NOTICES.md`.
- [ ] Check Vidora name/trademark conflicts before public release.
- [ ] Run the clean Windows 11 x64 platform matrix and meet the four-platform acceptance gate.

## Build and verify

1. `dotnet restore Vidora.sln --locked-mode`
2. `dotnet build Vidora.sln -c Release --no-restore`
3. `dotnet test Vidora.sln -c Release --no-build`
4. `dotnet publish src/Vidora/Vidora.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64`
5. Place only the reviewed FFmpeg executable at `artifacts/approved-ffmpeg/ffmpeg.exe`; compile with `--define=FfmpegSha256=<reviewed-sha256>` and its reviewed materials under the release notices. The installer preprocessor compares the actual file hash to this value.
6. Compile `installer/Vidora.iss` with Inno Setup 7.1.1 or a reviewed compatible 7.x version.
7. Create portable ZIP from the same publish folder plus `LICENSE` and `THIRD-PARTY-NOTICES.md`.
8. Generate SHA-256 files for installer and ZIP; verify them after copying to the release staging folder.
9. Inspect publish output for local data, logs, test media, secrets and downloaded yt-dlp; none may be included.

No distributable is approved until every current gate above is checked and the dated platform matrix supports each README claim.
