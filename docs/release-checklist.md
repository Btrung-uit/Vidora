# Release checklist — slim Windows prerelease

Updated 2026-11-06. A passing build is a candidate, not permission to publish.
The owner chose a slim distribution: no FFmpeg or yt-dlp binary is redistributed.
Existing older full portable ZIPs under artifacts are not releasable.

## Automated gates

- Restore with `dotnet restore Vidora.sln --locked-mode`; Release build and full MSTest suite must pass for the exact release commit.
- Run `pwsh -NoProfile -File scripts/test-packaging.ps1`.
- From a clean checkout run `pwsh -NoProfile -File scripts/build-portable-zip.ps1 -Version 0.1.0-beta.1 -OutputDirectory artifacts/release-candidate`.
- Verify ZIP SHA-256, extract, and call `Assert-SlimPackageManifest` from scripts/package-content.ps1. Confirm coreclr.dll exists. This checks inventory/hashes, clean source, no tool binaries, common secrets, user data or media.
- Candidate contains runtime/toolkit licenses, LICENSE, THIRD-PARTY-NOTICES, README-PORTABLE and a source-commit manifest. Explicit `-AllowDirty` is local-only; never release dirty candidates.
- Windows CI uploads verification candidates only. It never creates tags or GitHub Releases.

## Owner gates

- [ ] Complete [hands-on acceptance](manual-release-acceptance.md) on clean Windows 11 x64 without .NET SDK, using the extracted ZIP.
- [x] Owner supplied dated sample download/playback results for all four platforms and tool versions in [platform matrix](platform-test-matrix.md). This validates those samples in the tested candidate, not every URL or subsequent builds.
- [x] Owner explicitly retained the name Vidora on 2026-11-06 after being informed of an existing app-store product using it. This records the naming decision, not trademark clearance.
- [ ] Review/merge the implementation PR. Build/use CI for the final approved commit.
- [ ] Review release notes, platform scope and limitations, then explicitly approve a GitHub prerelease with that exact ZIP/checksum.

## Optional installer

Portable release does not require Inno Setup. If an installer is wanted, install a
reviewed compiler and run in PowerShell 7:

```powershell
./scripts/build-installer.ps1 -PublishDirectory <extracted-clean-candidate> -IsccPath <ISCC.exe>
```

The wrapper checks manifest/inventory before compilation. Test install, upgrade,
uninstall, shortcuts and retained user data on a clean VM; create an EXE checksum.
Installer compilation/testing has not been claimed.

## Decision

**Not yet approved for public release:** technical preparation does not substitute
for owner acceptance, platform samples, naming or publication approval.
