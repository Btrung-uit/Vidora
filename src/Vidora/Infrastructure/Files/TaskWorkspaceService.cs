using System.IO;

namespace Vidora.Infrastructure.Files;

public sealed class TaskWorkspaceService
{
    private readonly string tempRoot;
    private readonly string canonicalRoot;
    private readonly ICanonicalPathResolver pathResolver;
    private readonly Dictionary<Guid, OwnedDirectory> ownedDirectories = [];
    private readonly Lock sync = new();

    public TaskWorkspaceService(string tempRoot, ICanonicalPathResolver pathResolver)
    {
        this.tempRoot = Directory.CreateDirectory(Path.GetFullPath(tempRoot)).FullName;
        this.pathResolver = pathResolver;
        canonicalRoot = Normalize(pathResolver.Resolve(this.tempRoot));
    }

    public string Create(Guid taskId)
    {
        var directory = Directory.CreateDirectory(Path.Combine(tempRoot, taskId.ToString("D"))).FullName;
        Register(taskId, directory);
        return directory;
    }

    public void Register(Guid taskId, string directory)
    {
        var currentRoot = Normalize(pathResolver.Resolve(tempRoot));
        var canonicalDirectory = Normalize(pathResolver.Resolve(directory));
        var expectedDirectory = Normalize(Path.Combine(canonicalRoot, taskId.ToString("D")));
        var expectedSourceDirectory = Normalize(Path.Combine(tempRoot, taskId.ToString("D")));
        if (!string.Equals(currentRoot, canonicalRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Normalize(directory), expectedSourceDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(canonicalDirectory, expectedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Task workspace is outside the configured temp root.");
        }

        lock (sync)
        {
            if (ownedDirectories.ContainsKey(taskId))
            {
                throw new InvalidOperationException($"Task workspace '{taskId}' is already registered.");
            }

            ownedDirectories.Add(taskId, new OwnedDirectory(Path.GetFullPath(directory), canonicalDirectory));
        }
    }

    public bool Cleanup(Guid taskId)
    {
        OwnedDirectory ownedDirectory;
        lock (sync)
        {
            if (!ownedDirectories.TryGetValue(taskId, out ownedDirectory!))
            {
                return false;
            }
        }

        try
        {
            var currentRoot = Normalize(pathResolver.Resolve(tempRoot));
            var currentDirectory = Normalize(pathResolver.Resolve(ownedDirectory.Path));
            if (!string.Equals(currentRoot, canonicalRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentDirectory, ownedDirectory.CanonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Directory.Delete(ownedDirectory.Path, recursive: true);
            lock (sync)
            {
                ownedDirectories.Remove(taskId);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record OwnedDirectory(string Path, string CanonicalPath);
}
