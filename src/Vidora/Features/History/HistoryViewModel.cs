using System.ComponentModel;
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

    [ObservableProperty]
    public partial bool HasPersistenceError { get; private set; }

    [ObservableProperty]
    public partial bool HasOperationError { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!subscribedToQueue)
        {
            queueService.Changed += QueueService_Changed;
            subscribedToQueue = true;
        }

        await TryReloadAsync(cancellationToken);
        HasPersistenceError |= queueService.HasHistoryPersistenceError;
    }

    public void RefreshAvailability()
    {
        foreach (var item in Items)
        {
            item.RefreshAvailability();
        }

        OpenFileCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void OpenFile(HistoryItemViewModel? row)
    {
        if (CanOpenFile(row))
        {
            TryOpenPath(row!.Item.FinalPath!);
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
            TryOpenPath(folder);
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
    private async Task RetryAsync(HistoryItemViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null)
        {
            return;
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
        try
        {
            await queueService.EnqueueAsync(request, cancellationToken);
            HasPersistenceError = queueService.HasHistoryPersistenceError;
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            HasPersistenceError = true;
        }
    }

    [RelayCommand]
    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        if (!confirmClear())
        {
            return;
        }

        try
        {
            await historyService.ClearAsync(cancellationToken);
            Items = [];
            HasPersistenceError = queueService.HasHistoryPersistenceError;
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            HasPersistenceError = true;
        }
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

    private async void ReloadFromQueue()
    {
        await TryReloadAsync(CancellationToken.None);
        HasPersistenceError |= queueService.HasHistoryPersistenceError;
    }

    private async Task<bool> TryReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var items = await historyService.LoadAsync(cancellationToken);
            Items = items.Select(static item => new HistoryItemViewModel(item)).ToArray();
            HasPersistenceError = false;
            return true;
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            HasPersistenceError = true;
            return false;
        }
    }

    private void TryOpenPath(string path)
    {
        try
        {
            openPath(path);
            HasOperationError = false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            HasOperationError = true;
        }
    }

    private static bool IsPersistenceException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;
}

public sealed class HistoryItemViewModel(DownloadHistoryItem item) : ObservableObject
{
    public DownloadHistoryItem Item { get; } = item;

    public bool IsMissing =>
        Item.Status == DownloadStatus.Completed &&
        (string.IsNullOrWhiteSpace(Item.FinalPath) || !File.Exists(Item.FinalPath));

    public void RefreshAvailability() => OnPropertyChanged(nameof(IsMissing));
}
