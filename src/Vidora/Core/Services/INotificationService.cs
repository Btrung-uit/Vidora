namespace Vidora.Core.Services;

public interface INotificationService : IDisposable
{
    void Show(string title, string message);
}
