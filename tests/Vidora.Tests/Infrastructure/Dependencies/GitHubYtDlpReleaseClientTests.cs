using System.Net;
using System.Text;
using Vidora.Infrastructure.Dependencies;

namespace Vidora.Tests.Infrastructure.Dependencies;

[TestClass]
public sealed class GitHubYtDlpReleaseClientTests
{
    private const string Version = "2026.10.26";
    private const string Checksum = "f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d";
    private static readonly Uri ExecutableUri = new(
        $"https://github.com/yt-dlp/yt-dlp/releases/download/{Version}/yt-dlp.exe");
    private static readonly Uri ManifestUri = new(
        $"https://github.com/yt-dlp/yt-dlp/releases/download/{Version}/SHA2-256SUMS");

    [TestMethod]
    public async Task GetLatestAsync_OfficialRelease_ParsesAssetsChecksumAndSetsUserAgent()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new QueueHandler(requests,
            JsonResponse(ReleaseJson(ExecutableUri, ManifestUri)),
            RedirectResponse(new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/yt-dlp-checksums")),
            TextResponse($"{Checksum}  yt-dlp.exe\n"));
        var client = new GitHubYtDlpReleaseClient(new HttpClient(handler));

        var release = await client.GetLatestAsync(CancellationToken.None);

        Assert.AreEqual(Version, release.Version);
        Assert.AreEqual(ExecutableUri, release.ExecutableUri);
        Assert.AreEqual(ManifestUri, release.ChecksumManifestUri);
        Assert.AreEqual(Checksum, release.ExpectedSha256);
        CollectionAssert.AreEqual(
            new[]
            {
                "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest",
                ManifestUri.AbsoluteUri,
                "https://release-assets.githubusercontent.com/github-production-release-asset/yt-dlp-checksums"
            },
            requests.Select(request => request.RequestUri!.AbsoluteUri).ToArray());
        Assert.IsTrue(requests.All(request => request.Headers.UserAgent.Any(product => product.Product?.Name == "Vidora")));
    }

    [TestMethod]
    public async Task GetLatestAsync_AssetOutsideOfficialRepository_RejectsBeforeDownload()
    {
        var handler = new QueueHandler([], JsonResponse(ReleaseJson(
            new Uri("https://evil.example/yt-dlp.exe"),
            ManifestUri)));
        var client = new GitHubYtDlpReleaseClient(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetLatestAsync(CancellationToken.None));

        Assert.AreEqual(0, handler.RemainingResponses);
    }

    [TestMethod]
    public async Task GetLatestAsync_RedirectOutsideApprovedGitHubHosts_IsRejected()
    {
        var handler = new QueueHandler(
            [],
            JsonResponse(ReleaseJson(ExecutableUri, ManifestUri)),
            RedirectResponse(new Uri("https://evil.example/SHA2-256SUMS")));
        var client = new GitHubYtDlpReleaseClient(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetLatestAsync(CancellationToken.None));

        Assert.AreEqual(0, handler.RemainingResponses);
    }

    [TestMethod]
    public async Task DownloadAssetAsync_UnapprovedInputUri_IsRejectedWithoutRequest()
    {
        var requests = new List<HttpRequestMessage>();
        var client = new GitHubYtDlpReleaseClient(new HttpClient(new QueueHandler(requests)));
        await using var destination = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAssetAsync(
            new Uri("https://evil.example/yt-dlp.exe"),
            destination,
            CancellationToken.None));

        Assert.IsEmpty(requests);
    }

    private static string ReleaseJson(Uri executableUri, Uri manifestUri) => $$"""
        {
          "tag_name": "{{Version}}",
          "assets": [
            { "name": "yt-dlp.exe", "browser_download_url": "{{executableUri}}" },
            { "name": "SHA2-256SUMS", "browser_download_url": "{{manifestUri}}" }
          ]
        }
        """;

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage TextResponse(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "text/plain")
    };

    private static HttpResponseMessage RedirectResponse(Uri location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = location;
        return response;
    }

    private sealed class QueueHandler(
        List<HttpRequestMessage> requests,
        params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> remaining = new(responses);

        public int RemainingResponses => remaining.Count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add(request);
            return Task.FromResult(remaining.Dequeue());
        }
    }
}
