namespace Vidora.Infrastructure.Dependencies;

public sealed record YtDlpReleaseInfo(
    string Version,
    Uri ExecutableUri,
    Uri ChecksumManifestUri,
    string ExpectedSha256);
