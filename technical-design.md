# Vidora Technical Design

**Trạng thái:** Đã chốt để lập kế hoạch triển khai

**Phiên bản tài liệu:** 1.0

**Ngày:** 2026-10-30

**Phạm vi:** MVP desktop local cho YouTube, Facebook, Instagram và TikTok

## 0. Mục tiêu và nguyên tắc

Vidora là ứng dụng WPF chạy cục bộ, giúp người dùng tải video công khai về thư mục do họ chọn mà không phụ thuộc các website tải video trung gian. Vidora quản lý trải nghiệm, hàng đợi, lịch sử, trạng thái và lỗi; `yt-dlp` đảm nhiệm trích xuất/tải; FFmpeg đảm nhiệm ghép hoặc remux media.

Các nguyên tắc chi phối thiết kế:

- Local-first: URL, metadata và media không đi qua server của Vidora.
- Một tiến trình tải tại một thời điểm trong MVP.
- Không ghi đè hoặc xóa file người dùng ngoài những file tạm mà tác vụ hiện tại sở hữu.
- Không lưu mật khẩu, cookie hoặc session mạng xã hội.
- Không tự viết extractor cho từng nền tảng; cập nhật `yt-dlp` để thích nghi thay đổi bên ngoài.
- Không transcode trong MVP; chỉ tải, merge hoặc remux.
- Ưu tiên thư viện chuẩn và số dependency tối thiểu.
- Mọi hoạt động chậm đều bất đồng bộ và có `CancellationToken`.

## 1. Đối tượng và môi trường chạy

### Quyết định

- Hệ điều hành hỗ trợ chính thức: Windows 11 x64 còn trong vòng đời hỗ trợ.
- Windows 10 phổ thông không thuộc phạm vi hỗ trợ chính thức. Không chặn cứng nếu ứng dụng vẫn chạy, nhưng không phát hành cam kết hoặc ma trận kiểm thử cho môi trường này.
- Runtime: .NET 10 LTS, target framework `net10.0-windows`.
- UI framework: WPF.
- Ngôn ngữ MVP: tiếng Việt; chuỗi UI phải đi qua resource để có thể thêm tiếng Anh sau MVP.
- Đối tượng: người dùng phổ thông, không cần biết `yt-dlp`, FFmpeg, codec hoặc command line.
- Kiến trúc CPU phát hành: `win-x64`.
- Kiểu publish: self-contained để máy người dùng không phải cài .NET Runtime.

### Lý do

.NET 10 là LTS đang được hỗ trợ đến 2028-11-14. WPF chỉ chạy trên Windows. Đến thời điểm thiết kế, .NET 10 chỉ hỗ trợ chính thức các bản Windows 10 thuộc nhánh LTSC/Enterprise; vì vậy Vidora không tuyên bố hỗ trợ Windows 10 phổ thông.

### Cấu hình publish

- Release chính: self-contained `win-x64` trong installer.
- Portable: thư mục ZIP self-contained, là artifact phụ sau khi installer ổn định.
- Không bật trimming trong MVP vì WPF, binding và reflection có thể bị ảnh hưởng.
- Không yêu cầu single-file cho bản đầu. `yt-dlp`, FFmpeg và license vẫn là file rời; single-file không làm gói phân phối thực sự thành một file.

## 2. Phân phối `yt-dlp` và FFmpeg

### Bố trí công cụ

```text
%LOCALAPPDATA%\Vidora\
├── tools\
│   └── yt-dlp.exe
├── data\
├── logs\
└── temp\

<installation-directory>\
└── tools\ffmpeg\
    ├── ffmpeg.exe
    ├── ffprobe.exe
    └── LICENSES...
```

### `yt-dlp`

- Không đóng băng một bản `yt-dlp` trong installer lâu dài.
- Lần thiết lập đầu tiên tải `yt-dlp.exe` từ release chính thức của repository `yt-dlp/yt-dlp` qua HTTPS.
- File được tải vào tên tạm, kiểm tra checksum SHA-256 từ manifest release tương ứng, chạy `--version`, rồi mới thay thế bản hiện tại.
- Update là hành động rõ ràng trong Settings; ứng dụng có thể báo có bản mới nhưng không tự thay binary khi đang tải.
- Chỉ cho phép nguồn release được hard-code. UI không nhận URL binary tùy ý.
- Nếu update thất bại, giữ nguyên bản đang hoạt động; không để file nửa chừng ở vị trí executable chính.

### FFmpeg

- Installer đóng gói một build FFmpeg x64 đã pin phiên bản và ghi rõ nguồn, cấu hình license và checksum.
- Vidora chỉ gọi executable như một process riêng; không link thư viện FFmpeg vào ứng dụng.
- Không tự update FFmpeg trong MVP.
- Release gate bắt buộc: kèm license, attribution và nghĩa vụ phân phối tương ứng với build được chọn trong `THIRD-PARTY-NOTICES.md`.
- `DependencyService` kiểm tra sự tồn tại và chạy `ffmpeg -version` trước khi cho tải.

### Trạng thái dependency

```csharp
public enum DependencyStatus
{
    Ready,
    Missing,
    Invalid,
    UpdateAvailable,
    Updating
}
```

Ứng dụng chỉ chuyển sang `Ready` khi cả `yt-dlp --version` và `ffmpeg -version` kết thúc thành công trong timeout.

## 3. Cấu trúc giao diện

### Điều hướng

Vidora dùng một `MainWindow` với ba khu vực điều hướng, không mở nhiều cửa sổ nghiệp vụ:

```text
MainWindow
├── Download
│   ├── URL input + Paste
│   ├── Discovery/preview
│   ├── Quality + destination
│   ├── Active item
│   └── Queue
├── History
└── Settings
```

Dialog hệ thống chỉ dùng cho chọn thư mục, xác nhận đóng ứng dụng khi đang tải và thông báo lỗi không thể phục hồi.

### Trạng thái màn hình Download

```text
Empty -> Discovering -> Ready -> Queued
              |          |        |
              v          v        v
            Failed     Failed   Downloading
                                  |  |  |
                                  v  v  v
                           Completed Cancelled Failed
```

- `Empty`: cho nhập URL; nút tải bị khóa.
- `Discovering`: khóa URL hiện tại; cho phép hủy discovery.
- `Ready`: hiển thị preview và cho chọn quality/folder.
- `Queued`: cấu hình đã được snapshot vào item; thay đổi form không sửa item cũ.
- `Downloading`: chỉ item active hiển thị progress realtime.
- `Completed`, `Cancelled`, `Failed`: cung cấp hành động phù hợp, không dùng một nút chung mơ hồ.

### Theme và accessibility

- Dùng WPF `ThemeMode`/resource của .NET 10 cho Light, Dark và System; không thêm theme framework ở MVP.
- Không truyền đạt trạng thái chỉ bằng màu; luôn có text và icon.
- Mọi control tương tác có label/access key và focus order hợp lý.
- Tiến trình có accessible name; animation không cần thiết bị loại bỏ.
- Thông báo nền dùng `System.Windows.Forms.NotifyIcon`/balloon notification có sẵn trong Windows desktop stack, tránh thêm SDK notification riêng. Người dùng có thể tắt trong Settings.

## 4. Kiến trúc code

### Solution

```text
Vidora.sln
├── src\Vidora\Vidora.csproj
└── tests\Vidora.Tests\Vidora.Tests.csproj
```

Chỉ có một application project và một test project. Không tạo project `Domain`, `Application`, `Infrastructure` riêng khi quy mô chưa cần.

### Cấu trúc application project

```text
src\Vidora\
├── App.xaml(.cs)                    # composition root, lifecycle
├── MainWindow.xaml(.cs)
├── Features\
│   ├── Download\                   # view, viewmodel, UI models
│   ├── History\
│   └── Settings\
├── Core\
│   ├── Models\                     # request/result/state models
│   ├── Services\                   # service interfaces
│   └── Errors\                     # stable error model
├── Infrastructure\
│   ├── Processes\                  # Process runner + yt-dlp protocol
│   ├── Persistence\                # atomic JSON stores
│   ├── Files\                      # path/output/temp safety
│   ├── Dependencies\               # yt-dlp/FFmpeg validation/update
│   └── Logging\
└── Resources\                      # strings, icons, themes
```

### Dependency direction

```text
Views -> ViewModels -> Core service interfaces
                          ^
                          |
                  Infrastructure implementations
```

- View không chạy process và không đọc/ghi JSON.
- ViewModel không biết câu lệnh CLI hoặc đường dẫn binary.
- Infrastructure không tham chiếu View/ViewModel.
- `App.xaml.cs` tạo concrete services và truyền dependency qua constructor; không thêm DI container trong MVP.

### Dependency NuGet

- `CommunityToolkit.Mvvm`: `ObservableObject`, `RelayCommand`, `AsyncRelayCommand` và source generators.
- Test stack: MSTest + `Microsoft.NET.Test.Sdk`.
- Không dùng MediatR, AutoMapper, EF Core, SQLite, Serilog hoặc generic host ở MVP.
- Phiên bản package được pin qua `packages.lock.json`; chỉ cập nhật có chủ đích.

## 5. Hợp đồng với `yt-dlp`

### Process boundary

Mọi process ngoài ứng dụng đi qua một abstraction duy nhất:

```csharp
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        IProgress<ProcessOutputLine>? output,
        CancellationToken cancellationToken);
}
```

Quy tắc bắt buộc:

- Dùng `ProcessStartInfo.ArgumentList`; không nối URL hoặc đường dẫn thành command string.
- `UseShellExecute = false`, redirect stdout/stderr, không mở console.
- Đọc stdout và stderr đồng thời để tránh deadlock.
- Khi hủy: `Kill(entireProcessTree: true)`, chờ process kết thúc với timeout, rồi mới cleanup file tạm đã đăng ký.
- Discovery có timeout riêng; download không có timeout tổng nhưng hỗ trợ cancellation.
- Exit code khác 0 không được xem là kết quả thành công dù có file output.

### Discovery

`IVideoDiscoveryService` nhận một URL và trả model nội bộ:

```csharp
public interface IVideoDiscoveryService
{
    Task<VideoMetadata> DiscoverAsync(
        string url,
        CancellationToken cancellationToken);
}
```

Lệnh logic sử dụng `--dump-single-json`, `--skip-download` và `--no-playlist`. Parser chỉ đọc JSON; không parse text trang web. Domain allow-list dùng để phản hồi sớm, còn kết quả từ `yt-dlp` mới là bằng chứng URL thực sự được hỗ trợ.

`VideoMetadata` chỉ giữ dữ liệu UI cần: source URL, extractor/platform, media ID, title, author, duration, thumbnail URL và danh sách format tối thiểu cần cho quality policy. Field thiếu giữ `null`.

### Quality policy

```csharp
public enum QualityPreset
{
    Best,
    P1080,
    P720,
    P480
}
```

- Preset giới hạn chiều cao tối đa, không yêu cầu đúng tuyệt đối.
- Ưu tiên cặp video MP4/H.264 + audio M4A/AAC có thể merge/remux thành MP4.
- Nếu không có, chọn tổ hợp tốt nhất không vượt giới hạn và giữ container trung thực như WebM/MKV.
- Không chọn format yêu cầu transcode.
- UI hiển thị resolution/container thực tế sau khi `yt-dlp` chốt output.
- Logic tạo format selector nằm trong một `FormatSelector`, không rải chuỗi selector trong ViewModel.

### Progress protocol

- Dùng `--newline` và `--progress-template` có prefix riêng, ví dụ `VIDORA_PROGRESS:`.
- Dùng `--print after_move:` có prefix `VIDORA_RESULT:` để lấy final path sau post-processing.
- Parser bỏ qua line không có prefix; line không hợp lệ được log ở mức debug nhưng không làm crash download.
- Progress field có thể thiếu; model dùng nullable cho percent, speed, total bytes và ETA.

```csharp
public sealed record DownloadProgress(
    double? Percent,
    long? DownloadedBytes,
    long? TotalBytes,
    double? BytesPerSecond,
    TimeSpan? Eta,
    string Stage);
```

`DownloadService` chỉ trả `Completed` khi process thành công và final path tồn tại.

## 6. Mô hình dữ liệu và lưu trữ

### Model cốt lõi

```csharp
public enum DownloadStatus
{
    Pending,
    Downloading,
    Completed,
    Cancelled,
    Failed,
    Interrupted
}

public sealed record DownloadRequest(
    Guid Id,
    Uri SourceUrl,
    VideoMetadata Metadata,
    QualityPreset Quality,
    string DestinationDirectory,
    DateTimeOffset CreatedAt);
```

`DownloadQueueItem` chứa request bất biến và state/progress thay đổi. `DownloadHistoryItem` là snapshot kết quả, không giữ toàn bộ raw JSON từ `yt-dlp`.

### File lưu trữ

```text
%LOCALAPPDATA%\Vidora\data\settings.json
%LOCALAPPDATA%\Vidora\data\queue.json
%LOCALAPPDATA%\Vidora\data\history.json
```

- Dùng `System.Text.Json`; không dùng database trong MVP.
- Mỗi file có `schemaVersion` ở root.
- Ghi nguyên tử: serialize sang file tạm cùng thư mục, flush, rồi replace/move trong cùng volume.
- Nếu file lỗi: đổi tên thành `.corrupt-<timestamp>`, khởi tạo dữ liệu mặc định và hiển thị cảnh báo phục hồi; không làm ứng dụng không khởi động được.
- Settings lưu ngay sau thay đổi ổn định; queue lưu sau mọi state transition; history lưu sau thêm/xóa/clear.
- Giới hạn history mặc định 1.000 mục; thêm mục mới thì loại record cũ nhất, không xóa media.
- `Clear history` chỉ thay đổi `history.json`.

### Khôi phục queue

- Khi đóng bình thường hoặc crash, item `Downloading` được đọc lại thành `Interrupted`.
- `Pending` được giữ nhưng queue ở trạng thái stopped.
- Ứng dụng không tự tải khi khởi động; người dùng phải chọn Resume queue.
- Item nhớ nguyên quality và destination đã snapshot lúc enqueue.

## 7. Quy tắc filesystem

### Output naming

Template logic:

```text
<sanitized-title> [<platform>-<media-id>].<actual-extension>
```

- Ưu tiên để `yt-dlp` sanitize filename với chế độ tương thích Windows.
- Giữ extension thực tế từ final path; không đổi extension bằng tay.
- Không ghi đè: bật chính sách no-overwrite và kiểm tra collision trước khi start.
- Nếu vẫn collision, thêm ` (2)`, ` (3)`... trước extension.
- Giới hạn basename để toàn đường dẫn nằm trong giới hạn an toàn của API/file tooling được dùng.

### Ownership của file tạm

- Mỗi task có thư mục `%LOCALAPPDATA%\Vidora\temp\<task-id>\`.
- Chỉ task tương ứng được đăng ký xóa thư mục này.
- Download output cuối cùng nằm trực tiếp trong destination của người dùng; temp/intermediate dùng task temp khi công cụ cho phép.
- Cleanup nhận đường dẫn tuyệt đối đã canonicalize và xác nhận nó nằm dưới Vidora temp root trước khi xóa.
- Không dùng wildcard để xóa media hoặc file `.part` trong thư mục người dùng.

### Validation trước tải

- Destination tồn tại hoặc có thể được tạo sau xác nhận.
- Có quyền ghi thông qua probe file riêng rồi xóa probe.
- Kiểm tra dung lượng khi metadata cung cấp ước lượng; nếu không biết, thông báo thay vì chặn.
- Đường dẫn UNC/network và removable drive không bị cấm, nhưng lỗi mất kết nối được phân loại `DiskFailure`.

## 8. Queue, retry và vòng đời ứng dụng

### Queue policy

- FIFO, concurrency = 1.
- Item chỉ được enqueue sau discovery thành công.
- Remove chỉ áp dụng cho `Pending`, `Failed`, `Cancelled` hoặc `Interrupted`.
- Duplicate URL được phép nhưng UI cảnh báo nếu URL đang có trong queue hoặc đã hoàn thành trong history.
- Failure của một item không xóa các item sau. Queue chuyển sang stopped và hỏi người dùng Continue hoặc Retry để tránh tải hàng loạt ngoài ý muốn.
- Cancel item active dừng process và cũng dừng queue; người dùng bấm Resume để chạy item kế tiếp.

### Retry

- Retry tạo một lần chạy mới cho cùng item ID, giữ URL, quality và destination.
- `AttemptCount` tăng; error cũ được lưu trong history/diagnostic của attempt.
- MVP không tự retry. Người dùng chủ động retry sau khi đọc lỗi, tránh tải lại ngoài ý muốn hoặc lặp vô hạn.

### App shutdown

- Không có active download: lưu state và đóng.
- Có active download: dialog cho `Continue downloading` hoặc `Cancel download and exit`.
- Khi exit: cancel token, kill process tree, đợi cleanup với timeout, đánh dấu `Interrupted`, persist queue rồi đóng.
- Nếu cleanup timeout, log đường dẫn temp để lần khởi động sau dọn đúng task folder; không block vô hạn.

## 9. Error model và logging

### Error taxonomy

```csharp
public enum VidoraErrorCode
{
    InvalidUrl,
    UnsupportedSource,
    Unavailable,
    AuthenticationRequired,
    NetworkFailure,
    DiskFailure,
    InsufficientSpace,
    DependencyMissing,
    DependencyInvalid,
    ExtractorOutdated,
    PostProcessingFailure,
    Cancelled,
    Unknown
}

public sealed record VidoraError(
    VidoraErrorCode Code,
    string UserMessageKey,
    string SuggestedActionKey,
    string TechnicalDetail,
    bool IsRetryable);
```

- UI lấy message từ resource key, không hiển thị raw stderr làm nội dung chính.
- Classifier dựa trên exit code và các marker/message đã biết tại một chỗ duy nhất.
- Không nhận diện được thì `Unknown`, vẫn giữ technical detail đã redact.
- Cancellation không được ghi như failure.

### Logging

- Dùng logger file tối giản nội bộ; không thêm Serilog ở MVP.
- Log chứa timestamp, level, operation ID/task ID, component, event và error code.
- Redact query string có khả năng chứa token, cookie header, command arguments nhạy cảm và user profile path khi copy diagnostic.
- Log rolling: tối đa 5 file, mỗi file 2 MB.
- Không log raw metadata JSON hoặc toàn bộ environment variables.
- UI cho copy diagnostic của một tác vụ; không tự upload log.

## 10. Chiến lược kiểm thử

### Automated tests

`Vidora.Tests` dùng MSTest. Không phụ thuộc mạng hoặc tải video thật trong test mặc định.

Nhóm test bắt buộc:

- URL validation và platform recognition.
- Parse discovery JSON với field đầy đủ, thiếu và malformed.
- Progress parser với nullable field, noise line và malformed marker.
- Format selector cho bốn preset, MP4 preference và fallback container.
- Queue transition, FIFO, cancel, failure stop, retry và restore.
- Atomic JSON save/load, schema version và corrupt recovery.
- Filename sanitization/collision và path containment cho cleanup.
- Error classification và redaction.
- ViewModel command enable/disable theo UI state.
- Dependency update không thay binary cũ khi verify thất bại.

`IProcessRunner`, filesystem clock và notification được bọc interface ở nơi test cần thay thế. Không mock mọi class nội bộ.

### Integration tests local

- Chạy `yt-dlp --version` và `ffmpeg -version` với binary thật.
- Discovery một URL test công khai do dự án kiểm soát hoặc fixture hợp pháp.
- Download file media nhỏ do dự án kiểm soát để kiểm tra progress/final path/cancel.
- Không chạy test nền tảng bên thứ ba trong CI mặc định.

### Manual release matrix

Mỗi release ghi ngày, phiên bản Vidora, `yt-dlp`, FFmpeg và kết quả:

- Ít nhất một video công khai tải thành công trên YouTube, Facebook, Instagram và TikTok.
- Quality Best và ít nhất một preset giới hạn.
- Đường dẫn có dấu tiếng Việt, khoảng trắng và file trùng tên.
- Mất mạng, thiếu dependency, destination mất quyền ghi.
- Cancel, shutdown active, restore queue và history file bị xóa ngoài ứng dụng.
- Installer trên Windows 11 x64 sạch.

Một nền tảng không tải thành công sẽ chặn claim hỗ trợ bốn nền tảng của release; thông báo lỗi đúng không thay thế tiêu chí này.

## 11. Phát hành, versioning và pháp lý

### Repository và license

- Source code Vidora dùng MIT License, trừ khi kiểm tra tên/pháp lý trước public release yêu cầu thay đổi.
- `THIRD-PARTY-NOTICES.md` liệt kê `yt-dlp`, FFmpeg, CommunityToolkit.Mvvm, icon/font asset và license tương ứng.
- Không đưa media sample có bản quyền vào repository.
- README nêu rõ Vidora không vượt DRM/access control và người dùng chịu trách nhiệm về quyền tải nội dung.

### Versioning

- Semantic Versioning: `MAJOR.MINOR.PATCH`.
- `0.x`: phát triển MVP; `1.0.0`: đạt toàn bộ acceptance criteria và clean-machine release check.
- `yt-dlp`/FFmpeg version được hiển thị riêng, không gộp vào version Vidora.
- Release note ghi supported platform matrix tại thời điểm kiểm thử, không hứa mọi URL hoạt động vĩnh viễn.

### Distribution

- Installer là artifact chính; portable ZIP là artifact phụ.
- Mỗi artifact có SHA-256 checksum.
- Release chứa `LICENSE`, `THIRD-PARTY-NOTICES.md`, supported OS và hướng dẫn gỡ cài đặt/xóa local data.
- Trước public release, kiểm tra tên “Vidora” trên GitHub, package registries và các nhãn hiệu liên quan; nếu có xung đột đáng kể, đổi tên trước khi tạo installer identity.

## 12. Interfaces ổn định giữa các thành phần

Các interface sau là ranh giới cần được giữ ổn định trong implementation plan; chữ ký chi tiết có thể bổ sung type phụ nhưng không đảo trách nhiệm:

```csharp
public interface IVideoDiscoveryService
{
    Task<VideoMetadata> DiscoverAsync(string url, CancellationToken cancellationToken);
}

public interface IDownloadService
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken);
}

public interface IDownloadQueueService
{
    IReadOnlyList<DownloadQueueItem> Items { get; }
    Task EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task CancelActiveAsync(CancellationToken cancellationToken);
}

public interface IHistoryService
{
    Task<IReadOnlyList<DownloadHistoryItem>> LoadAsync(CancellationToken cancellationToken);
    Task AddAsync(DownloadHistoryItem item, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public interface IDependencyService
{
    Task<DependencyReport> CheckAsync(CancellationToken cancellationToken);
    Task<DependencyReport> UpdateYtDlpAsync(CancellationToken cancellationToken);
}
```

## 13. Luồng dữ liệu chính

```text
User URL
  -> URL validation
  -> VideoDiscoveryService
  -> ProcessRunner -> yt-dlp JSON
  -> VideoMetadata preview
  -> snapshot quality + destination
  -> QueueService persists Pending item
  -> DownloadService -> ProcessRunner -> yt-dlp + FFmpeg
  -> ProgressParser -> active item/ViewModel
  -> final path validation
  -> HistoryService + queue persistence
  -> completion notification
```

Không có bước nào gửi URL hoặc media đến server do Vidora quản lý.

## 14. Thứ tự triển khai bắt buộc

Implementation plan phải chia theo lát dọc có thể kiểm thử, theo thứ tự:

1. Solution shell, app lifecycle và resource nền.
2. Dependency discovery cho `yt-dlp`/FFmpeg.
3. URL validation + discovery + preview.
4. Tải một video + progress + final path.
5. Filesystem safety + cancel + shutdown.
6. Queue state machine + persistence.
7. History.
8. Error UX + logging/redaction.
9. Update `yt-dlp`.
10. Theme, notification và accessibility pass.
11. Installer/portable + release matrix + documentation.

Không bắt đầu playlist, audio-only, subtitle, livestream, cookie hoặc đa nền tảng trước khi 11 bước trên đạt acceptance criteria MVP.

## 15. Tài liệu tham chiếu

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [.NET 10 supported OS versions](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- [WPF documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [.NET self-contained and single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/)
- [`yt-dlp` documentation](https://github.com/yt-dlp/yt-dlp/blob/master/README.md)
- [`yt-dlp` supported sites](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md)
- [FFmpeg download and legal information](https://ffmpeg.org/download.html)
