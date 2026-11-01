using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vidora.Core.Errors;

namespace Vidora.Infrastructure.Logging;

public enum FileLogLevel
{
    Information,
    Warning,
    Error
}

public sealed record FileLogEntry(
    FileLogLevel Level,
    Guid OperationId,
    string Component,
    string Event,
    VidoraErrorCode? ErrorCode,
    string? Detail);

public interface IAppLogger
{
    Task WriteAsync(FileLogEntry entry, CancellationToken cancellationToken);
}

public sealed class FileLogger(string logDirectory, TimeProvider timeProvider) : IAppLogger
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFiles = 5;
    private const string Truncated = "[TRUNCATED]";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim access = new(1, 1);

    public async Task WriteAsync(FileLogEntry entry, CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            await access.WaitAsync(cancellationToken);
            acquired = true;
            Directory.CreateDirectory(logDirectory);
            var line = SerializeLine(entry);
            RotateIfNeeded(line.Length);

            await using var stream = new FileStream(
                GetPath(0),
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous);
            await stream.WriteAsync(line, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
        }
        finally
        {
            if (acquired)
            {
                access.Release();
            }
        }
    }

    private byte[] SerializeLine(FileLogEntry entry)
    {
        var redactedDetail = DiagnosticRedactor.Redact(entry.Detail ?? string.Empty);
        var line = Serialize(entry, redactedDetail);
        if (line.Length <= MaxFileBytes)
        {
            return line;
        }

        var retainedLength = Math.Min(1024, redactedDetail.Length);
        if (retainedLength > 0 && char.IsHighSurrogate(redactedDetail[retainedLength - 1]))
        {
            retainedLength--;
        }

        return Serialize(entry, redactedDetail[..retainedLength] + Environment.NewLine + Truncated);
    }

    private byte[] Serialize(FileLogEntry entry, string detail)
    {
        var serialized = JsonSerializer.Serialize(
            new SerializedLogEntry(
                timeProvider.GetUtcNow(),
                entry.Level,
                entry.OperationId,
                entry.Component,
                entry.Event,
                entry.ErrorCode,
                detail),
            JsonOptions);
        return Encoding.UTF8.GetBytes(serialized + Environment.NewLine);
    }

    private void RotateIfNeeded(int nextEntryBytes)
    {
        var activePath = GetPath(0);
        if (!File.Exists(activePath) || new FileInfo(activePath).Length + nextEntryBytes <= MaxFileBytes)
        {
            return;
        }

        File.Delete(GetPath(MaxFiles - 1));
        for (var index = MaxFiles - 1; index >= 2; index--)
        {
            var previousPath = GetPath(index - 1);
            if (File.Exists(previousPath))
            {
                File.Move(previousPath, GetPath(index));
            }
        }

        File.Move(activePath, GetPath(1));
    }

    private string GetPath(int index) => Path.Combine(
        logDirectory,
        index == 0 ? "vidora.log" : $"vidora.{index}.log");

    private sealed record SerializedLogEntry(
        DateTimeOffset Timestamp,
        FileLogLevel Level,
        Guid OperationId,
        string Component,
        string Event,
        VidoraErrorCode? ErrorCode,
        string Detail);
}
