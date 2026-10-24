using LuMail.Core.Catalog;
using LuMail.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace LuMail.App.Services;

/// <summary>
/// Web-Konten (FUNKTIONEN §20): pro Konto ein dauerhaftes WebView2-Profil (Profilname = Konto-Id) im gemeinsamen
/// Benutzerdatenordner <c>%LOCALAPPDATA%\LuMail\WebView2</c>. Pro Konto bleibt ein WebView2 geladen (ausgeblendet,
/// wenn nicht angezeigt), damit der Ungelesen-Zähler aus dem Seitentitel aktuell bleibt.
/// </summary>
public sealed class WebAccountService
{
    private sealed class Entry
    {
        public required Account Account { get; init; }
        public required Provider Provider { get; init; }
        public required WebView2 View { get; init; }
        public Task? Ready { get; set; }
        public int? Unread { get; set; }
    }

    private readonly AppServices _s;
    private readonly Dictionary<string, Entry> _entries = new();
    private Panel? _host;
    private Func<Task<CoreWebView2Environment>>? _environment;

    public WebAccountService(AppServices services) => _s = services;

    /// <summary>Gemeinsamer Benutzerdatenordner aller WebView2-Instanzen.</summary>
    public static string UserDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuMail", "WebView2");

    /// <summary>Profilname eines Kontos (Guid im "N"-Format).</summary>
    public static string ProfileName(string accountId) =>
        Guid.TryParse(accountId, out var g) ? g.ToString("N") : new string(accountId.Where(char.IsAsciiLetterOrDigit).ToArray());

    /// <summary>Ungelesen-Zahl geändert (Argument: Konto-Id).</summary>
    public event EventHandler<string>? CountChanged;
    /// <summary>Zahl gestiegen → Mitteilung „&lt;Konto&gt;: N neue E-Mail(s)“ (Argumente: Konto, Anzahl neu).</summary>
    public event EventHandler<(Account Account, int NewCount)>? NewMail;
    /// <summary>Konto geändert (z. B. Adresse aus dem Seitentitel übernommen).</summary>
    public event EventHandler<Account>? AccountUpdated;
    /// <summary>Adresse der aktiven Ansicht geändert (Zurück/Vor-Schaltflächen aktualisieren).</summary>
    public event EventHandler? NavigationChanged;

    public Account? ActiveAccount { get; private set; }
    public WebView2? ActiveView => ActiveAccount is not null && _entries.TryGetValue(ActiveAccount.Id, out var e) ? e.View : null;

    public int? Unread(string accountId) => _entries.TryGetValue(accountId, out var e) ? e.Unread : null;
    public int TotalUnread => _entries.Values.Sum(e => e.Unread ?? 0);

    /// <summary>Host-Panel (im visuellen Baum, sonst initialisiert WebView2 nicht) und Umgebung setzen.</summary>
    public void Attach(Panel host, Func<Task<CoreWebView2Environment>> environment)
    {
        _host = host;
        _environment = environment;
    }

    public async Task<CoreWebView2Environment> EnvironmentAsync() =>
        _environment is not null
            ? await _environment()
            : await CoreWebView2Environment.CreateWithOptionsAsync("", UserDataFolder, new CoreWebView2EnvironmentOptions());

    /// <summary>Optionen für das Profil eines Kontos.</summary>
    public async Task<(CoreWebView2Environment Environment, CoreWebView2ControllerOptions Options)> ProfileOptionsAsync(string accountId)
    {
        var environment = await EnvironmentAsync();
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = ProfileName(accountId);
        return (environment, options);
    }

    /// <summary>Lädt für alle Web-Konten eine Ansicht im Hintergrund (Zähler) und entfernt alte.</summary>
    public async Task SyncAccountsAsync(IReadOnlyList<Account> webAccounts)
    {
        foreach (var id in _entries.Keys.Except(webAccounts.Select(a => a.Id)).ToList()) RemoveView(id);
        foreach (var account in webAccounts)
        {
            try { await EnsureAsync(account); }
            catch (Exception) { /* WebView2 nicht verfügbar – Konto zeigt beim Öffnen einen Fehler */ }
        }
    }

    private Task EnsureAsync(Account account)
    {
        if (_entries.TryGetValue(account.Id, out var existing)) return existing.Ready ?? Task.CompletedTask;
        var provider = _s.Providers.Find(account.ProviderId);
        if (provider?.Web is null || _host is null) return Task.CompletedTask;

        var view = new WebView2 { Visibility = Visibility.Visible };
        var entry = new Entry { Account = account, Provider = provider, View = view };
        _entries[account.Id] = entry;
        _host.Children.Add(view);
        entry.Ready = InitializeAsync(entry);
        return entry.Ready;
    }

    private async Task InitializeAsync(Entry entry)
    {
        var (environment, options) = await ProfileOptionsAsync(entry.Account.Id);
        await entry.View.EnsureCoreWebView2Async(environment, options);
        var core = entry.View.CoreWebView2;
        core.DocumentTitleChanged += (_, _) => Evaluate(entry);
        core.SourceChanged += (_, _) =>
        {
            Evaluate(entry);
            if (ActiveAccount?.Id == entry.Account.Id) NavigationChanged?.Invoke(this, EventArgs.Empty);
        };
        core.HistoryChanged += (_, _) =>
        {
            if (ActiveAccount?.Id == entry.Account.Id) NavigationChanged?.Invoke(this, EventArgs.Empty);
        };
        core.NewWindowRequested += (sender, args) => OnNewWindow(entry, sender, args);
        if (ActiveAccount?.Id != entry.Account.Id)
        {
            core.Navigate(WebMail.FolderUrl(entry.Provider.Web!, "primary") ?? entry.Provider.Web!.HomeUrl);
            entry.View.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Pop-ups: gleicher Anbieter → in derselben Ansicht, sonst im Standardbrowser.</summary>
    private static void OnNewWindow(Entry entry, CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        var uri = args.Uri ?? "";
        if (WebMail.RedirectTarget(uri) is { } target) uri = target;
        else if (WebMail.IsInternalHost(entry.Provider.Web!, uri)) { sender.Navigate(uri); return; }
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
            AppServices.OpenInBrowser(parsed);
    }

    /// <summary>Titel auswerten (nur im Posteingang) und Adresse übernehmen.</summary>
    private void Evaluate(Entry entry)
    {
        var core = entry.View.CoreWebView2;
        if (core is null) return;
        var web = entry.Provider.Web!;
        var title = core.DocumentTitle;

        if (!entry.Account.Email.Contains('@') && WebMail.EmailFromTitle(title) is { } email)
        {
            entry.Account.Email = email;
            entry.Account.DisplayName = email.Split('@')[0];
            var stored = _s.Accounts.Load().FirstOrDefault(a => a.Id == entry.Account.Id);
            if (stored is not null)
            {
                stored.Email = email;
                stored.DisplayName = entry.Account.DisplayName;
                _s.Accounts.Upsert(stored);
            }
            AccountUpdated?.Invoke(this, entry.Account);
        }

        if (!WebMail.IsOnInbox(web, core.Source)) return;
        var count = WebMail.UnreadCount(web, title) ?? 0;
        var previous = entry.Unread;
        if (previous == count) return;
        entry.Unread = count;
        CountChanged?.Invoke(this, entry.Account.Id);
        if (previous is not null && count > previous) NewMail?.Invoke(this, (entry.Account, count - previous.Value));
    }

    /// <summary>Ansicht des Kontos zeigen und zum Ordner navigieren; alle anderen ausblenden.</summary>
    public async Task ShowAsync(Account account, string folderKey)
    {
        ActiveAccount = account;
        foreach (var e in _entries.Values) e.View.Visibility = e.Account.Id == account.Id ? Visibility.Visible : Visibility.Collapsed;
        await EnsureAsync(account);
        if (!_entries.TryGetValue(account.Id, out var entry)) return;
        entry.View.Visibility = Visibility.Visible;
        if (entry.Ready is not null) await entry.Ready;
        var url = WebMail.FolderUrl(entry.Provider.Web!, folderKey) ?? entry.Provider.Web!.HomeUrl;
        if (!string.Equals(entry.View.CoreWebView2?.Source, url, StringComparison.Ordinal)) entry.View.CoreWebView2?.Navigate(url);
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HideAll()
    {
        ActiveAccount = null;
        foreach (var e in _entries.Values) e.View.Visibility = Visibility.Collapsed;
    }

    public void GoBack()
    {
        if (ActiveView?.CoreWebView2 is { CanGoBack: true } core) core.GoBack();
    }

    public void GoForward()
    {
        if (ActiveView?.CoreWebView2 is { CanGoForward: true } core) core.GoForward();
    }

    public void Reload() => ActiveView?.CoreWebView2?.Reload();

    public bool CanGoBack => ActiveView?.CoreWebView2?.CanGoBack == true;
    public bool CanGoForward => ActiveView?.CoreWebView2?.CanGoForward == true;

    /// <summary>Konto entfernt: Profil (Cookies) löschen und Ansicht entfernen.</summary>
    public void Remove(Account account)
    {
        if (_entries.TryGetValue(account.Id, out var entry) && entry.View.CoreWebView2 is { } core)
        {
            try { core.Profile.Delete(); } catch (Exception) { }
        }
        else
        {
            DeleteProfileFolder(account.Id);
        }
        RemoveView(account.Id);
    }

    private void RemoveView(string accountId)
    {
        if (!_entries.Remove(accountId, out var entry)) return;
        _host?.Children.Remove(entry.View);
        try { entry.View.Close(); } catch (Exception) { }
        if (ActiveAccount?.Id == accountId) ActiveAccount = null;
        CountChanged?.Invoke(this, accountId);
    }

    /// <summary>Profilordner direkt löschen (wenn keine Ansicht geöffnet ist).</summary>
    public static void DeleteProfileFolder(string accountId)
    {
        try
        {
            var dir = Path.Combine(UserDataFolder, "EBWebView", ProfileName(accountId));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
