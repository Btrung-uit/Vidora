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

public partial class DownloadViewModel(
    IVideoDiscoveryService discoveryService,
    IDownloadService downloadService) : ObservableObject
{
    private CancellationTokenSource? activeDiscovery;

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
    public partial bool IsDownloading { get; private set; }

    [ObservableProperty]
    public partial DownloadProgress? Progress { get; private set; }

    [ObservableProperty]
    public partial DownloadResult? DownloadResult { get; private set; }

    [ObservableProperty]
    public partial VidoraError? DownloadError { get; private set; }

    [ObservableProperty]
    public partial DownloadStatus? LastDownloadStatus { get; private set; }

    public IReadOnlyList<QualityPreset> QualityPresets { get; } = Enum.GetValues<QualityPreset>();

    [RelayCommand]
    private void Paste()
    {
        if (Clipboard.ContainsText())
        {
            Url = Clipboard.GetText().Trim();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
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
        !string.IsNullOrWhiteSpace(DestinationDirectory) &&
        !IsDownloading;

    [RelayCommand(CanExecute = nameof(CanStartDownload))]
    private async Task StartDownloadAsync(CancellationToken cancellationToken)
    {
        if (!CanStartDownload() || Metadata is null)
        {
            return;
        }

        IsDownloading = true;
        Progress = null;
        DownloadResult = null;
        DownloadError = null;
        LastDownloadStatus = DownloadStatus.Downloading;

        try
        {
            var progress = new Progress<DownloadProgress>(value => Progress = value);
            DownloadResult = await downloadService.DownloadAsync(
                new DownloadRequest(
                    Guid.NewGuid(),
                    Metadata.SourceUrl,
                    Metadata,
                    SelectedQuality,
                    DestinationDirectory,
                    DateTimeOffset.UtcNow),
                progress,
                cancellationToken);
            LastDownloadStatus = DownloadResult.Status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LastDownloadStatus = DownloadStatus.Cancelled;
        }
        catch (VidoraException exception)
        {
            DownloadError = exception.Error;
            LastDownloadStatus = DownloadStatus.Failed;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload() => StartDownloadCommand.Cancel();
}
