using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vidora.Core.Errors;
using Vidora.Core.Models;
using Vidora.Core.Services;

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
    private readonly SynchronizationContext? uiContext;
    private CancellationTokenSource? activeDiscovery;

    public DownloadViewModel(
        IVideoDiscoveryService discoveryService,
        IDownloadQueueService queueService,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        SynchronizationContext? uiContext = null)
    {
        this.discoveryService = discoveryService;
        this.queueService = queueService;
        this.settingsService = settingsService;
        this.timeProvider = timeProvider;
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
    public partial VidoraError? DownloadError { get; private set; }

    [ObservableProperty]
    public partial DownloadStatus? LastDownloadStatus { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<DownloadQueueItem> QueueItems { get; private set; } = [];

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
        if (Clipboard.ContainsText())
        {
            Url = Clipboard.GetText().Trim();
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
            await settingsService.SaveAsync(new AppSettings(DestinationDirectory), cancellationToken);
        }
    }

    [RelayCommand]
    private Task CancelDownloadAsync(CancellationToken cancellationToken) =>
        queueService.CancelActiveAsync(cancellationToken);

    [RelayCommand]
    private Task ResumeQueueAsync(CancellationToken cancellationToken) =>
        queueService.ResumeAsync(cancellationToken);

    [RelayCommand]
    private Task RemovePendingAsync(DownloadQueueItem item, CancellationToken cancellationToken) =>
        queueService.RemovePendingAsync(item.Request.Id, cancellationToken);

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
        var active = snapshot.FirstOrDefault(item => item.Status == DownloadStatus.Downloading);
        var latest = active ?? snapshot.LastOrDefault();
        IsDownloading = active is not null;
        Progress = active?.Progress;
        LastDownloadStatus = latest?.Status;
        DownloadResult = latest is null || latest.Status is DownloadStatus.Pending or DownloadStatus.Downloading
            ? null
            : new DownloadResult(latest.Status, latest.FinalPath, latest.Error);
        DownloadError = latest?.Error;
    }
}
