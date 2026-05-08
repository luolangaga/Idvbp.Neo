using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Idvbp.Neo.Services;

public enum AppNotificationSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public sealed partial class AppNotification : ObservableObject
{
    public AppNotification(Guid id, string title, string message, AppNotificationSeverity severity, Action<AppNotification> dismiss)
    {
        Id = id;
        Title = title;
        Message = message;
        Severity = severity;
        DismissCommand = new RelayCommand(() => dismiss(this));
    }

    public Guid Id { get; }

    public string Title { get; }

    public string Message { get; }

    public AppNotificationSeverity Severity { get; }

    public IRelayCommand DismissCommand { get; }
}

public sealed class AppNotificationService
{
    private const int MaxNotifications = 5;

    public ObservableCollection<AppNotification> Notifications { get; } = [];

    public void Info(string message, string title = "提示", TimeSpan? timeout = null)
        => Show(title, message, AppNotificationSeverity.Info, timeout);

    public void Success(string message, string title = "完成", TimeSpan? timeout = null)
        => Show(title, message, AppNotificationSeverity.Success, timeout);

    public void Warning(string message, string title = "注意", TimeSpan? timeout = null)
        => Show(title, message, AppNotificationSeverity.Warning, timeout);

    public void Error(string message, string title = "请求失败", TimeSpan? timeout = null)
        => Show(title, message, AppNotificationSeverity.Error, timeout ?? TimeSpan.FromSeconds(7));

    public void Error(Exception exception, string title = "请求失败", TimeSpan? timeout = null)
        => Error(FormatException(exception), title, timeout);

    public void Show(string title, string message, AppNotificationSeverity severity, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var notification = new AppNotification(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(title) ? "提示" : title.Trim(),
            message.Trim(),
            severity,
            Dismiss);

        Dispatcher.UIThread.Post(() =>
        {
            Notifications.Add(notification);
            while (Notifications.Count > MaxNotifications)
            {
                Notifications.RemoveAt(0);
            }
        });

        _ = DismissAfterDelayAsync(notification.Id, timeout ?? TimeSpan.FromSeconds(4.5));
    }

    private async Task DismissAfterDelayAsync(Guid id, TimeSpan timeout)
    {
        await Task.Delay(timeout);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var notification = Notifications.FirstOrDefault(x => x.Id == id);
            if (notification is not null)
            {
                Notifications.Remove(notification);
            }
        });
    }

    private void Dismiss(AppNotification notification)
    {
        Dispatcher.UIThread.Post(() => Notifications.Remove(notification));
    }

    private static string FormatException(Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: not null } httpException)
        {
            return $"HTTP {(int)httpException.StatusCode} {httpException.StatusCode}: {httpException.Message}";
        }

        return exception.Message;
    }
}
