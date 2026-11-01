# Vidora Task 8 Errors and Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide stable actionable errors, secret-safe copied diagnostics, and local rolling logs without changing the existing `VidoraError` contract or uploading user data.

**Architecture:** A pure redactor protects every diagnostic boundary. One yt-dlp classifier maps known process output to the existing error codes, while a small injected file logger writes redacted structured entries. Discovery/download services classify and log failures; ViewModels expose explicit copy commands using only redacted details.

**Tech Stack:** .NET 10, WPF, MSTest, `System.Text.Json`, manual constructor injection; no new package.

**Spec:** `technical-design.md` sections Error handling and Logging; parent plan `docs/superpowers/plans/2026-10-30-vidora-mvp.md` Task 8.

## Global Constraints

- Keep the existing `VidoraErrorCode` values and `VidoraError(Code, UserMessageKey, SuggestedActionKey, TechnicalDetail, IsRetryable)` shape stable.
- Keep `Unknown` as a valid classification; cancellation must remain cancellation and must not become a failed download.
- Never log or copy raw metadata JSON, the complete environment, cookie values, token-like query values, or an unredacted user-profile path.
- Write at most five local log files of at most 2 MiB each under `%LOCALAPPDATA%\Vidora\logs`.
- Diagnostics are copied only after an explicit user command and are never uploaded.
- Use resource keys for all visible Vietnamese text.
- Do not add Serilog, a DI container, or another dependency.
- Implement each milestone with TDD, run the focused tests, then commit before starting the next milestone.

## Usage Guard

The Codex account quota is not visible to the agent. Estimates below are model-context estimates, not billing guarantees, and include reading, edits, tests, and one debugging pass.

| Milestone | Estimated usage | Share of Task 8 |
|---|---:|---:|
| 1. Diagnostic redaction | 10k-15k tokens | 12% |
| 2. Stable yt-dlp classification | 15k-22k tokens | 18% |
| 3. Rolling file logger | 15k-22k tokens | 18% |
| 4. Service integration | 18k-28k tokens | 24% |
| 5. Copy-diagnostic UX and composition | 15k-22k tokens | 18% |
| 6. Final verification and review | 8k-14k tokens | 10% |

Expected total: roughly 81k-123k model tokens. The ranges overlap because fixes found in a later milestone can exercise earlier code.

At every milestone boundary:

1. Commit the green milestone and report actual tests/results.
2. Stop if the app reports less remaining usage than the next milestone's upper estimate plus a 20% safety reserve.
3. Stop if remaining usage cannot be checked and the user has not explicitly said to continue past the checkpoint.
4. Never begin final review with less than 17k tokens of known budget (14k upper estimate plus reserve).

## Review Focus

1. Mixed-case and percent-encoded sensitive query keys must be redacted without damaging ordinary URLs — covered in Milestone 1.
2. One stderr line containing several markers must resolve by an explicit priority, not incidental string order — covered in Milestone 2.
3. Concurrent writes at the 2 MiB boundary must keep complete entries and no more than five files — covered in Milestone 3.
4. Logger I/O failure must never replace the original discovery/download error — covered in Milestone 4.
5. Copy commands must never expose cookie/token values or the Windows user-profile path — covered in Milestone 5.

---

### Task 1: Milestone 1 — Diagnostic redaction

**Files:**
- Create: `src/Vidora/Infrastructure/Logging/DiagnosticRedactor.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Logging/DiagnosticRedactorTests.cs`

**Interfaces:**
- Consumes: arbitrary process/log/diagnostic text and an optional explicit user-profile path for deterministic tests.
- Produces: `DiagnosticRedactor.Redact(string input, string? userProfilePath = null) : string`.

- [ ] Write failing tests for cookie headers, `--cookies`/`--cookies-from-browser`, token-like query keys (`token`, `access_token`, `auth`, `key`, `signature`), mixed case, percent encoding, and `C:\Users\name` replacement with `%USERPROFILE%`.
- [ ] Assert ordinary query parameters and non-profile paths remain readable, null/empty input is safe, and raw environment dumps are never accepted as a logging input elsewhere.
- [ ] Run the focused test class and verify RED because `DiagnosticRedactor` does not exist.
- [ ] Implement only the deterministic replacements required by the tests; use `[REDACTED]` for secret values.
- [ ] Run the focused tests and existing URL/process tests; verify GREEN.
- [ ] Commit as `feat: redact diagnostic secrets`.

---

### Task 2: Milestone 2 — Stable yt-dlp error classification

**Files:**
- Create: `src/Vidora/Infrastructure/Processes/YtDlpErrorClassifier.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/YtDlpErrorClassifierTests.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: `ProcessResult`, operation kind (`Discovery` or `Download`), and a cancellation flag.
- Produces: `YtDlpErrorClassifier.Classify(ProcessResult result, YtDlpOperation operation, bool wasCancelled = false) : VidoraError`.

- [ ] Write table-driven failing tests for every existing error code: `InvalidUrl`, `UnsupportedSource`, `Unavailable`, `AuthenticationRequired`, `NetworkFailure`, `DiskFailure`, `InsufficientSpace`, `DependencyMissing`, `DependencyInvalid`, `ExtractorOutdated`, `PostProcessingFailure`, `Cancelled`, and `Unknown`.
- [ ] Pin priority where messages overlap: cancellation, insufficient space, dependency/extractor, authentication, unavailable, network, disk/post-processing, invalid/unsupported URL, unknown.
- [ ] Assert each result has the stable code, resource message key, resource action key, retryability, and redacted technical detail containing exit code plus useful stderr.
- [ ] Run the focused tests and verify RED because the classifier does not exist.
- [ ] Implement all known yt-dlp/FFmpeg marker checks in this one file; do not add stderr checks to services.
- [ ] Add only missing resource entries required by the classifier and run focused/resource tests GREEN.
- [ ] Commit as `feat: classify media process errors`.

---

### Task 3: Milestone 3 — Rolling file logger

**Files:**
- Create: `src/Vidora/Infrastructure/Logging/FileLogger.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Logging/FileLoggerTests.cs`

**Interfaces:**
- Produces: `FileLogLevel`, immutable `FileLogEntry`, and `IAppLogger.WriteAsync(FileLogEntry entry, CancellationToken cancellationToken)`.
- Produces: `FileLogger(string logDirectory, TimeProvider timeProvider) : IAppLogger`.

- [ ] Write failing tests that parse written JSON-lines and assert timestamp, level, operation ID, component, event, optional error code, and already-redacted detail.
- [ ] Write failing rotation tests using real temporary files: crossing 2 MiB rolls the active file, retains at most `vidora.log` through `vidora.4.log`, and preserves complete lines.
- [ ] Add concurrent-write coverage and assert no interleaved/truncated JSON entry.
- [ ] Assert expected directory/file I/O failures are best-effort and do not throw back into the application.
- [ ] Implement a single in-process write gate and numbered rotation using BCL file APIs; call `DiagnosticRedactor` before serialization.
- [ ] Run logger/redactor tests GREEN and inspect one generated fixture for secrets.
- [ ] Commit as `feat: add rolling diagnostic logs`.

---

### Task 4: Milestone 4 — Discovery and download service integration

**Files:**
- Modify: `src/Vidora/Infrastructure/Processes/VideoDiscoveryService.cs`
- Modify: `src/Vidora/Infrastructure/Processes/DownloadService.cs`
- Modify: `tests/Vidora.Tests/Infrastructure/Processes/YtDlpDiscoveryParserTests.cs`
- Modify: `tests/Vidora.Tests/Infrastructure/Processes/DownloadServiceTests.cs`
- Modify: `src/Vidora/App.xaml.cs`

**Interfaces:**
- Consumes: `YtDlpErrorClassifier` and injected `IAppLogger`.
- Preserves: `IVideoDiscoveryService` and `IDownloadService` public method signatures.

- [ ] Write failing discovery tests proving nonzero exits use the classifier, use a nonempty operation ID, log the same stable error code, redact stderr, and keep explicit timeout/caller cancellation distinct.
- [ ] Write failing download tests proving request ID is the operation ID, classified errors reach queue/history unchanged, and cancellation is not logged/returned as `Unknown`.
- [ ] Add a logger-failure regression test proving the primary `VidoraException` or cancellation remains observable.
- [ ] Replace only the current nonzero-exit `Unknown` construction with centralized classification; preserve existing destination, dependency-start, output-path, and cleanup safety branches.
- [ ] Log stable structured events at failure/cancellation boundaries without logging command arguments, metadata JSON, or environment variables.
- [ ] Run discovery/download/queue/history test groups GREEN.
- [ ] Commit as `feat: integrate actionable process errors`.

---

### Task 5: Milestone 5 — Copy-diagnostic UX and application composition

**Files:**
- Modify: `src/Vidora/Features/Download/DownloadViewModel.cs`
- Modify: `src/Vidora/Features/Download/DownloadView.xaml`
- Modify: `src/Vidora/Features/History/HistoryViewModel.cs`
- Modify: `src/Vidora/Features/History/HistoryView.xaml`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`
- Modify: `tests/Vidora.Tests/Features/Download/DownloadViewModelDiscoveryTests.cs`
- Modify: `tests/Vidora.Tests/Features/Download/DownloadViewModelDownloadTests.cs`
- Modify: `tests/Vidora.Tests/Features/History/HistoryViewModelTests.cs`
- Modify: `tests/Vidora.Tests/Features/Shell/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: already-redacted `VidoraError.TechnicalDetail`, history task/attempt/status/error-code fields, and injected clipboard actions.
- Produces: explicit Copy technical details commands; no automatic clipboard or upload behavior.

- [ ] Write failing ViewModel tests: commands are unavailable without an error/failed history row, explicit execution copies stable fields, clipboard failure is contained, and copied output contains no secret/profile path.
- [ ] Write failing WPF binding tests proving localized Copy technical details buttons bind to the correct commands and do not replace the primary message/action text.
- [ ] Add the smallest commands and bindings; history diagnostics use task ID, attempt, timestamp, status, error code and redacted available fields rather than persisting new raw stderr.
- [ ] Construct one `FileLogger` at `%LOCALAPPDATA%\Vidora\logs`, inject it into both services, and inject `Clipboard.SetText` into the Download ViewModel in `App.xaml.cs`.
- [ ] Add missing localized error/action/copy labels; never display raw stderr as the main UI message.
- [ ] Run ViewModel, WPF shell, service, and logging tests GREEN.
- [ ] Commit as `feat: expose safe error diagnostics`.

---

### Task 6: Milestone 6 — Final verification and review

**Files:**
- Modify only files required to fix accepted review findings.

**Interfaces:**
- Consumes: completed Milestones 1-5.
- Produces: one verified Task 8 branch ready for integration.

- [ ] Run `dotnet format Vidora.sln --no-restore` on changed C# files and `git diff --check`.
- [ ] Run locked restore, Release build, and the full test suite.
- [ ] Inspect generated log fixtures and `git diff` for token/cookie/profile-path leakage and confirm no local log files are tracked.
- [ ] Perform one fresh whole-branch review focused on review-focus items; accept Critical/Important fixes in one pass and ledger every declined/out-of-scope item.
- [ ] Re-run the full suite after fixes, commit once, and stop before push/merge for user choice.

## Self-Review Result

- Spec coverage: classifier, redaction, 2 MiB/five-file rolling, structured fields, user copy action, no auto-upload, resources, and app composition all map to a milestone.
- Scope correction: `VidoraErrorCode` and `VidoraError` are already complete from Task 3, so Task 8 tests their stability rather than rewriting them.
- Scope correction: the parent plan omitted both XAML views and ViewModel tests needed for the promised Copy technical details UI; Milestone 5 includes them.
- Deliberately excluded: log viewer/search/export, telemetry, crash reporting, log-level settings, persistence of raw stderr in history, and third-party logging packages.
