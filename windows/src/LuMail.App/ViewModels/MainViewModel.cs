using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LuMail.App.Services;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using LuMail.Core.Storage;
using Microsoft.UI.Xaml;

namespace LuMail.App.ViewModels;

/// <summary>Zustand und Logik des Hauptfensters (Sidebar, Nachrichtenliste, Aktionen, Hintergrundaufgaben).</summary>
public sealed class MainViewModel : ObservableObject
{
    private sealed class AccountState
    {
        public List<MailFolder> Folders { get; set; } = new();
        public List<MessageItem> Inbox { get; set; } = new();
        public Dictionary<string, List<MessageItem>> FolderCache { get; } = new(StringComparer.Ordinal);
        public string Error { get; set; } = "";
        /// <summary>UIDs des letzten Posteingangs (für Mitteilungen über neue Nachrichten).</summary>
        public HashSet<uint>? KnownInboxUids { get; set; }
        public bool Offline { get; set; }
    }

    private const string Primary = "primary";
    private readonly AppServices _s;
    private readonly Action<Action> _runOnUi;
    private readonly Dictionary<string, AccountState> _states = new();
    private readonly Dictionary<string, IdleMonitor> _idle = new();
    private readonly HashSet<string> _refreshingAccounts = new();
    private List<MessageItem> _all = new();
    private List<MessageItem> _serverResults = new();
    /// <summary>Wieder aufgetauchte Erinnerungen: Schlüssel → Zeitpunkt (werden oben einsortiert).</summary>
    private readonly Dictionary<string, DateTimeOffset> _resurfaced = new();
    private int _loadVersion;
    private int _busy;
    private bool _refreshing;

    public MainViewModel(AppServices services, Action<Action> runOnUi)
    {
        _s = services;
        _runOnUi = runOnUi;
    }

    public ObservableCollection<SidebarItem> Sidebar { get; } = new();
    public ObservableCollection<MessageItem> Messages { get; } = new();
    public List<Account> Accounts { get; private set; } = new();
    /// <summary>Konten mit IMAP/SMTP (ohne Web-Konten) – nur diese betreffen Abruf, Push, Suche, Regeln, Verfassen.</summary>
    public IReadOnlyList<Account> ImapAccounts => Accounts.Where(a => !a.IsWeb).ToList();
    /// <summary>Web-Konten (eingebettetes Webmail, FUNKTIONEN §20).</summary>
    public IReadOnlyList<Account> WebAccounts => Accounts.Where(a => a.IsWeb).ToList();
    /// <summary>Ungelesen-Zahl eines Web-Kontos (aus dem Seitentitel).</summary>
    public Func<string, int?>? WebUnread { get; set; }
    /// <summary>Auswahl in der Seitenleiste hat sich geändert.</summary>
    public event EventHandler? SelectedChanged;
    public SidebarItem? Selected { get; private set; }

    /// <summary>Nach dem Neubefüllen von <see cref="Messages"/> (Auswahl wiederherstellen).</summary>
    public event EventHandler? MessagesReset;
    /// <summary>Fehlermeldungen für die Anzeige.</summary>
    public event EventHandler<string>? ErrorOccurred;
    /// <summary>Hinweise (Info) für die Anzeige.</summary>
    public event EventHandler<string>? InfoOccurred;
    /// <summary>Neue ungelesene Nachrichten (Posteingang/Updates) seit dem letzten Laden → Systemmitteilung.</summary>
    public event EventHandler<IReadOnlyList<MessageItem>>? NewMessagesArrived;
    /// <summary>Fällige Erinnerung.</summary>
    public event EventHandler<MessageItem>? ReminderDue;
    /// <summary>Ungelesen-Zähler (Kategorie Posteingang aller Konten) hat sich evtl. geändert.</summary>
    public event EventHandler? CountersChanged;

    public bool IsResetting { get; private set; }
    public bool IsBusy => _busy > 0;
    public bool HasAccounts => Accounts.Count > 0;
    public Visibility WelcomeVisibility => HasAccounts ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>True, wenn mindestens ein Konto zuletzt nicht erreichbar war (Anzeige aus dem Cache).</summary>
    public bool IsOffline => _states.Values.Any(s => s.Offline);

    private string _listTitle = "Alle Posteingänge";
    public string ListTitle { get => _listTitle; private set => SetProperty(ref _listTitle, value); }

    private string _listSubtitle = "";
    public string ListSubtitle { get => _listSubtitle; private set => SetProperty(ref _listSubtitle, value); }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            _serverResults.Clear();
            ApplyFilter();
        }
    }

    public bool ConversationsEnabled
    {
        get => _s.Settings.Conversations;
        set
        {
            if (_s.Settings.Conversations == value) return;
            _s.Settings.Conversations = value;
            _s.SaveSettings();
            OnPropertyChanged();
            SetList(_all.Where(m => !m.IsHeader).SelectMany(m => m.ThreadItems ?? new[] { m }).Distinct().ToList());
        }
    }

    private void Busy(bool on)
    {
        _busy = Math.Max(0, _busy + (on ? 1 : -1));
        OnPropertyChanged(nameof(IsBusy));
    }

    private AccountState State(string accountId)
    {
        if (!_states.TryGetValue(accountId, out var state)) _states[accountId] = state = new AccountState();
        return state;
    }

    private IEnumerable<MessageItem> AllInboxItems => _states.Values.SelectMany(s => s.Inbox);

    private bool IsSnoozed(MessageItem m) => _s.Snoozes.IsSnoozed(m.Account.Id, m.Summary.FolderPath, m.Summary.Uid);

    /// <summary>Sichtbare Posteingangsnachrichten (ohne zurückgestellte).</summary>
    private IEnumerable<MessageItem> VisibleInbox(AccountState state) => state.Inbox.Where(m => !IsSnoozed(m));

    // =============================================================== Laden

    public void LoadAccounts()
    {
        Accounts = _s.Accounts.Load();
        foreach (var id in _states.Keys.Except(Accounts.Select(a => a.Id)).ToList()) _states.Remove(id);
        foreach (var id in _idle.Keys.Except(Accounts.Select(a => a.Id)).ToList())
        {
            _ = _idle[id].DisposeAsync().AsTask();
            _idle.Remove(id);
        }
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(WelcomeVisibility));
    }

    /// <summary>§3: Beim Start sofort aus dem Offline-Cache anzeigen.</summary>
    public async Task LoadFromCacheAsync()
    {
        LoadAccounts();
        if (_s.Settings.OfflineCache)
        {
            foreach (var account in ImapAccounts)
            {
                var provider = _s.Providers.Find(account.ProviderId);
                if (provider is null) continue;
                var snapshot = _s.Cache.LoadSnapshot(account.Id);
                if (snapshot is null) continue;
                var state = State(account.Id);
                state.Folders = snapshot.Folders;
                state.Inbox = snapshot.Messages.TryGetValue("INBOX", out var inbox)
                    ? inbox.Select(m => new MessageItem(m, account, provider)).ToList()
                    : new List<MessageItem>();
                foreach (var (path, list) in snapshot.Messages.Where(p => p.Key != "INBOX"))
                    state.FolderCache[path] = list.Select(m => new MessageItem(m, account, provider)).ToList();
            }
        }
        RebuildSidebar();
        await ShowSelectedAsync(reload: false);
        CountersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lädt Ordner und Posteingang aller Konten neu (F5, Timer, Start).</summary>
    public async Task RefreshAllAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        Busy(true);
        try
        {
            LoadAccounts();
            RebuildSidebar();
            var errors = new List<string>();
            await Task.WhenAll(ImapAccounts.Select(async account =>
            {
                await RefreshAccountAsync(account, foldersToo: true);
                var state = State(account.Id);
                if (!string.IsNullOrEmpty(state.Error) && !state.Offline) errors.Add($"{account.Email}: {state.Error}");
            }));
            if (ImapAccounts.Count == 0) await ShowSelectedAsync(reload: false);
            if (errors.Count > 0) ErrorOccurred?.Invoke(this, string.Join("\n", errors));
            StartIdleMonitors();
            await ProcessDueAsync();
        }
        finally
        {
            Busy(false);
            _refreshing = false;
        }
    }

    /// <summary>Ein Konto neu laden (auch nach IDLE-Meldung, dann nur der Posteingang).</summary>
    private async Task RefreshAccountAsync(Account account, bool foldersToo)
    {
        if (!_refreshingAccounts.Add(account.Id)) return;
        var state = State(account.Id);
        var provider = _s.Providers.Find(account.ProviderId);
        try
        {
            if (provider is null)
            {
                state.Error = $"Unbekannter Anbieter „{account.ProviderId}“.";
                return;
            }
            try
            {
                if (foldersToo || state.Folders.Count == 0) state.Folders = (await _s.Mail.ListFoldersAsync(account)).ToList();
                var inbox = await _s.Mail.LoadInboxAsync(account);
                var items = inbox.Select(m => new MessageItem(m, account, provider)).ToList();

                // §2: neue, ungelesene Nachrichten in Posteingang/Updates (nicht beim ersten Laden nach dem Start).
                if (state.KnownInboxUids is not null && _s.Settings.Notifications)
                {
                    var fresh = items.Where(m => !state.KnownInboxUids.Contains(m.Summary.Uid) && !m.Seen
                                                 && m.CategoryId is Primary or "updates" && !IsSnoozed(m)).ToList();
                    if (fresh.Count > 0) NewMessagesArrived?.Invoke(this, fresh);
                }
                state.KnownInboxUids = items.Select(m => m.Summary.Uid).ToHashSet();
                state.Inbox = items;
                if (foldersToo) state.FolderCache.Clear();
                state.Error = "";
                state.Offline = false;

                ObserveContacts(items);
                SaveSnapshot(account, state);
                await CleanupAsync(account, state);
            }
            catch (Exception ex)
            {
                state.Error = ex.Message;
                state.Offline = ex is MailServiceException { InnerException: System.Net.Sockets.SocketException or IOException or TimeoutException }
                                || ex.InnerException is System.Net.Sockets.SocketException;
            }

            RebuildSidebar();
            if (Selected?.Target != SidebarTarget.Folder || Selected.Account?.Id == account.Id)
                await ShowSelectedAsync(reload: Selected?.Target == SidebarTarget.Folder && foldersToo);
            OnPropertyChanged(nameof(IsOffline));
            CountersChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshingAccounts.Remove(account.Id);
        }
    }

    private void SaveSnapshot(Account account, AccountState state)
    {
        if (!_s.Settings.OfflineCache) return;
        try
        {
            var snapshot = new AccountSnapshot { SavedAt = DateTimeOffset.Now, Folders = state.Folders };
            snapshot.Messages["INBOX"] = state.Inbox.Select(m => m.Summary).ToList();
            foreach (var (path, list) in state.FolderCache) snapshot.Messages[path] = list.Select(m => m.Summary).ToList();
            _s.Cache.SaveSnapshot(account.Id, snapshot);
        }
        catch (IOException)
        {
        }
    }

    private void ObserveContacts(IEnumerable<MessageItem> items)
    {
        var own = Accounts.Select(a => a.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in items)
        {
            if (!own.Contains(m.Summary.FromAddress)) _s.Contacts.Observe(m.Summary.FromAddress, m.Summary.FromName, m.Summary.Date);
            foreach (var r in m.Summary.Recipients)
                if (!own.Contains(r.Address)) _s.Contacts.Observe(r.Address, r.Name, m.Summary.Date);
        }
        try { _s.Contacts.Save(); } catch (IOException) { }
    }

    /// <summary>§12: Werbung &amp; Co. nach Frist archivieren (ohne Archiv: Papierkorb), max. 100 pro Durchlauf.</summary>
    private async Task CleanupAsync(Account account, AccountState state)
    {
        var days = _s.Settings.CleanupDays.Where(d => d.Value > 0).ToDictionary(d => d.Key, d => d.Value);
        if (days.Count == 0) return;
        var selected = CleanupSelector.Select(state.Inbox.Select(m => m.Summary), days, DateTimeOffset.Now);
        if (selected.Count == 0) return;
        var target = state.Folders.Any(f => f.Role == FolderRole.Archive) ? FolderRole.Archive : FolderRole.Trash;
        foreach (var summary in selected)
        {
            try
            {
                await _s.Mail.MoveToRoleAsync(account, summary.FolderPath, summary.Uid, target);
                state.Inbox.RemoveAll(m => m.Summary.Uid == summary.Uid);
            }
            catch (Exception)
            {
                break; // beim nächsten Durchlauf erneut
            }
        }
    }

    // ================================================================ IDLE

    /// <summary>§1: pro Konto eine zweite Verbindung mit IMAP IDLE.</summary>
    private void StartIdleMonitors()
    {
        foreach (var account in ImapAccounts)
        {
            if (_idle.ContainsKey(account.Id)) continue;
            var provider = _s.Providers.Find(account.ProviderId);
            if (provider is null) continue;
            var monitor = new IdleMonitor(account, provider, new StoredCredentialSource(_s.Secrets, _s.Tokens));
            monitor.InboxChanged += (_, accountId) => _runOnUi(() =>
            {
                var acc = Accounts.FirstOrDefault(a => a.Id == accountId);
                if (acc is not null) _ = RefreshAccountAsync(acc, foldersToo: false);
            });
            _idle[account.Id] = monitor;
            monitor.Start();
        }
    }

    public async Task StopBackgroundAsync()
    {
        foreach (var m in _idle.Values) await m.DisposeAsync();
        _idle.Clear();
    }

    // ============================================================= Sidebar

    private static string CategoryGlyph(string id) => id switch
    {
        "primary" => "",
        "promotions" => "",
        "social" => "",
        "updates" => "",
        "forums" => "",
        _ => "",
    };

    private static string RoleGlyph(FolderRole role) => role switch
    {
        FolderRole.Sent => "",
        FolderRole.Drafts => "",
        FolderRole.Spam => "",
        FolderRole.Trash => "",
        FolderRole.Archive => "",
        _ => "",
    };

    /// <summary>Alle Kategorie-Ids der Sidebar: feste, danach eigene.</summary>
    private IEnumerable<string> CategoryIds =>
        MessageCategories.All.Select(c => c.ToId()).Concat(_s.Rules.CustomCategories.Select(c => c.Id));

    public void RebuildSidebar(bool force = false)
    {
        var separate = _s.Settings.SeparateCategories;
        var items = new List<SidebarItem>
        {
            new() { Kind = SidebarItemKind.Section, Title = "Übersicht", Key = "section:overview" },
            new() { Kind = SidebarItemKind.Folder, Target = SidebarTarget.AllInboxes, Title = "Alle Posteingänge", ListTitle = "Alle Posteingänge", Glyph = "", Key = "all" },
            new() { Kind = SidebarItemKind.Folder, Target = SidebarTarget.Unread, Title = "Ungelesen", ListTitle = "Ungelesen", Glyph = "", Key = "unread" },
            new() { Kind = SidebarItemKind.Folder, Target = SidebarTarget.Flagged, Title = "Markiert", ListTitle = "Markiert", Glyph = "", Key = "flagged" },
            new() { Kind = SidebarItemKind.Folder, Target = SidebarTarget.Reminders, Title = "Erinnerungen", ListTitle = "Erinnerungen", Glyph = "", Key = "reminders" },
            new() { Kind = SidebarItemKind.Folder, Target = SidebarTarget.Scheduled, Title = "Geplant", ListTitle = "Geplant", Glyph = "", Key = "scheduled" },
        };

        foreach (var provider in _s.Providers.Providers)
        {
            var accounts = Accounts.Where(a => string.Equals(a.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (accounts.Count == 0) continue;

            // §9: mehrere (IMAP-)Konten → Überschrift klickbar („Alle <Anbieter>-Posteingänge“).
            items.Add(new SidebarItem
            {
                Kind = SidebarItemKind.Provider, Title = provider.Name, Provider = provider,
                Target = accounts.Count(a => !a.IsWeb) > 1 ? SidebarTarget.ProviderInbox : SidebarTarget.None,
                ListTitle = $"Alle {provider.Name}-Posteingänge",
                DotBrush = BrushCache.ForProvider(provider), Key = "provider:" + provider.Id,
            });

            foreach (var account in accounts)
            {
                var state = State(account.Id);
                items.Add(new SidebarItem
                {
                    Kind = SidebarItemKind.Account, Title = account.Email, Subtitle = account.DisplayName,
                    Account = account, Provider = provider, Key = "account:" + account.Id,
                    Indent = new Thickness(8, 0, 0, 0), Error = state.Error,
                });

                if (account.IsWeb)
                {
                    // §20: Ordner des Web-Kontos aus providers.json → web.folders
                    if (provider.Web is not null)
                    {
                        foreach (var entry in LuMail.Core.Catalog.WebMail.SidebarEntries(provider.Web, separate))
                        {
                            items.Add(new SidebarItem
                            {
                                Kind = SidebarItemKind.Folder, Target = SidebarTarget.Web, Title = entry.Name,
                                ListTitle = $"{entry.Name} · {account.Email}",
                                Glyph = entry.IsCategory ? CategoryGlyph(entry.Key) : RoleGlyph(WebRole(entry.Key)),
                                Account = account, Provider = provider, FolderPath = entry.Key,
                                Key = $"web:{account.Id}:{entry.Key}", Indent = new Thickness(20, 0, 0, 0),
                            });
                        }
                    }
                    continue;
                }

                if (separate)
                {
                    foreach (var id in CategoryIds)
                    {
                        var name = _s.CategoryName(id);
                        items.Add(new SidebarItem
                        {
                            Kind = SidebarItemKind.Folder, Target = SidebarTarget.Category, CategoryId = id,
                            Title = name, ListTitle = $"{name} · {account.Email}", Glyph = CategoryGlyph(id),
                            DotBrush = id.StartsWith("custom-", StringComparison.Ordinal) ? BrushCache.ForCategory(id) : null,
                            Account = account, Provider = provider, FolderPath = "INBOX", Role = FolderRole.Inbox,
                            Key = $"cat:{account.Id}:{id}", Indent = new Thickness(20, 0, 0, 0),
                        });
                    }
                }
                else
                {
                    items.Add(new SidebarItem
                    {
                        Kind = SidebarItemKind.Folder, Target = SidebarTarget.Category, CategoryId = null,
                        Title = "Posteingang", ListTitle = $"Posteingang · {account.Email}", Glyph = "",
                        Account = account, Provider = provider, FolderPath = "INBOX", Role = FolderRole.Inbox,
                        Key = $"cat:{account.Id}:inbox", Indent = new Thickness(20, 0, 0, 0),
                    });
                }

                foreach (var folder in state.Folders.Where(f => f.Role != FolderRole.Inbox))
                {
                    items.Add(new SidebarItem
                    {
                        Kind = SidebarItemKind.Folder, Target = SidebarTarget.Folder, Title = folder.DisplayName,
                        ListTitle = $"{folder.DisplayName} · {account.Email}",
                        Glyph = RoleGlyph(folder.Role), Account = account, Provider = provider,
                        FolderPath = folder.Path, Role = folder.Role,
                        Key = $"folder:{account.Id}:{folder.Path}", Indent = new Thickness(20, 0, 0, 0),
                    });
                }
            }
        }

        var selectedKey = Selected?.Key ?? "all";
        if (!force && Sidebar.Select(i => i.Key).SequenceEqual(items.Select(i => i.Key)))
        {
            for (var i = 0; i < items.Count; i++) Sidebar[i].Error = items[i].Error;
        }
        else
        {
            Sidebar.Clear();
            foreach (var item in items) Sidebar.Add(item);
        }

        var selected = Sidebar.FirstOrDefault(i => i.Key == selectedKey && i.IsSelectable) ?? Sidebar.First(i => i.Key == "all");
        foreach (var item in Sidebar) item.IsSelected = ReferenceEquals(item, selected);
        var changed = Selected?.Key != selected.Key;
        Selected = selected;
        RecountSidebar();
        if (changed) SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    private static FolderRole WebRole(string key) => key switch
    {
        "sent" => FolderRole.Sent,
        "drafts" => FolderRole.Drafts,
        "archive" => FolderRole.Archive,
        "spam" => FolderRole.Spam,
        "trash" => FolderRole.Trash,
        _ => FolderRole.None,
    };

    public void RecountSidebar()
    {
        var separate = _s.Settings.SeparateCategories;
        foreach (var item in Sidebar.Where(i => i.IsSelectable))
        {
            item.Unread = item.Target switch
            {
                SidebarTarget.AllInboxes => _states.Values.SelectMany(VisibleInbox).Count(m => !m.Seen && (!separate || m.CategoryId == Primary)),
                SidebarTarget.Unread => _states.Values.SelectMany(VisibleInbox).Count(m => !m.Seen),
                SidebarTarget.Flagged => AllInboxItems.Count(m => m.Flagged && !m.Seen),
                SidebarTarget.Reminders => _s.Snoozes.All.Count,
                SidebarTarget.Scheduled => _s.Outbox.All().Count,
                SidebarTarget.Web when item.Account is not null && item.FolderPath == "primary" => WebUnread?.Invoke(item.Account.Id) ?? 0,
                SidebarTarget.ProviderInbox when item.Provider is not null => ImapAccounts.Where(a => a.ProviderId == item.Provider.Id)
                    .SelectMany(a => VisibleInbox(State(a.Id))).Count(m => !m.Seen && (!separate || m.CategoryId == Primary)),
                SidebarTarget.Category when item.Account is not null => VisibleInbox(State(item.Account.Id))
                    .Count(m => !m.Seen && (item.CategoryId is null || m.CategoryId == item.CategoryId)),
                SidebarTarget.Folder when item.Account is not null =>
                    State(item.Account.Id).Folders.FirstOrDefault(f => f.Path == item.FolderPath)?.Unread ?? 0,
                _ => 0,
            };
        }
        // Web-Konten: Zahl auch am Konto (§20)
        foreach (var item in Sidebar.Where(i => i.Kind == SidebarItemKind.Account && i.Account is { IsWeb: true }))
            item.Unread = WebUnread?.Invoke(item.Account!.Id) ?? 0;
    }

    /// <summary>§2: Ungelesene der Kategorie Posteingang über alle Konten.</summary>
    public int PrimaryUnreadCount => _states.Values.SelectMany(VisibleInbox).Count(m => !m.Seen && (m.CategoryId ?? Primary) == Primary);

    /// <summary>Die 5 neuesten ungelesenen Mails (Kategorie Posteingang) für das Menü im Infobereich.</summary>
    public IReadOnlyList<MessageItem> NewestUnread(int count = 5) =>
        _states.Values.SelectMany(VisibleInbox).Where(m => !m.Seen && (m.CategoryId ?? Primary) == Primary)
            .OrderByDescending(m => m.Summary.Date).Take(count).ToList();

    // ==================================================== Nachrichtenliste

    public async Task SelectAsync(SidebarItem item)
    {
        if (!item.IsSelectable) return;
        if (Selected is not null) Selected.IsSelected = false;
        Selected = item;
        item.IsSelected = true;
        _serverResults.Clear();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
        await ShowSelectedAsync(reload: false);
    }

    public SidebarItem? FindSidebar(string key) => Sidebar.FirstOrDefault(i => i.Key == key);

    private async Task ShowSelectedAsync(bool reload)
    {
        var item = Selected;
        var version = ++_loadVersion;
        if (item is null)
        {
            SetList(new List<MessageItem>());
            return;
        }

        ListTitle = string.IsNullOrEmpty(item.ListTitle) ? item.Title : item.ListTitle;
        var separate = _s.Settings.SeparateCategories;
        List<MessageItem> list;
        switch (item.Target)
        {
            case SidebarTarget.AllInboxes:
                list = _states.Values.SelectMany(VisibleInbox).Where(m => !separate || m.CategoryId == Primary).ToList();
                break;
            case SidebarTarget.Unread:
                list = _states.Values.SelectMany(VisibleInbox).Where(m => !m.Seen).ToList();
                break;
            case SidebarTarget.Flagged:
                list = AllInboxItems.Where(m => m.Flagged).ToList();
                break;
            case SidebarTarget.Reminders:
                list = ReminderItems();
                break;
            case SidebarTarget.Scheduled:
                list = ScheduledItems();
                break;
            case SidebarTarget.ProviderInbox when item.Provider is not null:
                list = ImapAccounts.Where(a => a.ProviderId == item.Provider.Id).SelectMany(a => VisibleInbox(State(a.Id)))
                    .Where(m => !separate || m.CategoryId == Primary).ToList();
                break;
            case SidebarTarget.Category when item.Account is not null:
                list = VisibleInbox(State(item.Account.Id)).Where(m => item.CategoryId is null || m.CategoryId == item.CategoryId).ToList();
                break;
            case SidebarTarget.Folder when item.Account is not null && item.FolderPath is not null:
                var state = State(item.Account.Id);
                if (!reload && state.FolderCache.TryGetValue(item.FolderPath, out var cached))
                {
                    list = cached;
                    if (state.Offline || cached.Count > 0) break;
                }
                SetList(state.FolderCache.TryGetValue(item.FolderPath, out var stale) ? stale : new List<MessageItem>());
                Busy(true);
                try
                {
                    var provider = _s.Providers.Get(item.Account.ProviderId);
                    var summaries = await _s.Mail.LoadFolderAsync(item.Account, item.FolderPath);
                    list = summaries.Select(m => new MessageItem(m, item.Account, provider)).ToList();
                    state.FolderCache[item.FolderPath] = list;
                    SaveSnapshot(item.Account, state);
                    ObserveContacts(list);
                }
                catch (Exception ex)
                {
                    if (version == _loadVersion && stale is null) ErrorOccurred?.Invoke(this, ex.Message);
                    return;
                }
                finally
                {
                    Busy(false);
                }
                break;
            default:
                list = new List<MessageItem>();
                break;
        }

        if (version != _loadVersion) return;
        SetList(list);
    }

    private List<MessageItem> ReminderItems()
    {
        var result = new List<MessageItem>();
        foreach (var entry in _s.Snoozes.All)
        {
            var account = Accounts.FirstOrDefault(a => a.Id == entry.AccountId);
            var provider = account is null ? null : _s.Providers.Find(account.ProviderId);
            if (account is null || provider is null) continue;
            var state = State(account.Id);
            var known = state.Inbox.Concat(state.FolderCache.Values.SelectMany(l => l))
                .FirstOrDefault(m => m.Summary.FolderPath == entry.Folder && m.Summary.Uid == entry.Uid);
            var item = known ?? new MessageItem(new MessageSummary
            {
                AccountId = account.Id, FolderPath = entry.Folder, Uid = entry.Uid, Subject = "(Nachricht)", MessageId = entry.MessageId,
                Seen = true, Date = entry.Until,
            }, account, provider);
            item.Snooze = entry;
            result.Add(item);
        }
        return result;
    }

    private List<MessageItem> ScheduledItems()
    {
        var result = new List<MessageItem>();
        foreach (var entry in _s.Outbox.All())
        {
            var account = Accounts.FirstOrDefault(a => a.Id == entry.AccountId);
            var provider = account is null ? null : _s.Providers.Find(account.ProviderId);
            if (account is null || provider is null) continue;
            result.Add(new MessageItem(new MessageSummary
            {
                AccountId = account.Id, FolderPath = "outbox:" + entry.Id, Subject = entry.Subject, FromAddress = account.Email,
                Date = entry.SendAt, Seen = true, Preview = "",
            }, account, provider) { Outbox = entry });
        }
        return result;
    }

    private void SetList(List<MessageItem> list)
    {
        var flat = list.Where(m => !m.IsHeader).OrderByDescending(SortDate).ToList();
        _all = _s.Settings.Conversations && Selected?.Target is not (SidebarTarget.Reminders or SidebarTarget.Scheduled)
            ? GroupConversations(flat)
            : flat;
        ApplyFilter();
    }

    /// <summary>Sortierdatum: Erinnerungen erscheinen nach dem Wiederauftauchen oben.</summary>
    private DateTimeOffset SortDate(MessageItem m) =>
        _resurfaced.TryGetValue(m.Key, out var at) && at > m.Summary.Date ? at : m.Summary.Date;

    /// <summary>§8: Konversationen – Eintrag = neueste Nachricht, mit Anzahl und Teilnehmern.</summary>
    private static List<MessageItem> GroupConversations(List<MessageItem> flat)
    {
        var byKey = flat.ToDictionary(m => m.Key);
        var result = new List<MessageItem>();
        foreach (var conversation in ConversationBuilder.Group(flat.Select(m => m.Summary)))
        {
            var latest = byKey[conversation.Latest.Key];
            if (conversation.Count == 1)
            {
                result.Add(latest);
                continue;
            }
            var members = conversation.Messages.Select(s => byKey[s.Key]).ToList();
            result.Add(new MessageItem(latest.Summary, latest.Account, latest.Provider)
            {
                Thread = conversation.Messages,
                ThreadItems = members,
            });
        }
        return result;
    }

    public void ApplyFilter()
    {
        IsResetting = true;
        try
        {
            Messages.Clear();
            foreach (var m in _all.Where(m => m.Matches(_searchText) || (m.ThreadItems?.Any(t => t.Matches(_searchText)) ?? false)))
                Messages.Add(m);
            if (_serverResults.Count > 0)
            {
                var header = MessageItem.Header("Weitere Treffer auf dem Server", _serverResults[0].Account, _serverResults[0].Provider);
                Messages.Add(header);
                foreach (var m in _serverResults) Messages.Add(m);
            }
        }
        finally
        {
            IsResetting = false;
        }
        var count = Messages.Count(m => !m.IsHeader);
        var unread = Messages.Count(m => !m.IsHeader && !m.Seen);
        ListSubtitle = count == 0
            ? "Keine Nachrichten"
            : $"{count} {(_s.Settings.Conversations ? "Einträge" : "Nachrichten")}" + (unread > 0 ? $", {unread} ungelesen" : "");
        MessagesReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>§4: Serversuche im aktuellen Bereich (Konto-Ordner bzw. INBOX aller Konten in der Übersicht).</summary>
    public async Task ServerSearchAsync()
    {
        var text = _searchText.Trim();
        if (text.Length == 0 || Selected is null) return;
        var targets = new List<(Account Account, string Folder)>();
        switch (Selected.Target)
        {
            case SidebarTarget.Folder when Selected.Account is not null && Selected.FolderPath is not null:
                targets.Add((Selected.Account, Selected.FolderPath));
                break;
            case SidebarTarget.Category when Selected.Account is not null:
                targets.Add((Selected.Account, "INBOX"));
                break;
            case SidebarTarget.ProviderInbox when Selected.Provider is not null:
                targets.AddRange(ImapAccounts.Where(a => a.ProviderId == Selected.Provider.Id).Select(a => (a, "INBOX")));
                break;
            case SidebarTarget.Web:
                return; // Web-Konten durchsucht man im Webmail selbst
            default:
                targets.AddRange(ImapAccounts.Select(a => (a, "INBOX")));
                break;
        }

        Busy(true);
        try
        {
            var known = _all.SelectMany(m => m.ThreadItems ?? new[] { m }).Select(m => m.Key).ToHashSet();
            var results = new List<MessageItem>();
            var errors = new List<string>();
            await Task.WhenAll(targets.Select(async t =>
            {
                try
                {
                    var provider = _s.Providers.Get(t.Account.ProviderId);
                    var hits = await _s.Mail.SearchAsync(t.Account, t.Folder, text);
                    results.AddRange(hits.Where(h => !known.Contains(h.Key)).Select(h => new MessageItem(h, t.Account, provider)));
                }
                catch (Exception ex)
                {
                    errors.Add($"{t.Account.Email}: {ex.Message}");
                }
            }));
            if (text != _searchText.Trim()) return;
            _serverResults = results.OrderByDescending(m => m.Summary.Date).ToList();
            ApplyFilter();
            if (errors.Count > 0) ErrorOccurred?.Invoke(this, "Serversuche: " + string.Join("\n", errors));
            else if (_serverResults.Count == 0) InfoOccurred?.Invoke(this, "Auf dem Server wurden keine weiteren Treffer gefunden.");
        }
        finally
        {
            Busy(false);
        }
    }

    // ============================================================ Aktionen

    /// <summary>Alle Einzelnachrichten hinter Listeneinträgen (Konversationen aufgelöst).</summary>
    public static IReadOnlyList<MessageItem> Expand(IEnumerable<MessageItem> items) =>
        items.Where(i => !i.IsHeader && i.Outbox is null).SelectMany(i => i.ThreadItems ?? new[] { i }).Distinct().ToList();

    public async Task<MessageDetail> OpenAsync(MessageItem item)
    {
        var detail = await _s.Mail.FetchMessageAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid, markSeen: true);
        if (!item.Seen) ApplySeen(item, true);
        return detail;
    }

    /// <summary>Konversation: alle Nachrichten laden (älteste zuerst) und als gelesen markieren.</summary>
    public async Task<IReadOnlyList<MessageDetail>> OpenThreadAsync(MessageItem item)
    {
        var details = new List<MessageDetail>();
        foreach (var member in item.ThreadItems ?? new[] { item })
            details.Add(await OpenAsync(member));
        item.Seen = true;
        return details;
    }

    public async Task SetSeenAsync(IEnumerable<MessageItem> items, bool seen)
    {
        foreach (var item in Expand(items))
        {
            await _s.Mail.SetSeenAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid, seen);
            ApplySeen(item, seen);
        }
        foreach (var conv in items.Where(i => i.ThreadItems is not null)) conv.Seen = seen;
        CountersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySeen(MessageItem item, bool seen)
    {
        if (item.Seen == seen) return;
        item.Seen = seen;
        var folder = State(item.Account.Id).Folders.FirstOrDefault(f => f.Path == item.Summary.FolderPath);
        if (folder is not null) folder.Unread = Math.Max(0, folder.Unread + (seen ? -1 : 1));
        RecountSidebar();
        CountersChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetFlaggedAsync(IEnumerable<MessageItem> items, bool flagged)
    {
        foreach (var item in Expand(items))
        {
            await _s.Mail.SetFlaggedAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid, flagged);
            item.Flagged = flagged;
        }
        foreach (var conv in items.Where(i => i.ThreadItems is not null)) conv.Flagged = flagged;
        RecountSidebar();
    }

    public FolderRole RoleOf(MessageItem item) => _s.Mail.RoleOf(item.Account, item.Summary.FolderPath);

    public async Task MoveAsync(IEnumerable<MessageItem> items, FolderRole target)
    {
        var list = items.ToList();
        foreach (var item in Expand(list))
        {
            await _s.Mail.MoveToRoleAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid, target);
            RemoveLocal(item);
            var state = State(item.Account.Id);
            var targetPath = target == FolderRole.Inbox ? "INBOX" : state.Folders.FirstOrDefault(f => f.Role == target)?.Path;
            if (targetPath is not null) state.FolderCache.Remove(targetPath);
        }
        foreach (var conv in list) RemoveFromList(conv);
        if (target == FolderRole.Inbox) await RefreshAllAsync();
    }

    public async Task DeleteAsync(IEnumerable<MessageItem> items)
    {
        var list = items.ToList();
        foreach (var item in Expand(list))
        {
            await _s.Mail.DeleteAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid);
            RemoveLocal(item);
            var state = State(item.Account.Id);
            var trash = state.Folders.FirstOrDefault(f => f.Role == FolderRole.Trash)?.Path;
            if (trash is not null) state.FolderCache.Remove(trash);
        }
        foreach (var conv in list) RemoveFromList(conv);
    }

    private void RemoveLocal(MessageItem item)
    {
        var state = State(item.Account.Id);
        state.Inbox.Remove(item);
        foreach (var l in state.FolderCache.Values) l.Remove(item);
        if (!item.Seen)
        {
            var folder = state.Folders.FirstOrDefault(f => f.Path == item.Summary.FolderPath);
            if (folder is not null) folder.Unread = Math.Max(0, folder.Unread - 1);
        }
        RemoveFromList(item);
        RecountSidebar();
        CountersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveFromList(MessageItem item)
    {
        _all.Remove(item);
        _serverResults.Remove(item);
        Messages.Remove(item);
    }

    /// <summary>„Absender immer als … einsortieren“ (senders in rules.json).</summary>
    public void SetSenderRule(MessageItem item, string categoryId)
    {
        if (string.IsNullOrWhiteSpace(item.Summary.FromAddress)) return;
        _s.Rules.SetSender(item.Summary.FromAddress, categoryId);
        ApplyRulesLocally();
    }

    /// <summary>Nach Regeländerungen: Absender-Regeln sofort anwenden (andere Regeln beim nächsten Laden).</summary>
    public void ApplyRulesLocally()
    {
        foreach (var m in AllInboxItems)
            if (_s.Rules.LookupSender(m.Summary.FromAddress) is { } c) m.CategoryId = c;
        RecountSidebar();
        if (Selected?.Target is SidebarTarget.Category or SidebarTarget.AllInboxes or SidebarTarget.ProviderInbox) _ = ShowSelectedAsync(reload: false);
    }

    public bool HasSenderRule(MessageItem item) => _s.Rules.Senders.ContainsKey(item.Summary.FromAddress.Trim().ToLowerInvariant());

    public async Task RemoveSenderRuleAsync(MessageItem item)
    {
        if (_s.Rules.RemoveSender(item.Summary.FromAddress)) await RefreshAllAsync();
    }

    // ========================================================= Erinnern

    /// <summary>§13: Nachricht lokal ausblenden bis <paramref name="until"/>.</summary>
    public void Snooze(IEnumerable<MessageItem> items, DateTimeOffset until)
    {
        foreach (var item in Expand(items))
        {
            _s.Snoozes.Add(new SnoozeEntry
            {
                AccountId = item.Account.Id, Folder = item.Summary.FolderPath, MessageId = item.Summary.MessageId,
                Uid = item.Summary.Uid, Until = until,
            });
            RemoveFromList(item);
        }
        foreach (var i in items.ToList()) RemoveFromList(i);
        RecountSidebar();
        CountersChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UnsnoozeAsync(MessageItem item)
    {
        if (item.Snooze is null) return;
        _s.Snoozes.Remove(item.Snooze.AccountId, item.Snooze.Folder, item.Snooze.Uid);
        item.Snooze = null;
        RecountSidebar();
        await ShowSelectedAsync(reload: false);
    }

    /// <summary>Fällige Erinnerungen und geplante Nachrichten bearbeiten (Timer, Start).</summary>
    public async Task ProcessDueAsync()
    {
        var now = DateTimeOffset.Now;
        var refreshed = new HashSet<string>();
        foreach (var entry in _s.Snoozes.Due(now))
        {
            var account = Accounts.FirstOrDefault(a => a.Id == entry.AccountId);
            if (account is null)
            {
                _s.Snoozes.Remove(entry.AccountId, entry.Folder, entry.Uid);
                continue;
            }
            try
            {
                await _s.Mail.SetSeenAsync(account, entry.Folder, entry.Uid, false); // wieder als ungelesen markieren
            }
            catch (MailServiceException)
            {
                continue; // offline → später erneut
            }
            _s.Snoozes.Remove(entry.AccountId, entry.Folder, entry.Uid);
            var state = State(account.Id);
            var item = state.Inbox.Concat(state.FolderCache.Values.SelectMany(l => l))
                .FirstOrDefault(m => m.Summary.FolderPath == entry.Folder && m.Summary.Uid == entry.Uid);
            if (item is not null)
            {
                ApplySeen(item, false);
                _resurfaced[item.Key] = now;
                ReminderDue?.Invoke(this, item);
            }
            refreshed.Add(account.Id);
        }
        if (refreshed.Count > 0)
        {
            RecountSidebar();
            await ShowSelectedAsync(reload: false);
        }

        foreach (var entry in _s.Outbox.Due(now))
        {
            try
            {
                await SendScheduledAsync(entry);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Geplante Nachricht „{entry.Subject}“: {ex.Message}");
            }
        }
    }

    // ============================================================ Geplant

    public void Schedule(Account account, MimeKit.MimeMessage message, DateTimeOffset sendAt)
    {
        _s.Outbox.Add(account.Id, sendAt, message);
        RecountSidebar();
        if (Selected?.Target == SidebarTarget.Scheduled) _ = ShowSelectedAsync(reload: false);
    }

    public async Task SendScheduledAsync(OutboxEntry entry)
    {
        var account = Accounts.FirstOrDefault(a => a.Id == entry.AccountId)
                      ?? throw new MailServiceException("Das Absenderkonto existiert nicht mehr.");
        var message = _s.Outbox.Load(entry.Id);
        message.Date = DateTimeOffset.Now;
        await _s.Mail.SendMimeAsync(account, message);
        _s.Outbox.Delete(entry.Id);
        foreach (var r in message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes))
            _s.Contacts.RecordSent(r.Address, r.Name, DateTimeOffset.Now);
        try { _s.Contacts.Save(); } catch (IOException) { }
        RecountSidebar();
        if (Selected?.Target == SidebarTarget.Scheduled) await ShowSelectedAsync(reload: false);
    }

    public void DeleteScheduled(OutboxEntry entry)
    {
        _s.Outbox.Delete(entry.Id);
        RecountSidebar();
        if (Selected?.Target == SidebarTarget.Scheduled) _ = ShowSelectedAsync(reload: false);
    }

    public Account? FindAccount(string id) => Accounts.FirstOrDefault(a => a.Id == id);

    /// <summary>Nachricht anhand von Konto/Ordner/UID finden (Mitteilungs-Klick).</summary>
    public MessageItem? FindMessage(string accountId, string folder, uint uid)
    {
        if (!_states.TryGetValue(accountId, out var state)) return null;
        return state.Inbox.Concat(state.FolderCache.Values.SelectMany(l => l))
            .FirstOrDefault(m => m.Summary.FolderPath == folder && m.Summary.Uid == uid);
    }

    /// <summary>Cache leeren (Einstellungen).</summary>
    public void ClearCache()
    {
        _s.Cache.Clear();
    }
}
