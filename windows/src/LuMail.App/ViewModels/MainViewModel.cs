using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LuMail.App.Services;
using LuMail.Core.Models;
using Microsoft.UI.Xaml;

namespace LuMail.App.ViewModels;

/// <summary>Zustand und Logik des Hauptfensters (Sidebar, Nachrichtenliste, Aktionen).</summary>
public sealed class MainViewModel : ObservableObject
{
    private sealed class AccountState
    {
        public List<MailFolder> Folders { get; set; } = new();
        public List<MessageItem> Inbox { get; set; } = new();
        public Dictionary<string, List<MessageItem>> FolderCache { get; } = new(StringComparer.Ordinal);
        public string Error { get; set; } = "";
    }

    private readonly AppServices _s;
    private readonly Dictionary<string, AccountState> _states = new();
    private List<MessageItem> _all = new();
    private int _loadVersion;
    private int _busy;
    private bool _refreshing;

    public MainViewModel(AppServices services)
    {
        _s = services;
    }

    public ObservableCollection<SidebarItem> Sidebar { get; } = new();
    public ObservableCollection<MessageItem> Messages { get; } = new();
    public List<Account> Accounts { get; private set; } = new();
    public SidebarItem? Selected { get; private set; }

    /// <summary>Wird ausgelöst, nachdem <see cref="Messages"/> neu befüllt wurde (Auswahl wiederherstellen).</summary>
    public event EventHandler? MessagesReset;

    /// <summary>Fehlermeldungen für die Anzeige (InfoBar).</summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>True, während <see cref="Messages"/> neu befüllt wird.</summary>
    public bool IsResetting { get; private set; }

    public bool IsBusy => _busy > 0;
    public bool HasAccounts => Accounts.Count > 0;
    public Visibility WelcomeVisibility => HasAccounts ? Visibility.Collapsed : Visibility.Visible;

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
            if (SetProperty(ref _searchText, value)) ApplyFilter();
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

    // ------------------------------------------------------------ Laden

    public void LoadAccounts()
    {
        Accounts = _s.Accounts.Load();
        foreach (var id in _states.Keys.Except(Accounts.Select(a => a.Id)).ToList()) _states.Remove(id);
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(WelcomeVisibility));
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
            var tasks = Accounts.Select(async account =>
            {
                await RefreshAccountAsync(account);
                var state = State(account.Id);
                if (!string.IsNullOrEmpty(state.Error)) errors.Add($"{account.Email}: {state.Error}");
                // Ergebnis sofort anzeigen, ohne auf langsamere Konten zu warten.
                RebuildSidebar();
                if (Selected?.Target != SidebarTarget.Folder || Selected.Account?.Id == account.Id)
                    await ShowSelectedAsync(reload: Selected?.Target == SidebarTarget.Folder);
            }).ToList();
            await Task.WhenAll(tasks);
            if (Accounts.Count == 0) await ShowSelectedAsync(reload: false);
            if (errors.Count > 0) ErrorOccurred?.Invoke(this, string.Join("\n", errors));
        }
        finally
        {
            Busy(false);
            _refreshing = false;
        }
    }

    private async Task RefreshAccountAsync(Account account)
    {
        var state = State(account.Id);
        var provider = _s.Providers.Find(account.ProviderId);
        if (provider is null)
        {
            state.Error = $"Unbekannter Anbieter „{account.ProviderId}“.";
            return;
        }
        try
        {
            var folders = await _s.Mail.ListFoldersAsync(account);
            var inbox = await _s.Mail.LoadInboxAsync(account);
            state.Folders = folders.ToList();
            state.Inbox = inbox.Select(m => new MessageItem(m, account, provider)).ToList();
            state.FolderCache.Clear();
            state.Error = "";
        }
        catch (Exception ex)
        {
            state.Error = ex.Message;
        }
    }

    // ---------------------------------------------------------- Sidebar

    private static string CategoryGlyph(MessageCategory c) => c switch
    {
        MessageCategory.Primary => "",
        MessageCategory.Promotions => "",
        MessageCategory.Social => "",
        MessageCategory.Updates => "",
        MessageCategory.Forums => "",
        _ => "",
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

    private IEnumerable<MessageItem> AllInboxItems => _states.Values.SelectMany(s => s.Inbox);

    /// <summary>Baut die Sidebar nach ARCHITEKTUR.md auf; behält Auswahl und vermeidet Flackern.</summary>
    public void RebuildSidebar()
    {
        var separate = _s.Settings.SeparateCategories;
        var items = new List<SidebarItem>
        {
            new() { Kind = SidebarItemKind.Section, Title = "Übersicht", Key = "section:overview" },
            new() { Kind = SidebarItemKind.Folder, Target = SidebarTarget.AllInboxes, Title = "Alle Posteingänge", Glyph = "", Key = "all", Indent = new Thickness(0) },
            new() { Kind = SidebarItemKind.Folder, Target = SidebarTarget.Unread, Title = "Ungelesen", Glyph = "", Key = "unread" },
            new() { Kind = SidebarItemKind.Folder, Target = SidebarTarget.Flagged, Title = "Markiert", Glyph = "", Key = "flagged" },
        };

        foreach (var provider in _s.Providers.Providers)
        {
            var accounts = Accounts.Where(a => string.Equals(a.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (accounts.Count == 0) continue; // leere Anbietergruppen ausblenden

            items.Add(new SidebarItem
            {
                Kind = SidebarItemKind.Provider, Title = provider.Name, Provider = provider,
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

                if (separate)
                {
                    foreach (var category in MessageCategories.All)
                    {
                        items.Add(new SidebarItem
                        {
                            Kind = SidebarItemKind.Folder, Target = SidebarTarget.Category, Category = category,
                            Title = _s.Categories.DisplayName(category), Glyph = CategoryGlyph(category),
                            Account = account, Provider = provider, FolderPath = "INBOX", Role = FolderRole.Inbox,
                            Key = $"cat:{account.Id}:{category.ToId()}", Indent = new Thickness(20, 0, 0, 0),
                        });
                    }
                }
                else
                {
                    items.Add(new SidebarItem
                    {
                        Kind = SidebarItemKind.Folder, Target = SidebarTarget.Category, Category = null,
                        Title = "Posteingang", Glyph = "", Account = account, Provider = provider,
                        FolderPath = "INBOX", Role = FolderRole.Inbox,
                        Key = $"cat:{account.Id}:inbox", Indent = new Thickness(20, 0, 0, 0),
                    });
                }

                foreach (var folder in state.Folders.Where(f => f.Role != FolderRole.Inbox))
                {
                    items.Add(new SidebarItem
                    {
                        Kind = SidebarItemKind.Folder, Target = SidebarTarget.Folder, Title = folder.DisplayName,
                        Glyph = RoleGlyph(folder.Role), Account = account, Provider = provider,
                        FolderPath = folder.Path, Role = folder.Role,
                        Key = $"folder:{account.Id}:{folder.Path}", Indent = new Thickness(20, 0, 0, 0),
                    });
                }
            }
        }

        var selectedKey = Selected?.Key ?? "all";
        if (Sidebar.Select(i => i.Key).SequenceEqual(items.Select(i => i.Key)))
        {
            // Gleiche Struktur: nur Fehlerstatus übernehmen (Zahlen folgen in RecountSidebar).
            for (var i = 0; i < items.Count; i++) Sidebar[i].Error = items[i].Error;
        }
        else
        {
            Sidebar.Clear();
            foreach (var item in items) Sidebar.Add(item);
        }

        var selected = Sidebar.FirstOrDefault(i => i.Key == selectedKey && i.IsSelectable) ?? Sidebar.First(i => i.Key == "all");
        foreach (var item in Sidebar) item.IsSelected = ReferenceEquals(item, selected);
        Selected = selected;
        RecountSidebar();
    }

    /// <summary>Aktualisiert alle Ungelesen-Zahlen aus den geladenen Daten.</summary>
    public void RecountSidebar()
    {
        var separate = _s.Settings.SeparateCategories;
        foreach (var item in Sidebar.Where(i => i.Kind == SidebarItemKind.Folder))
        {
            item.Unread = item.Target switch
            {
                SidebarTarget.AllInboxes => AllInboxItems.Count(m => !m.Seen && (!separate || m.Category == MessageCategory.Primary)),
                SidebarTarget.Unread => AllInboxItems.Count(m => !m.Seen),
                SidebarTarget.Flagged => AllInboxItems.Count(m => m.Flagged && !m.Seen),
                SidebarTarget.Category when item.Account is not null => State(item.Account.Id).Inbox
                    .Count(m => !m.Seen && (item.Category is null || m.Category == item.Category)),
                SidebarTarget.Folder when item.Account is not null =>
                    State(item.Account.Id).Folders.FirstOrDefault(f => f.Path == item.FolderPath)?.Unread ?? 0,
                _ => 0,
            };
        }
    }

    // ---------------------------------------------------- Nachrichtenliste

    public async Task SelectAsync(SidebarItem item)
    {
        if (!item.IsSelectable) return;
        if (Selected is not null) Selected.IsSelected = false;
        Selected = item;
        item.IsSelected = true;
        await ShowSelectedAsync(reload: false);
    }

    private async Task ShowSelectedAsync(bool reload)
    {
        var item = Selected;
        var version = ++_loadVersion;
        if (item is null)
        {
            SetList(new List<MessageItem>());
            return;
        }

        ListTitle = item.Title;
        var separate = _s.Settings.SeparateCategories;
        List<MessageItem> list;
        switch (item.Target)
        {
            case SidebarTarget.AllInboxes:
                list = AllInboxItems.Where(m => !separate || m.Category == MessageCategory.Primary).ToList();
                break;
            case SidebarTarget.Unread:
                list = AllInboxItems.Where(m => !m.Seen).ToList();
                break;
            case SidebarTarget.Flagged:
                list = AllInboxItems.Where(m => m.Flagged).ToList();
                break;
            case SidebarTarget.Category when item.Account is not null:
                list = State(item.Account.Id).Inbox.Where(m => item.Category is null || m.Category == item.Category).ToList();
                break;
            case SidebarTarget.Folder when item.Account is not null && item.FolderPath is not null:
                var state = State(item.Account.Id);
                if (!reload && state.FolderCache.TryGetValue(item.FolderPath, out var cached))
                {
                    list = cached;
                    break;
                }
                SetList(state.FolderCache.TryGetValue(item.FolderPath, out var stale) ? stale : new List<MessageItem>());
                Busy(true);
                try
                {
                    var provider = _s.Providers.Get(item.Account.ProviderId);
                    var summaries = await _s.Mail.LoadFolderAsync(item.Account, item.FolderPath);
                    list = summaries.Select(m => new MessageItem(m, item.Account, provider)).ToList();
                    state.FolderCache[item.FolderPath] = list;
                }
                catch (Exception ex)
                {
                    if (version == _loadVersion) ErrorOccurred?.Invoke(this, ex.Message);
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

        if (version != _loadVersion) return; // inzwischen wurde etwas anderes gewählt
        SetList(list);
    }

    private void SetList(List<MessageItem> list)
    {
        _all = list.OrderByDescending(m => m.Summary.Date).ToList();
        ApplyFilter();
    }

    /// <summary>Filtert die geladenen Nachrichten nach dem Suchtext.</summary>
    public void ApplyFilter()
    {
        IsResetting = true;
        try
        {
            Messages.Clear();
            foreach (var m in _all.Where(m => m.Matches(_searchText))) Messages.Add(m);
        }
        finally
        {
            IsResetting = false;
        }
        var unread = Messages.Count(m => !m.Seen);
        ListSubtitle = Messages.Count == 0
            ? "Keine Nachrichten"
            : $"{Messages.Count} Nachrichten" + (unread > 0 ? $", {unread} ungelesen" : "");
        MessagesReset?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ Aktionen

    /// <summary>Lädt die Nachricht vollständig und markiert sie als gelesen.</summary>
    public async Task<MessageDetail> OpenAsync(MessageItem item)
    {
        var detail = await _s.Mail.FetchMessageAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid, markSeen: true);
        if (!item.Seen) ApplySeen(item, true);
        return detail;
    }

    public async Task SetSeenAsync(MessageItem item, bool seen)
    {
        await _s.Mail.SetSeenAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid, seen);
        ApplySeen(item, seen);
    }

    private void ApplySeen(MessageItem item, bool seen)
    {
        if (item.Seen == seen) return;
        item.Seen = seen;
        var folder = State(item.Account.Id).Folders.FirstOrDefault(f => f.Path == item.Summary.FolderPath);
        if (folder is not null) folder.Unread = Math.Max(0, folder.Unread + (seen ? -1 : 1));
        RecountSidebar();
    }

    public async Task SetFlaggedAsync(MessageItem item, bool flagged)
    {
        await _s.Mail.SetFlaggedAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid, flagged);
        item.Flagged = flagged;
        RecountSidebar();
    }

    public FolderRole RoleOf(MessageItem item) => _s.Mail.RoleOf(item.Account, item.Summary.FolderPath);

    /// <summary>Archivieren, Spam, Kein Spam (→ Posteingang).</summary>
    public async Task MoveAsync(MessageItem item, FolderRole target)
    {
        await _s.Mail.MoveToRoleAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid, target);
        RemoveLocal(item);
        var state = State(item.Account.Id);
        var targetPath = target == FolderRole.Inbox ? "INBOX" : state.Folders.FirstOrDefault(f => f.Role == target)?.Path;
        if (targetPath is not null) state.FolderCache.Remove(targetPath);
        if (target == FolderRole.Inbox) await RefreshAllAsync();
    }

    /// <summary>Löschen: Papierkorb bzw. endgültig im Papierkorb.</summary>
    public async Task DeleteAsync(MessageItem item)
    {
        await _s.Mail.DeleteAsync(item.Account, item.Summary.FolderPath, item.Summary.Uid);
        RemoveLocal(item);
        var state = State(item.Account.Id);
        var trash = state.Folders.FirstOrDefault(f => f.Role == FolderRole.Trash)?.Path;
        if (trash is not null) state.FolderCache.Remove(trash);
    }

    private void RemoveLocal(MessageItem item)
    {
        var state = State(item.Account.Id);
        state.Inbox.Remove(item);
        foreach (var list in state.FolderCache.Values) list.Remove(item);
        if (!item.Seen)
        {
            var folder = state.Folders.FirstOrDefault(f => f.Path == item.Summary.FolderPath);
            if (folder is not null) folder.Unread = Math.Max(0, folder.Unread - 1);
        }
        _all.Remove(item);
        Messages.Remove(item);
        RecountSidebar();
    }

    /// <summary>„Absender immer als … einsortieren“ – gilt für alle Konten, überschreibt alles.</summary>
    public void SetSenderRule(MessageItem item, MessageCategory category)
    {
        if (string.IsNullOrWhiteSpace(item.Summary.FromAddress)) return;
        _s.SenderRules.Set(item.Summary.FromAddress, category);
        foreach (var m in AllInboxItems)
        {
            if (_s.SenderRules.Lookup(m.Summary.FromAddress) is { } c) m.Category = c;
        }
        RecountSidebar();
        if (Selected?.Target is SidebarTarget.Category or SidebarTarget.AllInboxes) _ = ShowSelectedAsync(reload: false);
    }

    public bool HasSenderRule(MessageItem item) =>
        _s.SenderRules.All.ContainsKey(item.Summary.FromAddress.Trim().ToLowerInvariant());

    public async Task RemoveSenderRuleAsync(MessageItem item)
    {
        if (_s.SenderRules.Remove(item.Summary.FromAddress)) await RefreshAllAsync();
    }

    public Account? FindAccount(string id) => Accounts.FirstOrDefault(a => a.Id == id);
}
