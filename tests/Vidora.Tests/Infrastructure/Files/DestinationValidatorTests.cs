using Vidora.Core.Errors;
using Vidora.Infrastructure.Files;

namespace Vidora.Tests.Infrastructure.Files;

[TestClass]
public sealed class DestinationValidatorTests
{
    [TestMethod]
    public void Validate_WritableDirectory_ReturnsCanonicalPathWithoutProbeFile()
    {
        using var directory = new TemporaryDirectory();

        var result = DestinationValidator.Validate(directory.Path);

        Assert.AreEqual(Path.GetFullPath(directory.Path), result);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [TestMethod]
    public void Validate_MissingDirectory_ThrowsStableDiskError()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"vidora-missing-{Guid.NewGuid():N}");

        var exception = Assert.Throws<VidoraException>(() => DestinationValidator.Validate(missing));

        Assert.AreEqual(VidoraErrorCode.DiskFailure, exception.Error.Code);
    }

    [TestMethod]
    public void Validate_EmptyPath_ThrowsStableDiskError()
    {
        var exception = Assert.Throws<VidoraException>(() => DestinationValidator.Validate(string.Empty));

        Assert.AreEqual(VidoraErrorCode.DiskFailure, exception.Error.Code);
    }

    [TestMethod]
    public void Validate_FilePath_ThrowsStableDiskError()
    {
        var file = Path.GetTempFileName();
        try
        {
            var exception = Assert.Throws<VidoraException>(() => DestinationValidator.Validate(file));

            Assert.AreEqual(VidoraErrorCode.DiskFailure, exception.Error.Code);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("vidora-destination-test-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
