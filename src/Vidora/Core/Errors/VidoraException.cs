namespace Vidora.Core.Errors;

public sealed class VidoraException(VidoraError error) : Exception(error.TechnicalDetail)
{
    public VidoraError Error { get; } = error;
}
