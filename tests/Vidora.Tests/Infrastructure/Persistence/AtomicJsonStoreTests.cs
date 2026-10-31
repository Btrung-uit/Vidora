using Vidora.Infrastructure.Persistence;

namespace Vidora.Tests.Infrastructure.Persistence;

[TestClass]
public sealed class AtomicJsonStoreTests
{
    private static readonly DateTimeOffset CorruptAt = new(2026, 10, 31, 8, 9, 10, TimeSpan.Zero);

    [TestMethod]
    public async Task SaveAndLoadAsync_FirstDocument_RoundTrips()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "document.json");
        var store = CreateStore(path);

        await store.SaveAsync(new TestDocument(1, "first"), CancellationToken.None);
        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(AtomicStoreLoadStatus.Loaded, result.Status);
        Assert.AreEqual("first", result.Document.Value);
    }

    [TestMethod]
    public async Task SaveAsync_ExistingDocument_ReplacesPreviousValue()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "document.json");
        var store = CreateStore(path);
        await store.SaveAsync(new TestDocument(1, "first"), CancellationToken.None);

        await store.SaveAsync(new TestDocument(1, "second"), CancellationToken.None);
        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual("second", result.Document.Value);
    }

    [TestMethod]
    public async Task LoadAsync_InterruptedTempFile_KeepsLastGoodDocument()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "document.json");
        var store = CreateStore(path);
        await store.SaveAsync(new TestDocument(1, "good"), CancellationToken.None);
        await File.WriteAllTextAsync(path + ".tmp", "{ interrupted", CancellationToken.None);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(AtomicStoreLoadStatus.Loaded, result.Status);
        Assert.AreEqual("good", result.Document.Value);
    }

    [TestMethod]
    public async Task LoadAsync_MalformedDocument_QuarantinesAndReturnsDefault()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "document.json");
        await File.WriteAllTextAsync(path, "{ malformed", CancellationToken.None);
        var store = CreateStore(path);

        var result = await store.LoadAsync(CancellationToken.None);

        var expectedCorruptPath = path + ".corrupt-20261031T080910000Z";
        Assert.AreEqual(AtomicStoreLoadStatus.RecoveredCorrupt, result.Status);
        Assert.AreEqual("default", result.Document.Value);
        Assert.AreEqual(expectedCorruptPath, result.QuarantinedPath);
        Assert.IsFalse(File.Exists(path));
        Assert.IsTrue(File.Exists(expectedCorruptPath));
    }

    [TestMethod]
    public async Task LoadAsync_NullRoot_ClosesFileBeforeQuarantine()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "document.json");
        await File.WriteAllTextAsync(path, "null", CancellationToken.None);
        var store = CreateStore(path);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(AtomicStoreLoadStatus.RecoveredCorrupt, result.Status);
        Assert.AreEqual("default", result.Document.Value);
        Assert.IsFalse(File.Exists(path));
        Assert.IsTrue(File.Exists(result.QuarantinedPath));
    }

    [TestMethod]
    public async Task LoadAsync_UnsupportedSchema_ReturnsControlledDefaultWithoutDestroyingFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "document.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":2,\"value\":\"future\"}", CancellationToken.None);
        var store = CreateStore(path);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(AtomicStoreLoadStatus.UnsupportedSchema, result.Status);
        Assert.AreEqual("default", result.Document.Value);
        Assert.IsNull(result.QuarantinedPath);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public async Task LoadAsync_MissingDocument_ReturnsDefaultForStartup()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(Path.Combine(directory.Path, "document.json"));

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(AtomicStoreLoadStatus.Missing, result.Status);
        Assert.AreEqual("default", result.Document.Value);
    }

    private static AtomicJsonStore<TestDocument> CreateStore(string path) =>
        new(path, 1, () => new TestDocument(1, "default"), new FixedTimeProvider(CorruptAt));

    private sealed record TestDocument(int SchemaVersion, string Value) : VersionedDocument(SchemaVersion)
    {
        public override bool HasValidShape => Value is not null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-atomic-store-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
