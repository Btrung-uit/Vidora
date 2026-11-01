using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vidora.Core.Models;
using Vidora.Core.Services;

namespace Vidora.Features.History;

public partial class HistoryViewModel(
    IHistoryService historyService,
    IDownloadQueueService queueService,
    Action<string> openPath,
    Action<string> copyText,
    Func<bool> confirmClear,
    TimeProvider timeProvider,
    SynchronizationContext? synchronizationContext = null) : ObservableObject
{
    private readonly SynchronizationContext? uiContext = synchronizationContext ?? SynchronizationContext.Current;
    private bool subscribedToQueue;

    [ObservableProperty]
    public partial IReadOnlyList<HistoryItemViewModel> Items { get; private set; } = [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!subscribedToQueue)
        {
            queueService.Changed += QueueService_Changed;
            subscribedToQueue = true;
        }

        await ReloadAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void OpenFile(HistoryItemViewModel? row)
    {
        if (CanOpenFile(row))
        {
            openPath(row!.Item.FinalPath!);
        }
    }

    private static bool CanOpenFile(HistoryItemViewModel? row) =>
        row?.Item.FinalPath is { Length: > 0 } path && File.Exists(path);

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder(HistoryItemViewModel? row)
    {
        var folder = GetExistingFolder(row);
        if (folder is not null)
        {
            openPath(folder);
        }
    }

    private static bool CanOpenFolder(HistoryItemViewModel? row) => GetExistingFolder(row) is not null;

    [RelayCommand]
    private void CopyUrl(HistoryItemViewModel? row)
    {
        if (row is not null)
        {
            copyText(row.Item.SourceUrl.AbsoluteUri);
        }
    }

    [RelayCommand]
    private Task RetryAsync(HistoryItemViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        var item = row.Item;
        var metadata = new VideoMetadata(
            item.SourceUrl,
            item.Platform,
            item.MediaId,
            item.Title,
            null,
            null,
            null,
            item.Formats.ToArray());
        var request = new DownloadRequest(
            Guid.NewGuid(),
            item.SourceUrl,
            metadata,
            item.Quality,
            item.DestinationDirectory,
            timeProvider.GetUtcNow());
        return queueService.EnqueueAsync(request, cancellationToken);
    }

    [RelayCommand]
    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        if (!confirmClear())
        {
            return;
        }

        await historyService.ClearAsync(cancellationToken);
        Items = [];
    }

    private static string? GetExistingFolder(HistoryItemViewModel? row)
    {
        if (row is null)
        {
            return null;
        }

        var finalPath = row.Item.FinalPath;
        var parent = string.IsNullOrWhiteSpace(finalPath) ? null : Path.GetDirectoryName(finalPath);
        if (parent is not null && Directory.Exists(parent))
        {
            return parent;
        }

        return Directory.Exists(row.Item.DestinationDirectory)
            ? row.Item.DestinationDirectory
            : null;
    }

    private void QueueService_Changed(object? sender, EventArgs e)
    {
        if (uiContext is null)
        {
            ReloadFromQueue();
            return;
        }

        uiContext.Post(static state => ((HistoryViewModel)state!).ReloadFromQueue(), this);
    }

    private async void ReloadFromQueue() => await ReloadAsync(CancellationToken.None);

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var items = await historyService.LoadAsync(cancellationToken);
        Items = items.Select(static item => new HistoryItemViewModel(item)).ToArray();
    }
}

public sealed class HistoryItemViewModel(DownloadHistoryItem item)
{
    public DownloadHistoryItem Item { get; } = item;

    public bool IsMissing =>
        Item.Status == DownloadStatus.Completed &&
        (string.IsNullOrWhiteSpace(Item.FinalPath) || !File.Exists(Item.FinalPath));
}
