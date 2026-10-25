using LuMail.App.Services;
using LuMail.App.Views;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LuMail.App;

/// <summary>Hauptfenster: Mica-Hintergrund, eigene Titelleiste, Inhalt = <see cref="MainPage"/>.</summary>
public sealed class MainWindow : Window
{
    private readonly MainPage _page;

    private TrayService? _tray;

    public MainWindow()
    {
        Title = "Lu-Mail";
        _page = new MainPage();
        _page.HostWindow = this;
        Content = _page;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_page.TitleBarElement);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
            _page.UseTransparentBackground();
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
            _page.UseTransparentBackground();
        }

        WindowSizing.ResizeAndCenter(this, 1360, 860);
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LuMail.ico"));

        ThemeService.ApplyMode(this, App.Services.Settings);

        // Infobereich + Taskleisten-Zähler
        _tray = new TrayService(WinRT.Interop.WindowNative.GetWindowHandle(this),
            openApp: () => DispatcherQueue.TryEnqueue(BringToFront),
            openMessage: item => DispatcherQueue.TryEnqueue(() => { BringToFront(); _ = _page.OpenItemAsync(item); }));
        _page.Tray = _tray;

        Activated += (_, args) => _page.OnWindowActivationChanged(args.WindowActivationState != WindowActivationState.Deactivated);

        // Nachthimmel anhalten, solange das Fenster minimiert oder verborgen ist (§22).
        VisibilityChanged += (_, args) => _page.SetWindowVisible(args.Visible && !IsMinimized());
        AppWindow.Changed += (_, _) => _page.SetWindowVisible(AppWindow.IsVisible && !IsMinimized());

        Closed += (_, _) =>
        {
            // Geladenes Update beim Beenden still installieren (ohne Neustart).
            App.Updates?.InstallOnExitIfReady();
            _ = _page.Vm.StopBackgroundAsync();
            _tray?.Dispose();
            App.Notifications.Unregister();
        };
    }

    private bool IsMinimized() =>
        AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
        && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized;

    private void BringToFront()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
            && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            presenter.Restore();
        Activate();
    }

    public void OpenFromNotification(IDictionary<string, string> args)
    {
        BringToFront();
        _page.OpenFromNotification(args);
    }

    public void ShowError(string message) => _page.ShowMessage(message, isError: true);
}
