using System.Windows;
using Vidora.Core.Errors;
using Vidora.Features.Download;
using Vidora.Infrastructure.Processes;

namespace Vidora.Tests.Infrastructure.Processes;

[TestClass]
public sealed class YtDlpErrorClassifierTests
{
    [TestMethod]
    [DataRow("ERROR: input is not a valid URL", VidoraErrorCode.InvalidUrl, "Error.InvalidUrl", "Action.CheckUrl", false)]
    [DataRow("ERROR: Unsupported URL: https://example.test/video", VidoraErrorCode.UnsupportedSource, "Error.UnsupportedSource", "Action.UseSupportedSource", false)]
    [DataRow("ERROR: Video unavailable", VidoraErrorCode.Unavailable, "Error.Unavailable", "Action.UsePublicVideo", false)]
    [DataRow("ERROR: Login required to view this content", VidoraErrorCode.AuthenticationRequired, "Error.AuthenticationRequired", "Action.UsePublicVideo", false)]
    [DataRow("ERROR: Unable to download webpage: The operation timed out", VidoraErrorCode.NetworkFailure, "Error.NetworkFailure", "Action.TryAgain", true)]
    [DataRow("ERROR: Unable to open for writing: Permission denied", VidoraErrorCode.DiskFailure, "Error.DiskFailure", "Action.ChooseAnotherFolder", false)]
    [DataRow("ERROR: Unable to write data: No space left on device", VidoraErrorCode.InsufficientSpace, "Error.InsufficientSpace", "Action.FreeSpace", false)]
    [DataRow("ERROR: ffmpeg not found. Please install ffmpeg", VidoraErrorCode.DependencyMissing, "Error.DependencyMissing", "Action.OpenSettings", false)]
    [DataRow("ERROR: ffmpeg exited with code 1", VidoraErrorCode.DependencyInvalid, "Error.DependencyInvalid", "Action.OpenSettings", false)]
    [DataRow("ERROR: Your version of yt-dlp is out of date; please update to the latest version", VidoraErrorCode.ExtractorOutdated, "Error.ExtractorOutdated", "Action.UpdateYtDlp", true)]
    [DataRow("ERROR: Postprocessing: Conversion failed", VidoraErrorCode.PostProcessingFailure, "Error.PostProcessingFailure", "Action.TryAgain", true)]
    [DataRow("ERROR: an unrecognized failure", VidoraErrorCode.Unknown, "Error.DownloadFailed", "Action.TryAgain", true)]
    public void Classify_KnownMarker_ReturnsStableError(
        string standardError,
        VidoraErrorCode expectedCode,
        string expectedMessageKey,
        string expectedActionKey,
        bool expectedRetryable)
    {
        var result = Result(standardError);

        var actual = YtDlpErrorClassifier.Classify(result, YtDlpOperation.Download);

        Assert.AreEqual(expectedCode, actual.Code);
        Assert.AreEqual(expectedMessageKey, actual.UserMessageKey);
        Assert.AreEqual(expectedActionKey, actual.SuggestedActionKey);
        Assert.AreEqual(expectedRetryable, actual.IsRetryable);
        StringAssert.Contains(actual.TechnicalDetail, "exit code 1");
        StringAssert.Contains(actual.TechnicalDetail, standardError);
    }

    [TestMethod]
    public void Classify_Cancelled_TakesPriorityAndNeverReturnsFailure()
    {
        var result = Result("ERROR: No space left on device; ffmpeg not found; video unavailable");

        var actual = YtDlpErrorClassifier.Classify(
            result,
            YtDlpOperation.Download,
            wasCancelled: true);

        Assert.AreEqual(VidoraErrorCode.Cancelled, actual.Code);
        Assert.AreEqual("Error.Cancelled", actual.UserMessageKey);
        Assert.AreEqual("Action.TryAgain", actual.SuggestedActionKey);
        Assert.IsTrue(actual.IsRetryable);
    }

    [TestMethod]
    [DataRow("ERROR: Permission denied because no space left on device", VidoraErrorCode.InsufficientSpace)]
    [DataRow("ERROR: Video unavailable; login required", VidoraErrorCode.AuthenticationRequired)]
    [DataRow("ERROR: Video unavailable because ffmpeg not found", VidoraErrorCode.DependencyMissing)]
    [DataRow("ERROR: ffmpeg exited with code 1; please update to the latest version", VidoraErrorCode.DependencyInvalid)]
    [DataRow("ERROR: Unable to download webpage: Permission denied", VidoraErrorCode.NetworkFailure)]
    public void Classify_OverlappingMarkers_UsesStablePriority(
        string standardError,
        VidoraErrorCode expectedCode)
    {
        var actual = YtDlpErrorClassifier.Classify(Result(standardError), YtDlpOperation.Download);

        Assert.AreEqual(expectedCode, actual.Code);
    }

    [TestMethod]
    public void Classify_UnknownDiscovery_UsesDiscoveryMessage()
    {
        var actual = YtDlpErrorClassifier.Classify(
            Result("ERROR: an unrecognized failure"),
            YtDlpOperation.Discovery);

        Assert.AreEqual(VidoraErrorCode.Unknown, actual.Code);
        Assert.AreEqual("Error.DiscoveryFailed", actual.UserMessageKey);
    }

    [TestMethod]
    public void Classify_TechnicalDetail_RedactsSecretsAndExcludesStandardOutput()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = new ProcessResult(
            7,
            ["{\"rawMetadata\":\"must-not-be-copied\"}"],
            [$"ERROR: unexpected failure at {profile}\\Videos\\clip.mp4?token=top-secret"]);

        var actual = YtDlpErrorClassifier.Classify(result, YtDlpOperation.Download);

        StringAssert.Contains(actual.TechnicalDetail, "exit code 7");
        StringAssert.Contains(actual.TechnicalDetail, "token=[REDACTED]");
        StringAssert.Contains(actual.TechnicalDetail, "%USERPROFILE%");
        Assert.IsFalse(actual.TechnicalDetail.Contains("top-secret", StringComparison.Ordinal));
        Assert.IsFalse(actual.TechnicalDetail.Contains("rawMetadata", StringComparison.Ordinal));
    }

    [STATestMethod]
    [DoNotParallelize]
    public void ClassifierResourceKeys_ExistInVietnameseCatalog()
    {
        _ = new DownloadView();
        var resources = new ResourceDictionary
        {
            Source = new Uri("/Vidora;component/Resources/Strings.vi-VN.xaml", UriKind.Relative)
        };
        var keys = new[]
        {
            "Error.InvalidUrl", "Error.UnsupportedSource", "Error.Unavailable",
            "Error.AuthenticationRequired", "Error.NetworkFailure", "Error.DiskFailure",
            "Error.InsufficientSpace", "Error.DependencyMissing", "Error.DependencyInvalid",
            "Error.ExtractorOutdated", "Error.PostProcessingFailure", "Error.Cancelled",
            "Error.DownloadFailed", "Error.DiscoveryFailed", "Action.CheckUrl",
            "Action.UseSupportedSource", "Action.UsePublicVideo", "Action.TryAgain",
            "Action.ChooseAnotherFolder", "Action.FreeSpace", "Action.OpenSettings",
            "Action.UpdateYtDlp"
        };

        foreach (var key in keys)
        {
            Assert.IsTrue(resources.Contains(key), $"Missing resource key: {key}");
        }
    }

    private static ProcessResult Result(string standardError) => new(1, [], [standardError]);
}
