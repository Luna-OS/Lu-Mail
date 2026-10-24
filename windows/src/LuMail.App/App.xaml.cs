using LuMail.App.Services;
using Microsoft.UI.Xaml;

namespace LuMail.App;

/// <summary>Einstiegspunkt der WinUI-App (Main wird vom XAML-Compiler erzeugt).</summary>
public partial class App : Application
{
    private MainWindow? _window;

    /// <summary>Gemeinsame Dienste (Composition Root).</summary>
    public static AppServices Services { get; private set; } = null!;

    /// <summary>Auto-Updater.</summary>
    public static UpdateManager Updates { get; private set; } = null!;

    /// <summary>Systemmitteilungen.</summary>
    public static NotificationService Notifications { get; } = new();

    /// <summary>App wurde über einen Klick auf eine Mitteilung gestartet (wird nach dem Laden geöffnet).</summary>
    public static IDictionary<string, string>? PendingNotification { get; set; }

    public App()
    {
        // WebView2 legt sonst seinen Datenordner neben der .exe an (evtl. schreibgeschützt).
        var webViewData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuMail", "WebView2");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewData);

        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = AppServices.Create();
        Updates = new UpdateManager(Services);
        ThemeService.ApplyPalette(Services.Theme, Services.Settings);

        // Mitteilungen registrieren, bevor die Aktivierungsart geprüft wird.
        Notifications.Register();
        try
        {
            var activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.AppNotification
                && activation.Data is Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs notification)
                PendingNotification = notification.Arguments;
        }
        catch (Exception)
        {
        }

        _window = new MainWindow();
        Notifications.Activated += (_, args) => _window.DispatcherQueue.TryEnqueue(() => _window.OpenFromNotification(args));
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Unerwartete Fehler anzeigen statt die App zu beenden.
        e.Handled = true;
        _window?.ShowError(e.Exception?.Message ?? e.Message);
    }
}
