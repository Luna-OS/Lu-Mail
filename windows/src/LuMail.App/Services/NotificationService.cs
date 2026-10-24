using LuMail.App.ViewModels;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace LuMail.App.Services;

/// <summary>Systemmitteilungen (Windows App SDK, funktioniert auch unpackaged). Klick → Nachricht öffnen.</summary>
public sealed class NotificationService
{
    private bool _registered;

    /// <summary>Klick auf eine Mitteilung (Argumente: action, account, folder, uid). Kann auf einem Hintergrund-Thread kommen.</summary>
    public event EventHandler<IDictionary<string, string>>? Activated;

    public void Register()
    {
        if (_registered) return;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception)
        {
            // Mitteilungen nicht verfügbar (z. B. alte Windows-Version) – App läuft ohne weiter.
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        Activated?.Invoke(this, args.Arguments);

    /// <summary>„Absender – Betreff“.</summary>
    public void ShowMessage(MessageItem item, string? prefix = null)
    {
        if (!_registered) return;
        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("action", "open")
                .AddArgument("account", item.Account.Id)
                .AddArgument("folder", item.Summary.FolderPath)
                .AddArgument("uid", item.Summary.Uid.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AddText((prefix ?? "") + $"{item.Sender} – {item.Subject}");
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception)
        {
        }
    }

    public void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception) { }
        _registered = false;
    }
}
