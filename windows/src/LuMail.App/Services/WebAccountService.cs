using LuMail.Core.Catalog;
using LuMail.Core.Models;
using LuMail.Core.Resources;
using Microsoft.UI.Dispatching;
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
        /// <summary>UI-Thread der Ansicht (beim Anlegen festgehalten) – WebView2 nur von dort aufrufen.</summary>
        public required DispatcherQueue Dispatcher { get; init; }
        public Task? Ready { get; set; }
        public int? Unread { get; set; }
        /// <summary>Lu-Ansicht: Aufrufe pro Konto nacheinander (§21).</summary>
        public SemaphoreSlim Queue { get; } = new(1, 1);
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

    /// <summary>
    /// Mitschnitt (§21, <c>shared/webmail/lu-capture.js</c>) ab Dokumentbeginn in jede Seite dieses Browsers einhängen –
    /// direkt nach EnsureCoreWebView2Async und vor dem ersten Navigate. Fehlt er, liest der Leser aus dem Seitenaufbau.
    /// </summary>
    public static async Task InstallCaptureAsync(CoreWebView2 core)
    {
        try { await core.AddScriptToExecuteOnDocumentCreatedAsync(SharedResources.CaptureScript); }
        catch (Exception) { /* Mitschnitt ist optional */ }
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
        var entry = new Entry { Account = account, Provider = provider, View = view, Dispatcher = view.DispatcherQueue };
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
        await InstallCaptureAsync(core);
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

    /// <summary>
    /// Ansicht des Kontos zeigen (alle anderen ausblenden). Mit <paramref name="navigate"/> zum Ordner navigieren;
    /// ohne bleibt die aktuelle Seite (z. B. die in der Lu-Ansicht geöffnete Nachricht).
    /// </summary>
    public async Task ShowAsync(Account account, string folderKey, bool navigate = true)
    {
        var entry = await ActivateAsync(account);
        if (entry is null) return;
        var url = WebMail.FolderUrl(entry.Provider.Web!, folderKey) ?? entry.Provider.Web!.HomeUrl;
        if (navigate && !string.Equals(entry.View.CoreWebView2?.Source, url, StringComparison.Ordinal)) entry.View.CoreWebView2?.Navigate(url);
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ansicht sichtbar machen (auch hinter der Lu-Ansicht – so rendert die Webseite weiter).</summary>
    public async Task ActivateBehindAsync(Account account) => await ActivateAsync(account);

    private async Task<Entry?> ActivateAsync(Account account)
    {
        ActiveAccount = account;
        foreach (var e in _entries.Values) e.View.Visibility = e.Account.Id == account.Id ? Visibility.Visible : Visibility.Collapsed;
        await EnsureAsync(account);
        if (!_entries.TryGetValue(account.Id, out var entry)) return null;
        entry.View.Visibility = Visibility.Visible;
        if (entry.Ready is not null) await entry.Ready;
        return entry.View.CoreWebView2 is null ? null : entry;
    }

    /// <summary>„Neue E-Mail“ im Webmail: zur <c>composeUrl</c> (falls vorhanden).</summary>
    public async Task ComposeAsync(Account account)
    {
        var entry = await ActivateAsync(account);
        if (entry?.Provider.Web?.ComposeUrl is { Length: > 0 } url) entry.View.CoreWebView2.Navigate(url);
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------- Lu-Ansicht (§21)

    /// <summary>Hat der Anbieter einen Web-Leser?</summary>
    public bool HasReader(Account account) => _s.Providers.Find(account.ProviderId)?.Web?.Reader is { Length: > 0 };

    /// <summary>
    /// Skript im Webmail ausführen. Immer auf dem UI-Thread der Ansicht: kommt der Aufruf von woanders (z. B. nach
    /// einem ConfigureAwait(false)), wird er dorthin umgeleitet statt mit RPC_E_WRONG_THREAD still zu scheitern.
    /// </summary>
    private static Task<string?> ExecuteAsync(Entry entry, string script)
    {
        if (entry.Dispatcher.HasThreadAccess) return ExecuteOnUiThreadAsync(entry, script);
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!entry.Dispatcher.TryEnqueue(async () => done.TrySetResult(await ExecuteOnUiThreadAsync(entry, script))))
            done.TrySetResult(null); // Warteschlange beendet (App schließt)
        return done.Task;
    }

    private static async Task<string?> ExecuteOnUiThreadAsync(Entry entry, string script)
    {
        try
        {
            return entry.View.CoreWebView2 is { } core ? await core.ExecuteScriptAsync(script) : null;
        }
        catch (Exception)
        {
            return null; // Seite lädt gerade neu o. Ä. – die Abfrage wiederholt sich
        }
    }

    /// <summary>
    /// Ordner im Hintergrund-Browser öffnen und die Liste lesen: steht er schon in einer Nachricht des Ordners,
    /// genügt history.back(), sonst Navigation; danach Abfrage nach §21.
    /// </summary>
    public async Task<(WebReaderOutcome Outcome, IReadOnlyList<WebReaderItem> Items)> LoadListAsync(Account account, string folderKey, CancellationToken ct = default)
    {
        await EnsureAsync(account);
        if (!_entries.TryGetValue(account.Id, out var entry) || entry.Provider.Web?.Reader is not { Length: > 0 } reader)
            return (WebReaderOutcome.Failed, Array.Empty<WebReaderItem>());
        if (entry.Ready is not null) await entry.Ready;
        if (entry.View.CoreWebView2 is null) return (WebReaderOutcome.Failed, Array.Empty<WebReaderItem>());
        ct.ThrowIfCancellationRequested();

        // Abbruch (anderer Ordner/anderes Konto/Lu-Ansicht verlassen) wirkt in der Warteschlange, beim Navigieren und
        // in der Abfrage-Schleife – als OperationCanceledException, nie als Failed.
        await entry.Queue.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var core = entry.View.CoreWebView2;
            var folderUrl = WebMail.FolderUrl(entry.Provider.Web, folderKey) ?? entry.Provider.Web.HomeUrl;
            switch (WebReader.NavigationFor(core.Source, folderUrl))
            {
                case WebFolderNavigation.HistoryBack:
                    await ExecuteAsync(entry, "history.back()");
                    await Task.Delay(800, ct);
                    break;
                case WebFolderNavigation.Navigate:
                    await NavigateAndWaitAsync(core, folderUrl, ct);
                    break;
            }
            return await WebReader.PollListAsync(script => ExecuteAsync(entry, script), reader, ct: ct);
        }
        finally
        {
            entry.Queue.Release();
        }
    }

    /// <summary>Navigieren und auf den Seitenwechsel warten (auch Hash-Navigation), höchstens 5 s, dann kurz rendern lassen.</summary>
    private static async Task NavigateAndWaitAsync(CoreWebView2 core, string url, CancellationToken ct)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSource(CoreWebView2 s, CoreWebView2SourceChangedEventArgs a) => changed.TrySetResult();
        void OnCompleted(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs a) => changed.TrySetResult();
        core.SourceChanged += OnSource;
        core.NavigationCompleted += OnCompleted;
        try
        {
            core.Navigate(url);
            await Task.WhenAny(changed.Task, Task.Delay(5000, ct));
            ct.ThrowIfCancellationRequested();
            await Task.Delay(800, ct);
        }
        finally
        {
            core.SourceChanged -= OnSource;
            core.NavigationCompleted -= OnCompleted;
        }
    }

    /// <summary>Nachricht im Webmail öffnen und lesen (§21); null, wenn das nicht gelingt.</summary>
    public async Task<WebMessageResult?> LoadMessageAsync(Account account, WebReaderItem item, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(account.Id, out var entry) || entry.Provider.Web?.Reader is not { Length: > 0 } reader) return null;
        if (entry.Ready is not null) await entry.Ready;
        if (entry.View.CoreWebView2 is null) return null;
        ct.ThrowIfCancellationRequested();
        await entry.Queue.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await WebReader.OpenAndPollMessageAsync(script => ExecuteAsync(entry, script), reader, item.Id, ct: ct);
        }
        finally
        {
            entry.Queue.Release();
        }
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

    /// <summary>
    /// §20 „Jetzt umstellen“ (bzw. dieselbe Adresse im Assistenten per IMAP hinzugefügt): das IMAP-Konto ersetzt das
    /// Web-Konto. Profil löschen, solange die Ansicht noch offen ist (sonst bleiben gesperrte Dateien liegen), dann das
    /// Konto entfernen.
    /// </summary>
    public async Task ReplaceWithImapAsync(Account web, Account imap)
    {
        Remove(web);
        _s.Settings.WebViewModes.Remove(web.Id);
        _s.Settings.CollapsedAccounts.Remove(web.Id);
        _s.SaveSettings();
        await _s.Setup.ReplaceWebAccountAsync(web, imap);
    }

    /// <summary>Web-Konto desselben Anbieters mit derselben Adresse (Kandidat für die Ersetzung durch IMAP).</summary>
    public Account? FindWebTwin(Account imap) =>
        imap.IsWeb ? null : _s.Accounts.Load().FirstOrDefault(a => a.IsWeb
            && string.Equals(a.ProviderId, imap.ProviderId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Email, imap.Email, StringComparison.OrdinalIgnoreCase));

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
