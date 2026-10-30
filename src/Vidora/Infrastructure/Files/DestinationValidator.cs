using System.IO;
using Vidora.Core.Errors;

namespace Vidora.Infrastructure.Files;

public static class DestinationValidator
{
    public static string Validate(string destinationDirectory)
    {
        try
        {
            var fullPath = Path.GetFullPath(destinationDirectory);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException("The destination directory does not exist.");
            }

            var probePath = Path.Combine(fullPath, $".vidora-write-{Guid.NewGuid():N}.tmp");
            using (new FileStream(
                       probePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       1,
                       FileOptions.DeleteOnClose))
            {
            }

            return fullPath;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new VidoraException(new VidoraError(
                VidoraErrorCode.DiskFailure,
                "Error.DestinationUnavailable",
                "Action.ChooseAnotherFolder",
                exception.Message,
                true));
        }
    }
}
