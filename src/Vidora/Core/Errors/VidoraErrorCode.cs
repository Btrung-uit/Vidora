namespace Vidora.Core.Errors;

public enum VidoraErrorCode
{
    InvalidUrl,
    UnsupportedSource,
    Unavailable,
    AuthenticationRequired,
    NetworkFailure,
    DiskFailure,
    InsufficientSpace,
    DependencyMissing,
    DependencyInvalid,
    ExtractorOutdated,
    PostProcessingFailure,
    Cancelled,
    Unknown
}
