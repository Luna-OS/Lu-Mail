using LuMail.App.Services;
using Microsoft.UI.Xaml;

namespace LuMail.App;

/// <summary>Einstiegspunkt der WinUI-App (Main wird vom XAML-Compiler erzeugt).</summary>
public partial class App : Application
{
    private MainWindow? _window;

    /// <summary>Gemeinsame Dienste (Composition Root).</summary>
    public static AppServices Services { get; private set; } = null!;

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
        _window = new MainWindow();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Unerwartete Fehler anzeigen statt die App zu beenden.
        e.Handled = true;
        _window?.ShowError(e.Exception?.Message ?? e.Message);
    }
}
