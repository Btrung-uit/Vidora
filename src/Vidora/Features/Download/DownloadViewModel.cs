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

public partial class DownloadViewModel(IVideoDiscoveryService discoveryService) : ObservableObject
{
    private CancellationTokenSource? activeDiscovery;

    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DownloadDiscoveryState State { get; private set; }

    [ObservableProperty]
    public partial VideoMetadata? Metadata { get; private set; }

    [ObservableProperty]
    public partial VidoraError? Error { get; private set; }

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
}
