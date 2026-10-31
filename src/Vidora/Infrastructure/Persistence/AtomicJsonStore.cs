using System.IO;
using System.Text.Json;

namespace Vidora.Infrastructure.Persistence;

public sealed class AtomicJsonStore<TDocument>(
    string filePath,
    int supportedSchemaVersion,
    Func<TDocument> createDefault,
    TimeProvider timeProvider)
    where TDocument : VersionedDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim access = new(1, 1);

    public async Task<AtomicStoreLoadResult<TDocument>> LoadAsync(CancellationToken cancellationToken)
    {
        await access.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                return new AtomicStoreLoadResult<TDocument>(
                    createDefault(),
                    AtomicStoreLoadStatus.Missing,
                    null);
            }

            try
            {
                TDocument? document;
                await using (var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    document = await JsonSerializer.DeserializeAsync<TDocument>(stream, JsonOptions, cancellationToken);
                }

                if (document is null || !document.HasValidShape)
                {
                    return RecoverCorrupt();
                }

                return document.SchemaVersion == supportedSchemaVersion
                    ? new AtomicStoreLoadResult<TDocument>(document, AtomicStoreLoadStatus.Loaded, null)
                    : new AtomicStoreLoadResult<TDocument>(
                        createDefault(),
                        AtomicStoreLoadStatus.UnsupportedSchema,
                        null);
            }
            catch (JsonException)
            {
                return RecoverCorrupt();
            }
            catch (NotSupportedException)
            {
                return RecoverCorrupt();
            }
        }
        finally
        {
            access.Release();
        }
    }

    public async Task SaveAsync(TDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != supportedSchemaVersion)
        {
            throw new ArgumentException("Document schema version is not supported.", nameof(document));
        }

        await access.WaitAsync(cancellationToken);
        var temporaryPath = filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            File.Delete(temporaryPath);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
            {
                File.Replace(temporaryPath, filePath, null);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
            access.Release();
        }
    }

    private AtomicStoreLoadResult<TDocument> RecoverCorrupt()
    {
        var timestamp = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        var quarantinedPath = filePath + $".corrupt-{timestamp}";
        for (var suffix = 2; File.Exists(quarantinedPath); suffix++)
        {
            quarantinedPath = filePath + $".corrupt-{timestamp}-{suffix}";
        }

        File.Move(filePath, quarantinedPath);
        return new AtomicStoreLoadResult<TDocument>(
            createDefault(),
            AtomicStoreLoadStatus.RecoveredCorrupt,
            quarantinedPath);
    }
}
