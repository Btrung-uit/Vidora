# Queue and history usability implementation plan

**Goal:** Complete five existing desktop workflows requested after the UI audit.
**Architecture:** Keep WPF/MVVM, the serialized queue mutation gate, atomic JSON stores,
existing dependency updater, file-opening delegate, and Vietnamese resources.
**Scope:** Retry failed queue entries, open completed outputs in place, conditional
dependency setup guidance, history search/filter, and clear finished queue entries.
Audio-only is explicitly deferred by the user. No new dependencies, schema changes,
automatic downloads of tools, media deletion, or automatic retries.

## Acceptance and tasks

- [x] Queue service: `RetryAsync(Guid, CancellationToken)` preserves the previous
  history attempt, resets only failed entries, and resumes FIFO processing on explicit
  user action. `ClearFinishedAsync(CancellationToken)` preserves history and files,
  removes terminal entries only, keeps pending/active entries, and saves before
  publishing changes. Failed history persistence prevents removal. Add regression tests.
- [x] Download: wire retry and clear commands with actionable persistence errors;
  completed entries expose open-file/folder actions. Missing files and shell-launch
  failures are handled; no file opens automatically. Add command and rendered-binding tests.
- [x] History: search title/URL (case insensitive), combine with status and missing-file
  filters, show a distinct no-results state, and preserve filters on refresh. All
  filtering is in memory; clearing history still applies to the whole history.
- [x] Setup: scrollable Settings shows conditional yt-dlp/FFmpeg instructions,
  explicit install/update, checking/updating feedback, and a shell banner linking to
  Settings when the tool report needs setup. Do not download FFmpeg automatically; explain
  the expected bundled tools path and recheck action. Add setup/layout tests.
- [x] Verify: focused tests, full suite, Release build, and batched WPF rendering of
  queue/history/setup at default/minimum content widths and both themes. Review
  persistence, stale command parameters, missing outputs, combined filters, and
  first-run offline states. Update README, commit, push branch, and create a draft PR.

## Evidence / decisions

- `8a5d227` pushed first as requested: removal of failed queue entries.
- Shell test now uses plain `Application` with explicit shutdown and local resources,
  avoiding real App startup, user data access, and shutdown of shared WPF state.
- Keep the user's running app and local queue/history untouched during verification;
  build to separate artifacts and use seeded render tests rather than live downloads.
- Retry keeps the queue item's identity and increments its attempt only when processing
  starts; queue order stays FIFO. Clear does not cancel, restart, or resume any task.

## Execution ledger

- Full suite: 266/266 passed. Release build: 0 warnings, 0 errors.
- Seeded offscreen WPF renders inspected at 720x480 and 960x600 for Light/Dark,
  including scrolling to setup recheck and queue actions. No live media download.
- Independent read-only review found no actionable issues.
- Feature changes are committed and pushed on `codex/queue-usability` for draft review.
