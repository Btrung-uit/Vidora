using Vidora.Infrastructure.Files;

namespace Vidora.Tests.Infrastructure.Files;

[TestClass]
public sealed class TaskWorkspaceServiceTests
{
    [TestMethod]
    public void Cleanup_RegisteredExactTaskDirectory_DeletesOnlyOwnedDirectory()
    {
        using var root = new TemporaryDirectory("vidora-temp-root-");
        var taskId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var service = new TaskWorkspaceService(root.Path, new FullPathResolver());
        var taskDirectory = service.Create(taskId);
        File.WriteAllText(Path.Combine(taskDirectory, "owned.part"), "temporary");

        var cleaned = service.Cleanup(taskId);

        Assert.IsTrue(cleaned);
        Assert.IsFalse(Directory.Exists(taskDirectory));
        Assert.IsTrue(Directory.Exists(root.Path));
    }

    [TestMethod]
    public void Register_SiblingPrefixPath_RejectsOwnership()
    {
        using var root = new TemporaryDirectory("Vidora-temp-");
        using var sibling = new TemporaryDirectory(Path.GetFileName(root.Path) + "-evil-");
        var taskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var candidate = Directory.CreateDirectory(Path.Combine(sibling.Path, taskId.ToString("D"))).FullName;
        var service = new TaskWorkspaceService(root.Path, new FullPathResolver());

        Assert.Throws<InvalidOperationException>(() => service.Register(taskId, candidate));
        Assert.IsTrue(Directory.Exists(candidate));
    }

    [TestMethod]
    public void Register_TraversalPath_RejectsOwnership()
    {
        using var parent = new TemporaryDirectory("vidora-temp-parent-");
        var root = Directory.CreateDirectory(Path.Combine(parent.Path, "temp")).FullName;
        var taskId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var outside = Directory.CreateDirectory(Path.Combine(parent.Path, taskId.ToString("D"))).FullName;
        var traversal = Path.Combine(root, "..", taskId.ToString("D"));
        var service = new TaskWorkspaceService(root, new FullPathResolver());

        Assert.Throws<InvalidOperationException>(() => service.Register(taskId, traversal));
        Assert.IsTrue(Directory.Exists(outside));
    }

    [TestMethod]
    public void Register_OutsideAliasResolvingToOwnedTarget_RejectsLexicalPath()
    {
        using var root = new TemporaryDirectory("vidora-temp-root-");
        using var outside = new TemporaryDirectory("vidora-temp-alias-");
        var taskId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var expected = Directory.CreateDirectory(Path.Combine(root.Path, taskId.ToString("D"))).FullName;
        var alias = outside.Path;
        var resolver = new DelegatePathResolver(path =>
            string.Equals(Path.GetFullPath(path), alias, StringComparison.OrdinalIgnoreCase)
                ? expected
                : Path.GetFullPath(path));
        var service = new TaskWorkspaceService(root.Path, resolver);

        Assert.Throws<InvalidOperationException>(() => service.Register(taskId, alias));
        Assert.IsTrue(Directory.Exists(alias));
    }

    [TestMethod]
    public void Cleanup_ReparseTargetChangedOutsideRoot_RejectsWithoutDeleting()
    {
        using var root = new TemporaryDirectory("vidora-temp-root-");
        using var outside = new TemporaryDirectory("vidora-temp-outside-");
        var taskId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var redirect = false;
        var resolver = new DelegatePathResolver(path =>
        {
            var fullPath = Path.GetFullPath(path);
            var expectedTaskPath = Path.Combine(root.Path, taskId.ToString("D"));
            return redirect && string.Equals(fullPath, expectedTaskPath, StringComparison.OrdinalIgnoreCase)
                ? outside.Path
                : fullPath;
        });
        var service = new TaskWorkspaceService(root.Path, resolver);
        var taskDirectory = service.Create(taskId);
        File.WriteAllText(Path.Combine(outside.Path, "keep.txt"), "user data");
        redirect = true;

        var cleaned = service.Cleanup(taskId);

        Assert.IsFalse(cleaned);
        Assert.IsTrue(Directory.Exists(taskDirectory));
        Assert.IsTrue(File.Exists(Path.Combine(outside.Path, "keep.txt")));
    }

    private sealed class FullPathResolver : ICanonicalPathResolver
    {
        public string Resolve(string path) => Path.GetFullPath(path);
    }

    private sealed class DelegatePathResolver(Func<string, string> resolve) : ICanonicalPathResolver
    {
        public string Resolve(string path) => resolve(path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string prefix) =>
            Path = Directory.CreateTempSubdirectory(prefix).FullName;

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
