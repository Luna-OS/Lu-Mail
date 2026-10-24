using LuMail.Core.Catalog;
using LuMail.Core.Storage;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LuMail.App.Services;

/// <summary>
/// Erscheinungsbild (FUNKTIONEN §16): Variante (Luna/Neumond/Vollmond), Akzentfarbe und Modus System/Hell/Dunkel.
/// Die Pinsel in App.xaml → ThemeDictionaries werden zur Laufzeit umgefärbt; ThemeResource-Verweise folgen sofort.
/// </summary>
public static class ThemeService
{
    public static void ApplyPalette(ThemeCatalog theme, AppSettings settings)
    {
        var dictionaries = Application.Current.Resources.ThemeDictionaries;
        foreach (var (key, dark) in new[] { ("Light", false), ("Default", true), ("Dark", true) })
        {
            if (!dictionaries.TryGetValue(key, out var value) || value is not ResourceDictionary rd) continue;
            foreach (var (token, hex) in theme.Palette(settings.ThemeVariant, dark, settings.Accent))
            {
                if (rd.TryGetValue(ThemeCatalog.ResourceKey(token), out var brushObj) && brushObj is SolidColorBrush brush)
                {
                    var (a, r, g, b) = ThemeCatalog.ParseHex(hex);
                    brush.Color = ColorHelper.FromArgb(a, r, g, b);
                }
            }
        }
    }

    public static ElementTheme RequestedTheme(AppSettings settings) => settings.ThemeMode switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>Modus auf ein Fenster anwenden.</summary>
    public static void ApplyMode(Window window, AppSettings settings)
    {
        if (window.Content is FrameworkElement root)
        {
            var requested = RequestedTheme(settings);
            // Umschalten erzwingt die Neuauswertung aller ThemeResource-Verweise (auch nach Palettenwechsel).
            root.RequestedTheme = requested == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            root.RequestedTheme = requested;
        }
    }
}
