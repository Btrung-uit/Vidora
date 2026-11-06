using System.IO;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Infrastructure.Logging;

namespace Vidora.Features.Download;

public enum DownloadDiscoveryState
{
    Empty,
    Discovering,
    Ready,
    Failed
}

public partial class DownloadViewModel : ObservableObject
{
    private readonly IVideoDiscoveryService discoveryService;
    private readonly IDownloadQueueService queueService;
    private readonly ISettingsService settingsService;
    private readonly TimeProvider timeProvider;
    private readonly Action<string> copyText;
    private readonly Action<string> openPath;
    private readonly SynchronizationContext? uiContext;
    private CancellationTokenSource? activeDiscovery;

    public DownloadViewModel(
        IVideoDiscoveryService discoveryService,
        IDownloadQueueService queueService,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        SynchronizationContext? uiContext = null,
        Action<string>? copyText = null,
        Action<string>? openPath = null)
    {
        this.discoveryService = discoveryService;
        this.queueService = queueService;
        this.settingsService = settingsService;
        this.timeProvider = timeProvider;
        this.copyText = copyText ?? (_ => { });
        this.openPath = openPath ?? (_ => { });
        this.uiContext = uiContext ?? SynchronizationContext.Current;
        queueService.Changed += QueueService_Changed;
        RefreshQueueProjection();
    }

    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DownloadDiscoveryState State { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDownloadCommand))]
    public partial VideoMetadata? Metadata { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyDiagnosticCommand))]
    public partial VidoraError? Error { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDownloadCommand))]
    public partial string DestinationDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial QualityPreset SelectedQuality { get; set; } = QualityPreset.Best;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscoverCommand))]
    public partial bool IsDownloading { get; private set; }

    [ObservableProperty]
    public partial DownloadProgress? Progress { get; private set; }

    [ObservableProperty]
    public partial DownloadResult? DownloadResult { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyDiagnosticCommand))]
    public partial VidoraError? DownloadError { get; private set; }

    [ObservableProperty]
    public partial DownloadStatus? LastDownloadStatus { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearFinishedCommand))]
    public partial IReadOnlyList<DownloadQueueItem> QueueItems { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasQueueOperationError { get; private set; }

    [ObservableProperty]
    public partial string QueueOperationErrorKey { get; private set; } = "Queue.PersistenceFailed";

    public IReadOnlyList<QualityPreset> QualityPresets { get; } = Enum.GetValues<QualityPreset>();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        if (settings.LastDestinationDirectory is { } destination && Directory.Exists(destination))
        {
            DestinationDirectory = destination;
        }
    }

    [RelayCommand]
    private void Paste()
    {
        if (System.Windows.Clipboard.ContainsText())
        {
            Url = System.Windows.Clipboard.GetText().Trim();
        }
    }

    private static bool CanDiscover() => true;

    [RelayCommand(CanExecute = nameof(CanDiscover), AllowConcurrentExecutions = true)]
    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!CanDiscover())
        {
            return;
        }

        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref activeDiscovery, operation);
        previous?.Cancel();

        State = DownloadDiscoveryState.Discovering;
        Metadata = null;
        Error = null;

        try
        {
            var metadata = await discoveryService.DiscoverAsync(Url.Trim(), operation.Token);
            if (ReferenceEquals(Volatile.Read(ref activeDiscovery), operation))
            {
                Metadata = metadata;
                State = DownloadDiscoveryState.Ready;
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (ReferenceEquals(Volatile.Read(ref activeDiscovery), operation))
            {
                State = DownloadDiscoveryState.Empty;
            }
        }
        catch (VidoraException exception)
        {
            if (ReferenceEquals(Volatile.Read(ref activeDiscovery), operation))
            {
                Error = exception.Error;
                State = DownloadDiscoveryState.Failed;
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref activeDiscovery, null, operation);
            operation.Dispose();
        }
    }

    [RelayCommand]
    private void CancelDiscovery() => Volatile.Read(ref activeDiscovery)?.Cancel();

    private static bool CanCopyDiagnostic(VidoraError? error) => error is not null;

    [RelayCommand(CanExecute = nameof(CanCopyDiagnostic))]
    private void CopyDiagnostic(VidoraError? error)
    {
        if (error is null)
        {
            return;
        }

        var diagnostic = string.Join(
            Environment.NewLine,
            $"Code: {error.Code}",
            $"MessageKey: {error.UserMessageKey}",
            $"ActionKey: {error.SuggestedActionKey}",
            $"Retryable: {error.IsRetryable}",
            "Detail:",
            DiagnosticRedactor.Redact(error.TechnicalDetail));
        try
        {
            copyText(diagnostic);
        }
        catch (Exception)
        {
        }
    }

    private bool CanStartDownload() =>
        Metadata is not null &&
        !string.IsNullOrWhiteSpace(DestinationDirectory);

    [RelayCommand(CanExecute = nameof(CanStartDownload))]
    private async Task StartDownloadAsync(CancellationToken cancellationToken)
    {
        if (!CanStartDownload() || Metadata is null)
        {
            return;
        }

        var taskId = Guid.NewGuid();
        await queueService.EnqueueAsync(
            new DownloadRequest(
                taskId,
                Metadata.SourceUrl,
                Metadata,
                SelectedQuality,
                DestinationDirectory,
                timeProvider.GetUtcNow()),
            cancellationToken);
        if (Directory.Exists(DestinationDirectory))
        {
            var destination = DestinationDirectory;
            await settingsService.UpdateAsync(settings => settings with { LastDestinationDirectory = destination }, cancellationToken);
        }
    }

    [RelayCommand]
    private Task CancelDownloadAsync(CancellationToken cancellationToken) =>
        queueService.CancelActiveAsync(cancellationToken);

    [RelayCommand]
    private Task ResumeQueueAsync(CancellationToken cancellationToken) =>
        queueService.ResumeAsync(cancellationToken);

    private static bool CanRemove(DownloadQueueItem? item) =>
        item?.Status is DownloadStatus.Pending or DownloadStatus.Failed;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task RemoveAsync(DownloadQueueItem item, CancellationToken cancellationToken) =>
        RunQueueOperationAsync(() => queueService.RemoveAsync(item.Request.Id, cancellationToken));

    private static bool CanRetryQueueItem(DownloadQueueItem? item) => item?.Status == DownloadStatus.Failed;

    [RelayCommand(CanExecute = nameof(CanRetryQueueItem))]
    private Task RetryQueueItemAsync(DownloadQueueItem item, CancellationToken cancellationToken) =>
        RunQueueOperationAsync(() => queueService.RetryAsync(item.Request.Id, cancellationToken));

    private bool CanClearFinished() => QueueItems.Any(item => item.Status is
        DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Interrupted);

    [RelayCommand(CanExecute = nameof(CanClearFinished))]
    private Task ClearFinishedAsync(CancellationToken cancellationToken) =>
        RunQueueOperationAsync(() => queueService.ClearFinishedAsync(cancellationToken));

    private async Task RunQueueOperationAsync(Func<Task<bool>> operation)
    {
        QueueOperationErrorKey = "Queue.PersistenceFailed";
        try
        {
            HasQueueOperationError = !await operation();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            HasQueueOperationError = true;
        }
    }

    private static bool CanOpenQueueFile(DownloadQueueItem? item) =>
        item?.Status == DownloadStatus.Completed && item.FinalPath is { Length: > 0 } path && File.Exists(path);

    [RelayCommand(CanExecute = nameof(CanOpenQueueFile))]
    private void OpenQueueFile(DownloadQueueItem? item) =>
        TryOpenQueuePath(CanOpenQueueFile(item) ? item!.FinalPath : null);

    private static string? GetQueueFolder(DownloadQueueItem? item)
    {
        if (item?.Status != DownloadStatus.Completed)
        {
            return null;
        }

        var parent = string.IsNullOrWhiteSpace(item.FinalPath) ? null : Path.GetDirectoryName(item.FinalPath);
        return parent is not null && Directory.Exists(parent) ? parent :
            Directory.Exists(item.Request.DestinationDirectory) ? item.Request.DestinationDirectory : null;
    }

    private static bool CanOpenQueueFolder(DownloadQueueItem? item) => GetQueueFolder(item) is not null;

    [RelayCommand(CanExecute = nameof(CanOpenQueueFolder))]
    private void OpenQueueFolder(DownloadQueueItem? item) => TryOpenQueuePath(GetQueueFolder(item));

    private void TryOpenQueuePath(string? path)
    {
        QueueOperationErrorKey = path is null ? "History.Missing" : "History.OperationFailed";
        HasQueueOperationError = path is null;
        if (path is null)
        {
            return;
        }

        try
        {
            openPath(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            HasQueueOperationError = true;
        }
    }

    private void QueueService_Changed(object? sender, EventArgs e)
    {
        if (uiContext is null || ReferenceEquals(SynchronizationContext.Current, uiContext))
        {
            RefreshQueueProjection();
            return;
        }

        uiContext.Post(static state => ((DownloadViewModel)state!).RefreshQueueProjection(), this);
    }

    private void RefreshQueueProjection()
    {
        var snapshot = queueService.Items;
        QueueItems = snapshot;
        OpenQueueFileCommand.NotifyCanExecuteChanged();
        OpenQueueFolderCommand.NotifyCanExecuteChanged();
        var active = snapshot.FirstOrDefault(item => item.Status == DownloadStatus.Downloading);
        var latestTerminal = snapshot.LastOrDefault(item =>
            item.Status is not DownloadStatus.Pending and not DownloadStatus.Downloading);
        var latest = active ?? latestTerminal ?? snapshot.LastOrDefault();
        IsDownloading = active is not null;
        Progress = active?.Progress;
        LastDownloadStatus = latest?.Status;
        DownloadResult = latest is null || latest.Status is DownloadStatus.Pending or DownloadStatus.Downloading
            ? null
            : new DownloadResult(latest.Status, latest.FinalPath, latest.Error);
        DownloadError = latest?.Error;
    }
}
