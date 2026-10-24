using LuMail.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LuMail.App.Views;

/// <summary>Eigenes Fenster zum Verfassen (mehrere gleichzeitig möglich).</summary>
public sealed class ComposeWindow : Window
{
    private readonly ComposePage _page;

    public ComposeWindow()
    {
        Title = "Neue E-Mail – Lu-Mail";
        _page = new ComposePage { HostWindow = this };
        Content = _page;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_page.TitleBarElement);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        if (MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32 { Width = 760, Height = 680 });

        _page.Sent += (_, _) => Close();
        _page.SubjectChanged += (_, subject) => Title = $"{subject} – Lu-Mail";
    }

    public void Initialize(IReadOnlyList<Account> accounts, Account? from, OutgoingMessage? draft, ViewModels.MainViewModel? vm = null)
    {
        _page.Initialize(accounts, from, draft, vm);
        Services.ThemeService.ApplyMode(this, App.Services.Settings);
    }
}
