using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Vidora.Infrastructure.Dependencies;

public sealed class GitHubYtDlpReleaseClient(HttpClient httpClient) : IYtDlpReleaseClient
{
    private const int MaxRedirects = 5;
    private const string ExecutableName = "yt-dlp.exe";
    private const string ManifestName = "SHA2-256SUMS";
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");

    public async Task<YtDlpReleaseInfo> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(LatestReleaseUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var version = root.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(version) || version.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new InvalidDataException("The yt-dlp release version is invalid.");
        }

        var assets = root.GetProperty("assets");
        var executableUri = FindAsset(assets, version, ExecutableName);
        var manifestUri = FindAsset(assets, version, ManifestName);
        var checksum = await ReadExpectedChecksumAsync(manifestUri, cancellationToken);
        return new YtDlpReleaseInfo(version, executableUri, manifestUri, checksum);
    }

    public async Task DownloadAssetAsync(
        Uri assetUri,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ValidateRepositoryAssetUri(assetUri);
        using var response = await SendAssetAsync(assetUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(destination, cancellationToken);
    }

    private async Task<string> ReadExpectedChecksumAsync(
        Uri manifestUri,
        CancellationToken cancellationToken)
    {
        using var response = await SendAssetAsync(manifestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var manifest = await response.Content.ReadAsStringAsync(cancellationToken);
        var matches = manifest
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseChecksumLine)
            .Where(static match => match is not null)
            .ToArray();
        return matches.Length == 1
            ? matches[0]!
            : throw new InvalidDataException("The checksum manifest does not contain one yt-dlp.exe entry.");
    }

    private static string? ParseChecksumLine(string line)
    {
        var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length != 64)
        {
            return null;
        }

        var fileName = parts[1].TrimStart('*');
        if (!fileName.Equals(ExecutableName, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            Convert.FromHexString(parts[0]);
            return parts[0].ToLowerInvariant();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAssetAsync(
        Uri assetUri,
        CancellationToken cancellationToken)
    {
        ValidateRepositoryAssetUri(assetUri);
        var current = assetUri;
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            var response = await SendAsync(current, cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                ValidateDownloadResponseUri(response.RequestMessage?.RequestUri ?? current);
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (redirect == MaxRedirects || location is null || !location.IsAbsoluteUri)
            {
                throw new InvalidDataException("The GitHub asset redirect is invalid.");
            }

            ValidateDownloadResponseUri(location);
            current = location;
        }

        throw new InvalidDataException("Too many GitHub asset redirects.");
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Vidora", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static Uri FindAsset(JsonElement assets, string version, string name)
    {
        var matches = assets.EnumerateArray()
            .Where(asset => asset.TryGetProperty("name", out var assetName) &&
                            assetName.GetString() == name)
            .Select(asset => asset.GetProperty("browser_download_url").GetString())
            .ToArray();
        if (matches.Length != 1 || !Uri.TryCreate(matches[0], UriKind.Absolute, out var uri))
        {
            throw new InvalidDataException($"The official {name} release asset is missing.");
        }

        var expectedPath = $"/yt-dlp/yt-dlp/releases/download/{version}/{name}";
        if (!IsHttpsHost(uri, "github.com") || uri.AbsolutePath != expectedPath || !string.IsNullOrEmpty(uri.Query))
        {
            throw new InvalidDataException($"The {name} release asset URL is not official.");
        }

        return uri;
    }

    private static void ValidateRepositoryAssetUri(Uri uri)
    {
        if (!IsHttpsHost(uri, "github.com") ||
            !uri.AbsolutePath.StartsWith(
                "/yt-dlp/yt-dlp/releases/download/",
                StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query))
        {
            throw new InvalidDataException("The release asset URL is not official.");
        }
    }

    private static void ValidateDownloadResponseUri(Uri uri)
    {
        if (!IsHttpsHost(uri, "release-assets.githubusercontent.com") &&
            !IsOfficialGitHubAsset(uri))
        {
            throw new InvalidDataException("The GitHub asset redirect host is not approved.");
        }
    }

    private static bool IsOfficialGitHubAsset(Uri uri) =>
        IsHttpsHost(uri, "github.com") &&
        uri.AbsolutePath.StartsWith(
            "/yt-dlp/yt-dlp/releases/download/",
            StringComparison.Ordinal);

    private static bool IsHttpsHost(Uri uri, string host) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort;

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;
}
