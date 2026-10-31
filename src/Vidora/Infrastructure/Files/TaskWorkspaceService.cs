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
        using var rootLease = pathResolver.Acquire(tempRoot);
        using var directoryLease = pathResolver.Acquire(directory);
        var currentRoot = Normalize(rootLease.Path);
        var canonicalDirectory = Normalize(directoryLease.Path);
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
            using var rootLease = pathResolver.Acquire(tempRoot);
            var currentRoot = Normalize(rootLease.Path);
            string currentDirectory;
            using (var directoryLease = pathResolver.Acquire(ownedDirectory.Path))
            {
                currentDirectory = Normalize(directoryLease.Path);
                if (!string.Equals(currentRoot, canonicalRoot, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(currentDirectory, ownedDirectory.CanonicalPath, StringComparison.OrdinalIgnoreCase) ||
                    !DeleteContentsSafely(currentDirectory, currentDirectory))
                {
                    return false;
                }
            }

            if (!string.Equals(currentRoot, canonicalRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentDirectory, ownedDirectory.CanonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Directory.Delete(currentDirectory, recursive: false);
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

    private bool DeleteContentsSafely(string directory, string ownedRoot)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                File.Delete(entry);
                continue;
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(entry, recursive: false);
                continue;
            }

            string canonicalChild;
            using (var childLease = pathResolver.Acquire(entry))
            {
                canonicalChild = Normalize(childLease.Path);
                if (!IsWithin(ownedRoot, canonicalChild) ||
                    !DeleteContentsSafely(canonicalChild, ownedRoot))
                {
                    return false;
                }
            }

            Directory.Delete(canonicalChild, recursive: false);
        }

        return true;
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private sealed record OwnedDirectory(string Path, string CanonicalPath);
}
