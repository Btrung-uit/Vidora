# Vidora MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng Vidora 1.0, ứng dụng WPF local-first tải video công khai từ YouTube, Facebook, Instagram và TikTok với preview, quality preset, tiến trình, hủy, queue, history, cập nhật `yt-dlp` và gói cài Windows.

**Architecture:** Một WPF application project và một MSTest project. View/ViewModel chỉ gọi Core service interfaces; implementation dùng process boundary duy nhất cho `yt-dlp`/FFmpeg, JSON nguyên tử trong `%LOCALAPPDATA%`, và không có backend hay database server.

**Tech Stack:** .NET SDK 10.0.401, `net10.0-windows`, WPF, C#, CommunityToolkit.Mvvm 8.4.2, System.Text.Json, MSTest 4.4.1, Microsoft.NET.Test.Sdk 18.11.0, `yt-dlp`, FFmpeg, Inno Setup 7.1.1.

**Spec:** [`technical-design.md`](../../../technical-design.md)

## Global Constraints

- Chỉ hỗ trợ chính thức Windows 11 x64; publish self-contained `win-x64`.
- Không backend, cloud database, telemetry, tài khoản, cookie import hoặc đăng nhập mạng xã hội.
- MVP chỉ tải video công khai đơn lẻ; không playlist, livestream, audio-only, subtitle hoặc transcode.
- Queue FIFO với concurrency = 1; cancel/failure dừng queue cho đến khi người dùng Resume.
- Không ghi đè file hoàn chỉnh; không xóa file ngoài task temp directory thuộc Vidora.
- URL/path được truyền bằng `ProcessStartInfo.ArgumentList`, không ghép command string.
- Không lưu raw metadata JSON, password, cookie, token hoặc toàn bộ environment variables.
- Dùng `System.Text.Json` và atomic replace; không thêm SQLite/EF Core.
- Dùng manual constructor injection trong `App.xaml.cs`; không thêm DI container, MediatR, AutoMapper hoặc Serilog.
- Mọi chuỗi hiển thị cho người dùng được lấy từ `Resources/Strings.vi-VN.xaml`; không hard-code text trong ViewModel/service.
- Mỗi task thực hiện TDD cho logic không tầm thường, chạy `dotnet test` Release và commit độc lập.
- Không commit `yt-dlp.exe`, FFmpeg binaries, media tải thử, installer output hoặc local application data.

## Review Focus

1. URL có dấu `&`, dấu nháy hoặc payload giống shell vẫn phải được truyền thành một argument và không thực thi lệnh phụ — kiểm thử ở Task 3.
2. Hủy/đóng ứng dụng khi đang tải phải kết thúc toàn bộ process tree, giữ queue sau và không xóa file ngoài task temp root — kiểm thử ở Task 5.
3. `queue.json`, `history.json` hoặc `settings.json` bị cắt giữa chừng phải được cách ly thành `.corrupt-*` và ứng dụng vẫn khởi động — kiểm thử ở Task 6 và 7.
4. Đường dẫn có tiền tố giống temp root nhưng thực tế nằm ngoài root, kể cả reparse point có thể resolve, phải bị từ chối cleanup — kiểm thử ở Task 5.
5. Checksum/update `yt-dlp` sai hoặc download bị ngắt phải giữ nguyên executable cũ đang hoạt động — kiểm thử ở Task 9.

## Planned File Map

```text
Vidora.sln
global.json
Directory.Build.props
src/Vidora/
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)
├── Features/
│   ├── Shell/
│   ├── Download/
│   ├── History/
│   └── Settings/
├── Core/
│   ├── Models/
│   ├── Services/
│   └── Errors/
├── Infrastructure/
│   ├── Processes/
│   ├── Persistence/
│   ├── Files/
│   ├── Dependencies/
│   ├── Notifications/
│   └── Logging/
├── Properties/PublishProfiles/
└── Resources/
tests/Vidora.Tests/
├── Core/
├── Features/
├── Infrastructure/
└── Fixtures/
installer/Vidora.iss
docs/release-checklist.md
docs/platform-test-matrix.md
```

---

### Task 1: Bootstrap solution and navigable WPF shell

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Vidora.sln`
- Create: `src/Vidora/Vidora.csproj`
- Create: `src/Vidora/App.xaml`
- Create: `src/Vidora/App.xaml.cs`
- Create: `src/Vidora/MainWindow.xaml`
- Create: `src/Vidora/MainWindow.xaml.cs`
- Create: `src/Vidora/Features/Shell/ShellPage.cs`
- Create: `src/Vidora/Features/Shell/MainWindowViewModel.cs`
- Create: `src/Vidora/Resources/Strings.vi-VN.xaml`
- Create: `src/Vidora/Resources/Themes.xaml`
- Create: `tests/Vidora.Tests/Vidora.Tests.csproj`
- Create: `tests/Vidora.Tests/Features/Shell/MainWindowViewModelTests.cs`
- Modify: `.gitignore`
- Remove: `.gitkeep` files replaced by real files as each directory becomes populated

**Interfaces:**
- Consumes: none.
- Produces: `ShellPage { Download, History, Settings }`; `MainWindowViewModel.CurrentPage`; `NavigateCommand`.

- [ ] **Step 1: Scaffold the pinned solution and projects**

Run from repository root:

```powershell
dotnet new globaljson --sdk-version 10.0.401 --roll-forward latestPatch
dotnet new sln --name Vidora --format sln
dotnet new wpf --framework net10.0 --name Vidora --output src/Vidora
dotnet new mstest --framework net10.0 --name Vidora.Tests --output tests/Vidora.Tests
dotnet sln Vidora.sln add src/Vidora/Vidora.csproj tests/Vidora.Tests/Vidora.Tests.csproj
dotnet add src/Vidora/Vidora.csproj package CommunityToolkit.Mvvm --version 8.4.2
dotnet add tests/Vidora.Tests/Vidora.Tests.csproj package MSTest --version 4.4.1
dotnet add tests/Vidora.Tests/Vidora.Tests.csproj package Microsoft.NET.Test.Sdk --version 18.11.0
dotnet add tests/Vidora.Tests/Vidora.Tests.csproj reference src/Vidora/Vidora.csproj
```

Set `TargetFramework` to `net10.0-windows`, `SupportedOSPlatformVersion` to `10.0.22000.0`, enable nullable/implicit usings, and enable lock files in `Directory.Build.props`. Track both `packages.lock.json` files.

Run `dotnet restore Vidora.sln --use-lock-file` once after editing the project files to generate the initial lock files; later restores use `--locked-mode`.

- [ ] **Step 2: Write the failing shell navigation tests**

Add tests:

- `Constructor_DefaultsToDownloadPage` asserts `CurrentPage == ShellPage.Download`.
- `NavigateCommand_History_SetsHistoryPage`.
- `NavigateCommand_Settings_SetsSettingsPage`.
- `NavigateCommand_CurrentPage_DoesNotRecreateState` asserts reselecting the current page leaves the same value and raises no duplicate state transition.

- [ ] **Step 3: Run the shell tests and verify failure**

Run:

```powershell
dotnet test tests/Vidora.Tests/Vidora.Tests.csproj -c Release --filter FullyQualifiedName~MainWindowViewModelTests
```

Expected: FAIL because `ShellPage`/`MainWindowViewModel` do not exist.

- [ ] **Step 4: Implement the minimal shell**

Use CommunityToolkit source generators for observable state and commands. `MainWindow.xaml` has navigation for Download, History and Settings plus a content placeholder; no download behavior yet. `App.xaml.cs` manually creates `MainWindowViewModel` and `MainWindow`. Put shell strings in `Strings.vi-VN.xaml` from the start instead of hard-coding text that Task 10 must later move.

- [ ] **Step 5: Build and run all tests**

Run:

```powershell
dotnet restore Vidora.sln --locked-mode
dotnet build Vidora.sln -c Release --no-restore
dotnet test Vidora.sln -c Release --no-build
```

Expected: build succeeds with 0 warnings; four shell tests pass.

- [ ] **Step 6: Commit**

```powershell
git add -- .gitignore global.json Directory.Build.props Vidora.sln src/Vidora tests/Vidora.Tests
git commit -m "chore: bootstrap Vidora solution"
```

---

### Task 2: Process boundary and dependency readiness

**Files:**
- Create: `src/Vidora/Infrastructure/Processes/ProcessSpec.cs`
- Create: `src/Vidora/Infrastructure/Processes/ProcessOutputLine.cs`
- Create: `src/Vidora/Infrastructure/Processes/ProcessResult.cs`
- Create: `src/Vidora/Infrastructure/Processes/IProcessRunner.cs`
- Create: `src/Vidora/Infrastructure/Processes/ProcessRunner.cs`
- Create: `src/Vidora/Core/Models/DependencyStatus.cs`
- Create: `src/Vidora/Core/Models/DependencyComponentReport.cs`
- Create: `src/Vidora/Core/Models/DependencyReport.cs`
- Create: `src/Vidora/Core/Services/IDependencyService.cs`
- Create: `src/Vidora/Infrastructure/Dependencies/AppToolPaths.cs`
- Create: `src/Vidora/Infrastructure/Dependencies/DependencyService.cs`
- Create: `src/Vidora/Features/Settings/SettingsView.xaml`
- Create: `src/Vidora/Features/Settings/SettingsView.xaml.cs`
- Create: `src/Vidora/Features/Settings/SettingsViewModel.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/ProcessRunnerTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Dependencies/DependencyServiceTests.cs`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/MainWindow.xaml`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: manual composition root from Task 1.
- Produces:

```csharp
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        IProgress<ProcessOutputLine>? output,
        CancellationToken cancellationToken);
}

public interface IDependencyService
{
    Task<DependencyReport> CheckAsync(CancellationToken cancellationToken);
}
```

Task 9 extends this interface with `UpdateYtDlpAsync`; Task 2 does not expose an unfinished update action.

- [ ] **Step 1: Write failing process-runner tests**

Add tests using `%ComSpec%`:

- `RunAsync_CapturesStdoutAndStderrWithoutDeadlock`.
- `RunAsync_PreservesArgumentsContainingSpacesAndAmpersands`.
- `RunAsync_NonZeroExitCode_IsReturnedWithoutThrowing`.
- `RunAsync_AlreadyCancelled_DoesNotStartProcess`.

- [ ] **Step 2: Run process tests and verify failure**

Run `dotnet test ... --filter FullyQualifiedName~ProcessRunnerTests`.

Expected: FAIL because process types do not exist.

- [ ] **Step 3: Implement `ProcessRunner`**

Use `ArgumentList`, redirect stdout/stderr, read both streams concurrently, report lines with stream identity, and return exit code plus captured output. Do not classify yt-dlp errors here.

- [ ] **Step 4: Write failing dependency tests**

With a fake `IProcessRunner`, assert:

- both version commands succeed -> both components `Ready` and version strings parsed;
- missing executable -> `Missing`;
- timeout/nonzero output -> `Invalid`;
- settings state exposes readiness without blocking the caller thread.

- [ ] **Step 5: Implement dependency check and Settings status**

`AppToolPaths` returns `%LOCALAPPDATA%\Vidora\tools\yt-dlp.exe` and `<app-base>\tools\ffmpeg\ffmpeg.exe`. Use a 10-second linked cancellation timeout per version command.

- [ ] **Step 6: Verify and commit**

Run `dotnet test Vidora.sln -c Release` and expect all tests PASS.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: detect media tool readiness"
```

---

### Task 3: URL validation, metadata discovery, and preview

**Files:**
- Create: `src/Vidora/Core/Models/VideoPlatform.cs`
- Create: `src/Vidora/Core/Models/VideoFormat.cs`
- Create: `src/Vidora/Core/Models/VideoMetadata.cs`
- Create: `src/Vidora/Core/Errors/VidoraErrorCode.cs`
- Create: `src/Vidora/Core/Errors/VidoraError.cs`
- Create: `src/Vidora/Core/Errors/VidoraException.cs`
- Create: `src/Vidora/Core/Services/IVideoDiscoveryService.cs`
- Create: `src/Vidora/Infrastructure/Processes/SupportedUrlValidator.cs`
- Create: `src/Vidora/Infrastructure/Processes/YtDlpCommandFactory.cs`
- Create: `src/Vidora/Infrastructure/Processes/YtDlpDiscoveryParser.cs`
- Create: `src/Vidora/Infrastructure/Processes/VideoDiscoveryService.cs`
- Create: `src/Vidora/Features/Download/DownloadView.xaml`
- Create: `src/Vidora/Features/Download/DownloadView.xaml.cs`
- Create: `src/Vidora/Features/Download/DownloadViewModel.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/SupportedUrlValidatorTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/YtDlpDiscoveryParserTests.cs`
- Create: `tests/Vidora.Tests/Features/Download/DownloadViewModelDiscoveryTests.cs`
- Create: `tests/Vidora.Tests/Fixtures/Discovery/youtube-complete.json`
- Create: `tests/Vidora.Tests/Fixtures/Discovery/missing-optional-fields.json`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/MainWindow.xaml`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: `IProcessRunner`, `IDependencyService`.
- Produces:

```csharp
public interface IVideoDiscoveryService
{
    Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken);
}
```

`VideoMetadata` contains `SourceUrl`, `Platform`, `MediaId`, `Title`, nullable `Author`, `Duration`, `ThumbnailUrl`, and `IReadOnlyList<VideoFormat>`.

Define the complete error-code enum from the technical design now so later tasks do not replace temporary exception types. `VidoraException` carries a `VidoraError`; Task 8 adds centralized stderr classification and redaction without changing this contract.

- [ ] **Step 1: Write failing URL-validation tests**

Cover `youtube.com`, `youtu.be`, `facebook.com`, `fb.watch`, `instagram.com`, `tiktok.com`, `vm.tiktok.com`; reject non-HTTP(S), lookalike hosts, unsupported hosts, empty strings, playlists/profile URLs in MVP, and malformed URLs.

Add `BuildDiscoverySpec_UrlWithQuotesAndAmpersand_IsOneArgument` asserting the URL appears exactly once in `ArgumentList` after `--` and no shell is used.

- [ ] **Step 2: Write failing discovery parser/service tests**

Assert complete JSON maps required fields; optional fields become `null`; malformed JSON fails with a stable discovery exception; `--dump-single-json --skip-download --no-playlist -- <url>` is generated; dependency-not-ready prevents process start.

- [ ] **Step 3: Run targeted tests and verify failure**

Run `dotnet test ... --filter "FullyQualifiedName~SupportedUrlValidatorTests|FullyQualifiedName~YtDlpDiscoveryParserTests"`.

Expected: FAIL for missing types.

- [ ] **Step 4: Implement validator, command factory, parser, and service**

Parsing must use `System.Text.Json`; do not store raw JSON. Use a 30-second discovery timeout linked with the caller token.

- [ ] **Step 5: Add preview ViewModel behavior**

Tests assert state transitions `Empty -> Discovering -> Ready`, cancellation returns to `Empty`, failure becomes `Failed`, and a second discovery cancels/replaces the first. Implement Paste/Discover commands and preview bindings without download behavior.

- [ ] **Step 6: Verify and commit**

Run build plus all tests; manually launch once and verify tab navigation plus preview layout with design-time/sample state only.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: discover video metadata"
```

---

### Task 4: Quality selection, safe output, and one-video download

**Files:**
- Create: `src/Vidora/Core/Models/QualityPreset.cs`
- Create: `src/Vidora/Core/Models/DownloadStatus.cs`
- Create: `src/Vidora/Core/Models/DownloadRequest.cs`
- Create: `src/Vidora/Core/Models/DownloadProgress.cs`
- Create: `src/Vidora/Core/Models/DownloadResult.cs`
- Create: `src/Vidora/Core/Services/IDownloadService.cs`
- Create: `src/Vidora/Infrastructure/Processes/FormatSelector.cs`
- Create: `src/Vidora/Infrastructure/Processes/YtDlpProgressParser.cs`
- Create: `src/Vidora/Infrastructure/Processes/DownloadService.cs`
- Create: `src/Vidora/Infrastructure/Files/DestinationValidator.cs`
- Create: `src/Vidora/Infrastructure/Files/OutputNamePolicy.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/FormatSelectorTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/YtDlpProgressParserTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/DownloadServiceTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Files/DestinationValidatorTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Files/OutputNamePolicyTests.cs`
- Modify: `src/Vidora/Infrastructure/Processes/YtDlpCommandFactory.cs`
- Modify: `src/Vidora/Features/Download/DownloadView.xaml`
- Modify: `src/Vidora/Features/Download/DownloadViewModel.cs`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: `VideoMetadata`, `IProcessRunner`, command factory.
- Produces:

```csharp
public interface IDownloadService
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing format and progress tests**

Assert Best/1080/720/480 enforce maximum height, prefer MP4/H.264 + M4A/AAC, fall back truthfully to WebM/MKV, never request transcode, and parse nullable progress fields plus `VIDORA_RESULT:` final paths while ignoring noise/malformed lines.

- [ ] **Step 2: Write failing filesystem-policy tests**

Assert Vietnamese titles remain readable, invalid Windows characters are removed, reserved names are handled, long basenames are shortened, and collisions become ` (2)`, ` (3)` without overwriting.

- [ ] **Step 3: Implement selectors, parsers, destination validation, and output naming**

Use a write-probe to validate destination. `OutputNamePolicy` is deterministic and never changes extension independently of yt-dlp final output.

- [ ] **Step 4: Write failing download-service tests**

With fake runner/temp directory assert: progress is forwarded; nonzero exit fails; success without existing final path fails; success with final path returns `Completed`; URL and paths remain separate arguments; `--no-playlist`, `--newline`, progress marker and after-move marker are present.

- [ ] **Step 5: Implement one-video download and UI bindings**

Add folder picker, quality preset, Start/Cancel controls, progress, speed, bytes and ETA. Keep only one active download; queue is not introduced until Task 6.

- [ ] **Step 6: Verify and commit**

Run all tests and a local integration smoke test with a project-controlled small media URL if available; otherwise record the integration test as pending without weakening unit tests.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: download a public video"
```

---

### Task 5: Cancellation, task temp ownership, and safe shutdown

**Files:**
- Create: `src/Vidora/Infrastructure/Files/TaskWorkspaceService.cs`
- Create: `src/Vidora/Infrastructure/Files/ICanonicalPathResolver.cs`
- Create: `src/Vidora/Infrastructure/Files/WindowsCanonicalPathResolver.cs`
- Create: `src/Vidora/Core/Services/IActiveDownloadCoordinator.cs`
- Create: `src/Vidora/Core/Services/IAppShutdownCoordinator.cs`
- Create: `src/Vidora/Infrastructure/Processes/ActiveDownloadCoordinator.cs`
- Create: `src/Vidora/Infrastructure/Processes/AppShutdownCoordinator.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/ProcessRunnerCancellationTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Files/TaskWorkspaceServiceTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/AppShutdownCoordinatorTests.cs`
- Modify: `src/Vidora/Infrastructure/Processes/ProcessRunner.cs`
- Modify: `src/Vidora/Infrastructure/Processes/DownloadService.cs`
- Modify: `src/Vidora/Features/Download/DownloadViewModel.cs`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/MainWindow.xaml.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: `IDownloadService`, `IProcessRunner`.
- Produces: one active-operation coordinator; owned task directories at `%LOCALAPPDATA%\Vidora\temp\<task-id>`; shutdown outcome `ContinueRunning` or `ExitCompleted`.

- [ ] **Step 1: Write failing process cancellation test**

Start a child process that waits 30 seconds, cancel after it signals readiness, and assert `RunAsync` completes as cancelled within 5 seconds with no child process left alive.

- [ ] **Step 2: Write failing workspace containment tests**

Assert cleanup accepts only the exact canonical task directory beneath the configured temp root; rejects sibling-prefix paths such as `Vidora-temp-evil`; rejects `..`; and, using a fake `ICanonicalPathResolver`, rejects a reparse-point path whose final target resolves outside root.

- [ ] **Step 3: Implement cancellation and owned cleanup**

Call `Kill(entireProcessTree: true)`, wait with bounded timeout, and delete only paths registered by `TaskWorkspaceService`. Never wildcard-delete `.part` files from destination folders.

- [ ] **Step 4: Write failing shutdown tests**

Assert no active download exits immediately; choosing continue leaves process untouched; choosing exit cancels, waits, marks interrupted result, persists through callback, and times out without hanging indefinitely.

- [ ] **Step 5: Implement shutdown confirmation flow**

`MainWindow` delegates close handling to `IAppShutdownCoordinator`; code-behind contains only window lifecycle wiring.

- [ ] **Step 6: Verify and commit**

Run all tests twice to catch process cleanup flakiness; verify no test child process remains.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: cancel downloads safely"
```

---

### Task 6: FIFO queue, settings, and atomic persistence

**Files:**
- Create: `src/Vidora/Core/Models/DownloadQueueItem.cs`
- Create: `src/Vidora/Core/Models/AppSettings.cs`
- Create: `src/Vidora/Core/Services/IDownloadQueueService.cs`
- Create: `src/Vidora/Infrastructure/Persistence/VersionedDocument.cs`
- Create: `src/Vidora/Infrastructure/Persistence/AtomicJsonStore.cs`
- Create: `src/Vidora/Infrastructure/Persistence/QueueDocument.cs`
- Create: `src/Vidora/Infrastructure/Persistence/SettingsDocument.cs`
- Create: `src/Vidora/Infrastructure/Persistence/QueueStore.cs`
- Create: `src/Vidora/Infrastructure/Persistence/SettingsStore.cs`
- Create: `src/Vidora/Infrastructure/Processes/DownloadQueueService.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Persistence/AtomicJsonStoreTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Persistence/QueueStoreTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/DownloadQueueServiceTests.cs`
- Modify: `src/Vidora/Features/Download/DownloadView.xaml`
- Modify: `src/Vidora/Features/Download/DownloadViewModel.cs`
- Modify: `src/Vidora/Features/Settings/SettingsViewModel.cs`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: `IDownloadService`, `TimeProvider`, shutdown coordinator callback.
- Produces:

```csharp
public interface IDownloadQueueService
{
    IReadOnlyList<DownloadQueueItem> Items { get; }
    event EventHandler? Changed;
    Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task CancelActiveAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing atomic-store tests**

Assert first save/load, replace existing file, interrupted temp file does not replace good data, malformed JSON is renamed `.corrupt-<timestamp>`, schema mismatch returns controlled failure, and startup continues with defaults.

- [ ] **Step 2: Implement `AtomicJsonStore<TDocument>`**

Serialize to same-directory temp, flush, then replace/move. Inject `TimeProvider` for deterministic corrupt filenames. Do not swallow IO exceptions that mean the user cannot persist state.

- [ ] **Step 3: Write failing queue tests**

Cover FIFO/concurrency 1, snapshot quality/folder, remove pending only, failure stops queue, cancel stops queue, Resume starts next, duplicate URL warning state, and no automatic retry.

- [ ] **Step 4: Write failing queue restore tests**

Persist Pending/Downloading items, reload, assert Downloading becomes Interrupted, Pending remains Pending, and queue does not auto-start.

- [ ] **Step 5: Implement queue service, stores, and queue UI**

Persist after every state transition. Remember the last valid destination in settings. Use `Changed` only for invalidation; ViewModel refreshes a read-only projection on the UI dispatcher.

- [ ] **Step 6: Verify and commit**

Run all tests and manually queue two fake/test items to verify only one active row.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: persist the download queue"
```

---

### Task 7: Download history

**Files:**
- Create: `src/Vidora/Core/Models/DownloadHistoryItem.cs`
- Create: `src/Vidora/Core/Services/IHistoryService.cs`
- Create: `src/Vidora/Infrastructure/Persistence/HistoryDocument.cs`
- Create: `src/Vidora/Infrastructure/Persistence/HistoryService.cs`
- Create: `src/Vidora/Features/History/HistoryView.xaml`
- Create: `src/Vidora/Features/History/HistoryView.xaml.cs`
- Create: `src/Vidora/Features/History/HistoryViewModel.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Persistence/HistoryServiceTests.cs`
- Create: `tests/Vidora.Tests/Features/History/HistoryViewModelTests.cs`
- Modify: `src/Vidora/Infrastructure/Processes/DownloadQueueService.cs`
- Modify: `src/Vidora/MainWindow.xaml`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: `AtomicJsonStore`, queue terminal events.
- Produces:

```csharp
public interface IHistoryService
{
    Task<IReadOnlyList<DownloadHistoryItem>> LoadAsync(CancellationToken cancellationToken);
    Task AddAsync(DownloadHistoryItem item, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing history service tests**

Assert completed/failed/cancelled/interrupted records preserve timestamp, URL, title, platform, quality, status, final path and error code; cap at 1,000 newest; malformed history recovers; clearing history never touches referenced media.

- [ ] **Step 2: Implement history persistence and queue integration**

Add one history record per terminal attempt. Missing output files are computed/displayed as missing; do not mutate history merely because a file was deleted externally.

- [ ] **Step 3: Write failing history ViewModel tests**

Assert Open File disabled when missing, Open Folder uses existing parent, Copy URL returns source URL, Retry creates a new queue attempt with the captured quality/folder, and Clear requires confirmation.

- [ ] **Step 4: Implement History view and actions**

Use OS shell only for user-triggered open actions; validate target existence immediately before launch.

- [ ] **Step 5: Verify and commit**

Run all tests; manually delete a test output and verify the history row becomes Missing without crashing.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: add local download history"
```

---

### Task 8: Stable errors, redacted diagnostics, and rolling logs

**Files:**
- Modify: `src/Vidora/Core/Errors/VidoraErrorCode.cs`
- Modify: `src/Vidora/Core/Errors/VidoraError.cs`
- Create: `src/Vidora/Infrastructure/Processes/YtDlpErrorClassifier.cs`
- Create: `src/Vidora/Infrastructure/Logging/DiagnosticRedactor.cs`
- Create: `src/Vidora/Infrastructure/Logging/FileLogger.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Processes/YtDlpErrorClassifierTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Logging/DiagnosticRedactorTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Logging/FileLoggerTests.cs`
- Modify: `src/Vidora/Infrastructure/Processes/VideoDiscoveryService.cs`
- Modify: `src/Vidora/Infrastructure/Processes/DownloadService.cs`
- Modify: `src/Vidora/Features/Download/DownloadViewModel.cs`
- Modify: `src/Vidora/Features/History/HistoryViewModel.cs`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: process exit/output and operation IDs.
- Produces: centralized classification into the stable `VidoraError` contract created in Task 3, with message resource key, action resource key, redacted technical detail and retryable flag.

- [ ] **Step 1: Write failing error-classifier tests**

Fixtures cover InvalidUrl, UnsupportedSource, Unavailable, AuthenticationRequired, NetworkFailure, DiskFailure, InsufficientSpace, DependencyMissing/Invalid, ExtractorOutdated, PostProcessingFailure, Cancelled and Unknown. Cancellation must never become Unknown/Failed.

- [ ] **Step 2: Implement centralized classification**

Match known yt-dlp/FFmpeg patterns only in this classifier. Keep Unknown as a valid outcome; do not scatter stderr string checks across services.

- [ ] **Step 3: Write failing redaction/log tests**

Assert removal of cookie/token query values, cookie headers, user profile path from copied diagnostic, raw environment data, and log rotation at 2 MB with at most five files.

- [ ] **Step 4: Implement logger and user-facing error states**

Log timestamp, level, operation ID, component, event and error code. UI shows localized message/action and offers Copy technical details; never auto-upload.

- [ ] **Step 5: Verify and commit**

Run all tests and inspect a generated log fixture for secrets.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: report actionable download errors"
```

---

### Task 9: Safe `yt-dlp` installation and update

**Files:**
- Create: `src/Vidora/Infrastructure/Dependencies/YtDlpReleaseInfo.cs`
- Create: `src/Vidora/Infrastructure/Dependencies/IYtDlpReleaseClient.cs`
- Create: `src/Vidora/Infrastructure/Dependencies/GitHubYtDlpReleaseClient.cs`
- Create: `src/Vidora/Infrastructure/Dependencies/YtDlpUpdater.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Dependencies/GitHubYtDlpReleaseClientTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Dependencies/YtDlpUpdaterTests.cs`
- Modify: `src/Vidora/Infrastructure/Dependencies/DependencyService.cs`
- Modify: `src/Vidora/Core/Services/IDependencyService.cs`
- Modify: `src/Vidora/Features/Settings/SettingsViewModel.cs`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`

**Interfaces:**
- Consumes: `HttpClient`, `IProcessRunner`, `AppToolPaths`, active-download state.
- Produces: `IDependencyService.UpdateYtDlpAsync(CancellationToken)` and explicit first-run setup flow.

- [ ] **Step 1: Write failing release-client tests**

With a fake `HttpMessageHandler`, assert requests only target official `https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest` and official release asset URLs, set a User-Agent, parse `yt-dlp.exe` plus `SHA2-256SUMS`, and reject redirects/asset hosts outside the approved GitHub hosts.

- [ ] **Step 2: Implement release discovery**

Return immutable version, executable URI, checksum manifest URI and expected SHA-256. Do not accept a URL from UI/config.

- [ ] **Step 3: Write failing updater tests**

Assert valid checksum + successful `--version` atomically installs; checksum mismatch, network interruption, invalid version command and replacement failure leave the old executable byte-for-byte unchanged; update is rejected while a download is active.

- [ ] **Step 4: Implement temp download, verification, and atomic replacement**

Download under Vidora temp, hash with SHA-256, validate against manifest, execute `--version`, then replace. Preserve old binary until every check passes.

- [ ] **Step 5: Implement Settings/first-run UX**

Show installed version, latest version, Check/Update actions and recovery instructions. Never update silently.

- [ ] **Step 6: Verify and commit**

Run all tests. Perform one manual download of the official checksum manifest and binary in a disposable temp directory; do not commit the binary.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: update yt-dlp safely"
```

---

### Task 10: Theme, notifications, localization, and accessibility pass

**Files:**
- Create: `src/Vidora/Core/Services/IThemeService.cs`
- Create: `src/Vidora/Core/Services/INotificationService.cs`
- Create: `src/Vidora/Infrastructure/Notifications/WindowsNotificationService.cs`
- Modify: `src/Vidora/Resources/Strings.vi-VN.xaml`
- Modify: `src/Vidora/Resources/Themes.xaml`
- Create: `tests/Vidora.Tests/Features/Settings/SettingsViewModelTests.cs`
- Create: `tests/Vidora.Tests/Infrastructure/Notifications/WindowsNotificationServiceTests.cs`
- Modify: `src/Vidora/Vidora.csproj`
- Modify: `src/Vidora/App.xaml`
- Modify: `src/Vidora/App.xaml.cs`
- Modify: `src/Vidora/MainWindow.xaml`
- Modify: all feature XAML/ViewModels containing user-facing strings

**Interfaces:**
- Consumes: persisted settings, queue terminal events.
- Produces: Light/Dark/System theme service; opt-out notification service using `System.Windows.Forms.NotifyIcon`.

- [ ] **Step 1: Write failing settings/notification tests**

Assert default System theme, persisted explicit theme, notification opt-out, completion/failure notification only when window is not focused, and disposal removes tray resources.

- [ ] **Step 2: Implement theme and notification services**

Enable `<UseWindowsForms>true</UseWindowsForms>` only for built-in `NotifyIcon`; do not add a notification SDK. Dispose icon on application exit.

- [ ] **Step 3: Audit user-facing strings and complete the resource catalog**

Verify every earlier screen already uses resources, add any missing keys, and keep stable keys matching `VidoraError.UserMessageKey`/`SuggestedActionKey`. Keep Vietnamese as the only shipped language in MVP while preserving resource-based localization.

- [ ] **Step 4: Perform accessibility implementation pass**

Add labels/access keys, automation names, keyboard order, visible focus, non-color status text and accessible progress values. Remove nonessential animation.

- [ ] **Step 5: Verify and commit**

Run all tests/build. Manually test keyboard-only navigation, 125%/150% scaling, Windows High Contrast, Light/Dark/System, notification opt-out and screen-reader names.

```powershell
git add -- src/Vidora tests/Vidora.Tests
git commit -m "feat: polish the Windows experience"
```

---

### Task 11: Packaging, notices, release validation, and README completion

**Files:**
- Create: `installer/Vidora.iss`
- Create: `LICENSE`
- Create: `THIRD-PARTY-NOTICES.md`
- Create: `docs/release-checklist.md`
- Create: `docs/platform-test-matrix.md`
- Create: `src/Vidora/Properties/PublishProfiles/win-x64.pubxml`
- Modify: `.gitignore`
- Modify: `README.md`
- Modify: `src/Vidora/Vidora.csproj`

**Interfaces:**
- Consumes: completed application, pinned FFmpeg build/license, all acceptance criteria.
- Produces: self-contained `win-x64` publish folder, Inno Setup 7 installer, portable ZIP and SHA-256 checksums.

- [ ] **Step 1: Add release metadata and notices**

Add MIT license for Vidora. Record exact CommunityToolkit.Mvvm, `yt-dlp`, FFmpeg build source/configuration/license and Inno Setup notices. Do not package FFmpeg until the selected build's redistribution obligations are documented.

- [ ] **Step 2: Configure deterministic Release publish**

Publish self-contained `win-x64`, no trimming, no single-file requirement, version from MSBuild property. Exclude local data, logs, downloaded `yt-dlp.exe`, test media and secrets.

Run:

```powershell
dotnet publish src/Vidora/Vidora.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Expected: app publish succeeds and includes no user/local test data.

- [ ] **Step 3: Create Inno Setup 7 installer and portable ZIP**

Installer is per-user by default, installs app + pinned FFmpeg + licenses, creates Start Menu shortcut, supports clean uninstall, and leaves `%LOCALAPPDATA%\Vidora` unless the user explicitly chooses to remove data. Portable ZIP contains the same publish output and notices.

- [ ] **Step 4: Run automated release verification**

```powershell
dotnet restore Vidora.sln --locked-mode
dotnet build Vidora.sln -c Release --no-restore
dotnet test Vidora.sln -c Release --no-build
dotnet publish src/Vidora/Vidora.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Expected: all commands exit 0, 0 warnings, all tests pass.

- [ ] **Step 5: Execute manual release matrix**

On a clean Windows 11 x64 environment, verify installer/uninstaller, first-run `yt-dlp` setup, dependency versions, Vietnamese/space paths, collisions, cancel/shutdown/restore, corrupt persistence recovery, missing dependency, no-write destination, and one successful public sample on each of YouTube, Facebook, Instagram and TikTok. Record date and exact tool versions in `docs/platform-test-matrix.md`.

- [ ] **Step 6: Update README with verified evidence only**

Replace “implementation has not started” with actual status; add install/run instructions, screenshots and limitations only after the corresponding artifact/manual check exists. Do not claim support for a platform that failed the release matrix.

- [ ] **Step 7: Inspect staged release docs and commit**

```powershell
git add -- .gitignore README.md LICENSE THIRD-PARTY-NOTICES.md installer docs src/Vidora/Properties src/Vidora/Vidora.csproj
git diff --cached --name-status
git diff --cached --check
git commit -m "build: package Vidora for Windows"
```

## Final Verification

- [ ] Run `git status --short` and confirm only intentional local artifacts are ignored/untracked.
- [ ] Run `dotnet test Vidora.sln -c Release` from a fresh clone after restoring locked packages.
- [ ] Confirm no credentials, cookies, media files, local logs, executables downloaded at runtime, or `%LOCALAPPDATA%` content are tracked.
- [ ] Confirm every technical-design section maps to at least one implemented task and test/manual check.
- [ ] Confirm README claims match the dated platform matrix and distributable artifact.
- [ ] Run a final whole-branch code review focused on process injection, unsafe deletion, update integrity, cancellation leaks and corrupted persistence.

## Toolchain References

- [.NET 10 support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [CommunityToolkit.Mvvm 8.4.2](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2)
- [MSTest 4.4.1](https://www.nuget.org/packages/MSTest/4.4.1)
- [Microsoft.NET.Test.Sdk 18.11.0](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.11.0)
- [Inno Setup 7](https://jrsoftware.org/isinfo.php)
- [`yt-dlp` releases](https://github.com/yt-dlp/yt-dlp/releases)
