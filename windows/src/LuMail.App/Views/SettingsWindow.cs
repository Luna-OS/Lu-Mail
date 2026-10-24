using LuMail.App.Services;
using LuMail.App.ViewModels;
using LuMail.Core.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LuMail.App.Views;

/// <summary>Einstellungen als eigenes, in der Größe veränderbares Fenster mit Seitenleiste (FUNKTIONEN §19).</summary>
public sealed class SettingsWindow : Window
{
    public SettingsPage Page { get; }

    public SettingsWindow(IReadOnlyList<Account> accounts, MainViewModel vm)
    {
        Title = "Einstellungen – Lu-Mail";
        Page = new SettingsPage();
        Page.Initialize(accounts, vm);
        Content = Page;
        if (MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop();
        WindowSizing.ResizeAndCenter(this, 920, 660);
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "LuMail.ico")); }
        catch (Exception) { }
        ThemeService.ApplyMode(this, App.Services.Settings);
        Page.AppearanceChanged += (_, _) => ThemeService.ApplyMode(this, App.Services.Settings);
    }
}
