namespace Vidora.Core.Errors;

public sealed record VidoraError(
    VidoraErrorCode Code,
    string UserMessageKey,
    string SuggestedActionKey,
    string TechnicalDetail,
    bool IsRetryable);
