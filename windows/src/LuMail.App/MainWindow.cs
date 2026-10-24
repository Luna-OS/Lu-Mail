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

        AppWindow.Resize(new SizeInt32 { Width = 1360, Height = 860 });
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LuMail.ico"));

        // Geladenes Update beim Beenden still installieren (ohne Neustart).
        Closed += (_, _) => App.Updates?.InstallOnExitIfReady();
    }

    public void ShowError(string message) => _page.ShowMessage(message, isError: true);
}
