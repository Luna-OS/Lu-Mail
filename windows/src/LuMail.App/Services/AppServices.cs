using System.Diagnostics;
using LuMail.Core.Auth;
using LuMail.Core.Catalog;
using LuMail.Core.Login;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using LuMail.Core.Storage;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace LuMail.App.Services;

/// <summary>Composition Root: verbindet die plattformneutralen Core-Dienste mit Windows-Implementierungen.</summary>
public sealed class AppServices
{
    public AppPaths Paths { get; }
    public ProviderCatalog Providers { get; }
    public CategoryCatalog Categories { get; }
    public ThemeCatalog Theme { get; }
    public ISecretStore Secrets { get; }
    public AccountStore Accounts { get; }
    public RuleStore Rules { get; }
    public AppSettings Settings { get; }
    public OAuthTokenManager Tokens { get; }
    public MailService Mail { get; }
    public BrowserLoginService Login { get; }
    public AccountSetupService Setup { get; }
    public ContactStore Contacts { get; }
    public SnoozeStore Snoozes { get; }
    public OutboxStore Outbox { get; }
    public OfflineCache Cache { get; }
    public TrackerDetector Trackers { get; } = TrackerDetector.LoadEmbedded();
    public PhishingDetector Phishing { get; } = PhishingDetector.LoadEmbedded();
    /// <summary>HttpClient ohne Cookies für One-Click-Abbestellen.</summary>
    public HttpClient UnsubscribeHttp { get; } = OneClickUnsubscribe.CreateClient();

    private AppServices(AppPaths paths)
    {
        Paths = paths;
        Providers = ProviderCatalog.LoadEmbedded();
        Categories = CategoryCatalog.LoadEmbedded();
        Theme = ThemeCatalog.LoadEmbedded();
        Secrets = new DpapiSecretStore(paths.SecretsFile);
        Accounts = new AccountStore(paths.AccountsFile);
        Rules = new RuleStore(paths.RulesFile);
        Settings = AppSettings.Load(paths.SettingsFile);

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Lu-Mail/0.1 (Windows)");
        var oauth = new OAuthClient(http);
        Tokens = new OAuthTokenManager(oauth, Secrets, LoadOAuthConfig);
        Mail = new MailService(Providers, Categories, new StoredCredentialSource(Secrets, Tokens), Rules);
        Login = new BrowserLoginService(OpenInBrowser, oauth);
        Setup = new AccountSetupService(Login, Mail, Accounts, Secrets, Tokens, LoadOAuthConfig);
        Contacts = new ContactStore(paths.ContactsFile);
        Snoozes = new SnoozeStore(paths.SnoozedFile);
        Outbox = new OutboxStore(paths.OutboxDirectory);
        Cache = new OfflineCache(paths.CacheDirectory);
        Mail.Cache = Settings.OfflineCache ? Cache : null;
        Setup.AccountRemoved += (_, account) =>
        {
            Cache.RemoveAccount(account.Id);
            Snoozes.RemoveAccount(account.Id);
        };
    }

    public static AppServices Create() => new(AppPaths.Default().EnsureCreated());

    /// <summary>oauth.json wird bei jedem Zugriff neu gelesen (Änderungen ohne Neustart).</summary>
    public OAuthClientConfig LoadOAuthConfig() => OAuthClientConfig.Load(Paths.OAuthFile);

    public void SaveSettings() => Settings.Save(Paths.SettingsFile);

    /// <summary>Anzeigename einer Kategorie-Id (eingebaut oder eigene).</summary>
    public string CategoryName(string? id)
    {
        if (MessageCategories.TryParse(id, out var builtIn)) return Categories.DisplayName(builtIn);
        return Rules.FindCategory(id)?.Name ?? id ?? "";
    }

    /// <summary>Farbe (Hex) einer Kategorie-Id.</summary>
    public string CategoryColor(string? id)
    {
        if (id is not null && Theme.CategoryColors.TryGetValue(id, out var hex)) return hex;
        return Rules.FindCategory(id)?.Color ?? "#8B7CFF";
    }

    /// <summary>Öffnet eine URL im Standardbrowser.</summary>
    public static void OpenInBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

    /// <summary>Öffnet einen Ordner im Explorer.</summary>
    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}

/// <summary>Pinsel aus Hex-Farben (Anbieter, Kategorien) – zwischengespeichert.</summary>
public static class BrushCache
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush FromHex(string hex)
    {
        if (Cache.TryGetValue(hex, out var brush)) return brush;
        var (a, r, g, b) = ThemeCatalog.ParseHex(hex);
        brush = new SolidColorBrush(ColorHelper.FromArgb(a, r, g, b));
        Cache[hex] = brush;
        return brush;
    }

    public static SolidColorBrush ForProvider(Provider? provider) => FromHex(provider?.Color ?? "#8B7CFF");

    public static SolidColorBrush ForCategory(MessageCategory category) => ForCategory(category.ToId());

    public static SolidColorBrush ForCategory(string? categoryId) => FromHex(App.Services.CategoryColor(categoryId));
}
