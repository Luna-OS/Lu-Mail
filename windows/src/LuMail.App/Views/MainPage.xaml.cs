using System.ComponentModel;
using System.Globalization;
using LuMail.App.Services;
using LuMail.App.ViewModels;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace LuMail.App.Views;

/// <summary>Inhalt des Hauptfensters: Sidebar, Nachrichtenliste und Leseansicht.</summary>
public sealed partial class MainPage : Page
{
    private readonly AppServices _s = App.Services;
    private DispatcherQueueTimer? _refreshTimer;
    private DispatcherQueueTimer? _dueTimer;
    private bool _initialized;

    // Leseansicht
    private MessageItem? _current;
    private MessageDetail? _currentDetail;
    private IReadOnlyList<MessageDetail> _threadDetails = Array.Empty<MessageDetail>();
    private bool _externalAllowed;
    private bool _webViewReady;
    private bool _navigatingInternally;
    private int _openVersion;

    // Auswahl
    private string? _selectedKey;
    private bool _restoringSelection;

    // App-Sperre
    private bool _locked;
    private DateTimeOffset? _deactivatedAt;

    public MainPage()
    {
        Vm = new MainViewModel(App.Services, action => DispatcherQueue.TryEnqueue(() => action()));
        InitializeComponent();
        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => RenderCurrent();
    }

    public MainViewModel Vm { get; }

    /// <summary>Das Fenster, in dem diese Seite angezeigt wird.</summary>
    public Window? HostWindow { get; set; }

    /// <summary>Symbol im Infobereich / Taskleisten-Zähler (vom Fenster gesetzt).</summary>
    public TrayService? Tray { get; set; }

    public UIElement TitleBarElement => AppTitleBar;

    public void UseTransparentBackground() => RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    // ================================================================ Start

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        SidebarList.ItemsSource = Vm.Sidebar;
        MessageList.ItemsSource = Vm.Messages;
        ConversationToggle.IsChecked = Vm.ConversationsEnabled;
        Vm.PropertyChanged += OnViewModelPropertyChanged;
        Vm.MessagesReset += OnMessagesReset;
        Vm.ErrorOccurred += (_, message) => ShowMessage(message, isError: true);
        Vm.InfoOccurred += (_, message) => ShowMessage(message, isError: false, severity: InfoBarSeverity.Informational);
        Vm.NewMessagesArrived += (_, items) =>
        {
            foreach (var item in items.OrderByDescending(m => m.Summary.Date).Take(5)) App.Notifications.ShowMessage(item);
        };
        Vm.ReminderDue += (_, item) => App.Notifications.ShowMessage(item, "Erinnerung: ");
        Vm.CountersChanged += (_, _) => Tray?.Update(Vm.PrimaryUnreadCount, Vm.NewestUnread());

        StartRefreshTimer();
        StartDueTimer();
        App.Updates.StateChanged += (_, _) => UpdateUpdateBar();
        App.Updates.Start(DispatcherQueue);
        UpdateUpdateBar();

        if (_s.Settings.AppLock) Lock(promptNow: true);

        await InitWebViewAsync();
        await Vm.LoadFromCacheAsync();
        UpdateListHeader();
        if (App.PendingNotification is { } pending)
        {
            App.PendingNotification = null;
            OpenFromNotification(pending);
        }
        await Vm.RefreshAllAsync();
        UpdateListHeader();
    }

    public void StartRefreshTimer()
    {
        _refreshTimer ??= DispatcherQueue.CreateTimer();
        _refreshTimer.Stop();
        _refreshTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(_s.Settings.RefreshIntervalMinutes, 1, 120));
        _refreshTimer.IsRepeating = true;
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
    }

    private async void OnRefreshTimerTick(DispatcherQueueTimer sender, object args) => await Vm.RefreshAllAsync();

    /// <summary>Fällige Erinnerungen und geplante Nachrichten alle 30 s prüfen.</summary>
    private void StartDueTimer()
    {
        _dueTimer = DispatcherQueue.CreateTimer();
        _dueTimer.Interval = TimeSpan.FromSeconds(30);
        _dueTimer.IsRepeating = true;
        _dueTimer.Tick += async (_, _) =>
        {
            try { await Vm.ProcessDueAsync(); }
            catch (Exception ex) { ShowMessage(ex.Message, isError: true); }
        };
        _dueTimer.Start();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy))
            BusyBar.Visibility = Vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        UpdateListHeader();
    }

    private void UpdateListHeader()
    {
        ListTitleText.Text = Vm.ListTitle;
        ListSubtitleText.Text = Vm.ListSubtitle;
        OfflineText.Visibility = Vm.IsOffline ? Visibility.Visible : Visibility.Collapsed;
        WelcomePanel.Visibility = Vm.WelcomeVisibility;
        EmptyText.Visibility = Vm.HasAccounts && Vm.Messages.Count == 0 && !Vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    // ============================================================ Sidebar

    private async void OnSidebarItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SidebarItem item && item.IsSelectable)
        {
            await Vm.SelectAsync(item);
            UpdateListHeader();
        }
    }

    // ==================================================== Nachrichtenliste

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) Vm.SearchText = sender.Text;
    }

    /// <summary>Enter im Suchfeld: zusätzlich Serversuche.</summary>
    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        Vm.SearchText = sender.Text;
        await Vm.ServerSearchAsync();
        UpdateListHeader();
    }

    private void OnConversationToggleClick(object sender, RoutedEventArgs e)
    {
        Vm.ConversationsEnabled = ConversationToggle.IsChecked == true;
        UpdateListHeader();
    }

    private void OnMessagesReset(object? sender, EventArgs e)
    {
        UpdateListHeader();
        UpdateBulkBar();
        if (_selectedKey is null) return;
        var match = Vm.Messages.FirstOrDefault(m => m.Key == _selectedKey && !m.IsHeader);
        _restoringSelection = true;
        try
        {
            MessageList.SelectedItem = match;
        }
        finally
        {
            _restoringSelection = false;
        }
        if (match is null) ClearReader();
        else _current = match;
    }

    private List<MessageItem> SelectedItems() =>
        MessageList.SelectedItems.OfType<MessageItem>().Where(m => !m.IsHeader).ToList();

    private async void OnMessageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm.IsResetting || _restoringSelection) return;
        UpdateBulkBar();
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            ClearReader();
            return;
        }
        if (selected.Count > 1) return; // Mehrfachauswahl: Leiste statt Leseansicht
        var item = selected[0];
        if (item.Key == _selectedKey && ReferenceEquals(item, _current) && _currentDetail is not null) return;
        _selectedKey = item.Key;
        await OpenMessageAsync(item);
    }

    private void UpdateBulkBar()
    {
        var count = SelectedItems().Count;
        BulkBar.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        BulkText.Text = $"{count} ausgewählt";
    }

    // ======================================================== Leseansicht

    private async Task InitWebViewAsync()
    {
        try
        {
            await ReaderView.EnsureCoreWebView2Async();
            var core = ReaderView.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = true;
            core.NavigationStarting += OnReaderNavigationStarting;
            core.NewWindowRequested += OnReaderNewWindowRequested;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnReaderWebResourceRequested;
            _webViewReady = true;
        }
        catch (Exception ex)
        {
            ShowMessage("Die Web-Ansicht (WebView2) konnte nicht gestartet werden: " + ex.Message, isError: true);
        }
    }

    private void OnReaderNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var uri = args.Uri ?? "";
        if (_navigatingInternally && (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                                      || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                                      || uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)))
        {
            _navigatingInternally = false;
            return;
        }
        args.Cancel = true;
        if (args.IsUserInitiated) OpenLink(uri);
    }

    private void OnReaderNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (args.IsUserInitiated) OpenLink(args.Uri ?? "");
    }

    private void OnReaderWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (_externalAllowed) return;
        var uri = args.Request.Uri ?? "";
        if (uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            args.Response = sender.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
    }

    private void OpenLink(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return;
        if (parsed.Scheme == Uri.UriSchemeMailto)
        {
            var address = Uri.UnescapeDataString(parsed.GetComponents(UriComponents.Path, UriFormat.UriEscaped));
            var query = LuMail.Core.Auth.OAuthUrls.ParseQuery(parsed.Query);
            var from = _current?.Account ?? DefaultAccount();
            OpenCompose(new OutgoingMessage
            {
                To = address,
                Subject = query.TryGetValue("subject", out var subject) ? subject : "",
                Body = (query.TryGetValue("body", out var body) ? body : "") + MessageComposer.SignatureBlock(from?.Signature),
            }, from);
            return;
        }
        if (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            AppServices.OpenInBrowser(parsed);
    }

    private async Task OpenMessageAsync(MessageItem item)
    {
        var version = ++_openVersion;
        _current = item;
        _currentDetail = null;
        _threadDetails = Array.Empty<MessageDetail>();
        _externalAllowed = _s.Settings.AlwaysLoadExternalImages;
        ShowReaderHeader(item, null);
        AttachmentsPanel.Children.Clear();
        AttachmentsScroller.Visibility = Visibility.Collapsed;
        ExternalBanner.Visibility = Visibility.Collapsed;
        PhishingBar.IsOpen = false;
        UnsubscribeButton.Visibility = Visibility.Collapsed;
        ScheduledBar.Visibility = item.Outbox is null ? Visibility.Collapsed : Visibility.Visible;
        ReaderCommands.Visibility = item.Outbox is null ? Visibility.Visible : Visibility.Collapsed;
        ReminderBar.Visibility = item.Snooze is null ? Visibility.Collapsed : Visibility.Visible;
        if (item.Outbox is not null) ScheduledText.Text = $"Wird am {SnoozePresets.Format(item.Outbox.SendAt)} von {item.Account.Email} gesendet.";
        if (item.Snooze is not null) ReminderText.Text = $"Erinnerung am {SnoozePresets.Format(item.Snooze.Until)} – bis dahin ausgeblendet.";
        ReaderLoading.IsActive = true;
        try
        {
            if (item.Outbox is not null)
            {
                var mime = _s.Outbox.Load(item.Outbox.Id);
                _currentDetail = MailService.ToDetail(item.Account.Id, item.Summary.FolderPath, 0, mime);
                _threadDetails = new[] { _currentDetail };
            }
            else if (item.ThreadItems is { Count: > 1 })
            {
                _threadDetails = await Vm.OpenThreadAsync(item);
                _currentDetail = _threadDetails[^1];
            }
            else
            {
                _currentDetail = await Vm.OpenAsync(item);
                _threadDetails = new[] { _currentDetail };
            }
            if (version != _openVersion) return;

            ShowReaderHeader(item, _currentDetail);
            ShowAttachments(_currentDetail);
            var phishing = _s.Phishing.Check(_currentDetail.From?.Name, _currentDetail.From?.Address, _currentDetail.HtmlBody);
            PhishingBar.Message = string.Join(" ", phishing);
            PhishingBar.IsOpen = phishing.Count > 0;
            var unsubscribe = UnsubscribeInfo.Parse(_currentDetail.ListUnsubscribe ?? item.Summary.ListUnsubscribe,
                _currentDetail.ListUnsubscribePost ?? item.Summary.ListUnsubscribePost);
            UnsubscribeButton.Visibility = unsubscribe is not null && item.Outbox is null ? Visibility.Visible : Visibility.Collapsed;
            RenderCurrent();
        }
        catch (Exception ex)
        {
            if (version == _openVersion) ShowMessage(ex.Message, isError: true);
        }
        finally
        {
            if (version == _openVersion) ReaderLoading.IsActive = false;
            UpdateActionButtons();
            UpdateListHeader();
        }
    }

    private void ShowReaderHeader(MessageItem item, MessageDetail? detail)
    {
        ReaderPlaceholder.Visibility = Visibility.Collapsed;
        ReaderPanel.Visibility = Visibility.Visible;

        ReaderSubject.Text = item.ThreadCount > 1 ? $"{item.Subject} ({item.ThreadCount})" : item.Subject;
        var from = detail?.From;
        ReaderFrom.Text = from is null
            ? item.Sender
            : (string.IsNullOrWhiteSpace(from.Name) ? from.Address : $"{from.Name} <{from.Address}>");

        var to = detail is null ? item.Summary.To : string.Join(", ", detail.To.Select(a => a.DisplayName));
        var cc = detail is null || detail.Cc.Count == 0 ? "" : "  ·  Cc: " + string.Join(", ", detail.Cc.Select(a => a.DisplayName));
        ReaderTo.Text = string.IsNullOrEmpty(to) ? cc.TrimStart(' ', '·') : "An: " + to + cc;
        ReaderDate.Text = item.FullDateText;

        var initial = item.Sender.Trim().FirstOrDefault(char.IsLetterOrDigit);
        ReaderAvatarText.Text = initial == default ? "?" : char.ToUpper(initial, CultureInfo.CurrentCulture).ToString();
        ReaderAvatar.Background = BrushCache.ForCategory(item.CategoryId ?? "primary");
        ReaderAccountChip.Background = item.ProviderBrush;
        ReaderAccountText.Text = item.Account.Email;
        UpdateActionButtons();
    }

    private void ShowAttachments(MessageDetail detail)
    {
        AttachmentsPanel.Children.Clear();
        foreach (var attachment in detail.Attachments)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new FontIcon { Glyph = "", FontSize = 13 });
            content.Children.Add(new TextBlock { Text = $"{attachment.FileName} ({FormatSize(attachment.Size)})" });
            var button = new Button { Content = content, Tag = attachment };
            ToolTipService.SetToolTip(button, "In „Downloads“ speichern");
            button.Click += OnAttachmentClick;
            AttachmentsPanel.Children.Add(button);
        }
        AttachmentsScroller.Visibility = detail.Attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    private void OnAttachmentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AttachmentInfo attachment) return;
        try
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            var name = string.Concat(attachment.FileName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            if (string.IsNullOrWhiteSpace(name)) name = "Anhang";
            var target = Path.Combine(downloads, name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (var i = 1; File.Exists(target); i++) target = Path.Combine(downloads, $"{stem} ({i}){ext}");
            File.WriteAllBytes(target, attachment.Content);
            ShowMessage($"Gespeichert: {target}", isError: false, actionText: "Ordner öffnen", action: () => AppServices.OpenFolder(downloads));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("Anhang konnte nicht gespeichert werden: " + ex.Message, isError: true);
        }
    }

    /// <summary>Zeigt die aktuelle Nachricht bzw. Konversation (mit CSP) in der Web-Ansicht.</summary>
    private void RenderCurrent()
    {
        if (_currentDetail is null || !_webViewReady) return;
        var dark = ActualTheme == ElementTheme.Dark;
        var tokens = _s.Theme.Palette(_s.Settings.ThemeVariant, dark, _s.Settings.Accent);
        var style = new PlainTextStyle(tokens["surface"], tokens["textPrimary"], tokens["accent"]);
        var html = _threadDetails.Count > 1
            ? HtmlPreparer.PrepareConversation(_threadDetails, _externalAllowed, style)
            : HtmlPreparer.Prepare(_currentDetail, _externalAllowed, style);

        // §14 Tracking-Bericht + Hinweis auf blockierte Inhalte
        var allHtml = string.Join("\n", _threadDetails.Select(d => d.HtmlBody ?? ""));
        var trackers = _s.Trackers.Detect(allHtml);
        var hasExternal = HtmlPreparer.HasExternalContent(allHtml);
        if (!_externalAllowed && (hasExternal || trackers.Count > 0))
        {
            ExternalText.Text = trackers.Count > 0
                ? $"{trackers.Describe()}. Externe Inhalte wurden zum Schutz deiner Privatsphäre blockiert."
                : "Externe Bilder und Inhalte wurden zum Schutz deiner Privatsphäre blockiert.";
            ExternalBanner.Visibility = Visibility.Visible;
        }
        else
        {
            ExternalBanner.Visibility = Visibility.Collapsed;
        }

        _navigatingInternally = true;
        if (html.Length < 1_500_000)
        {
            ReaderView.CoreWebView2.NavigateToString(html);
        }
        else
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuMail", "Temp");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "nachricht.html");
            File.WriteAllText(file, html);
            ReaderView.CoreWebView2.Navigate(new Uri(file).AbsoluteUri);
        }
    }

    private void OnLoadExternalClick(object sender, RoutedEventArgs e)
    {
        _externalAllowed = true;
        RenderCurrent();
    }

    private void ClearReader()
    {
        _openVersion++;
        _current = null;
        _currentDetail = null;
        _threadDetails = Array.Empty<MessageDetail>();
        _selectedKey = null;
        ReaderPanel.Visibility = Visibility.Collapsed;
        ReaderPlaceholder.Visibility = Visibility.Visible;
        ReaderLoading.IsActive = false;
        if (_webViewReady)
        {
            _navigatingInternally = true;
            ReaderView.CoreWebView2.NavigateToString("<!doctype html><html><body></body></html>");
        }
    }

    private void UpdateActionButtons()
    {
        var item = _current;
        if (item is null) return;
        var role = Vm.RoleOf(item);
        FlagButton.Label = item.Flagged ? "Markierung entfernen" : "Markieren";
        SeenButton.Label = item.Seen ? "Als ungelesen markieren" : "Als gelesen markieren";
        SpamButton.Label = role == FolderRole.Spam ? "Kein Spam" : "Spam";
        DeleteButton.Label = role == FolderRole.Trash ? "Endgültig löschen" : "Löschen";
        ArchiveButton.IsEnabled = role != FolderRole.Archive;
    }

    // ============================================================ Aktionen

    /// <summary>Ziel einer Aktion: Mehrfachauswahl oder die geöffnete Nachricht.</summary>
    private List<MessageItem> ActionTargets()
    {
        var selected = SelectedItems();
        if (selected.Count > 1) return selected;
        return _current is null || _current.Outbox is not null ? new List<MessageItem>() : new List<MessageItem> { _current };
    }

    private async Task RunActionAsync(Func<List<MessageItem>, Task> action, bool removesMessages)
    {
        var targets = ActionTargets();
        if (targets.Count == 0) return;
        var index = Vm.Messages.IndexOf(targets[0]);
        try
        {
            await action(targets);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, isError: true);
            return;
        }

        if (removesMessages)
        {
            ClearReader();
            if (index >= 0 && Vm.Messages.Count > 0)
            {
                var next = Math.Min(index, Vm.Messages.Count - 1);
                if (Vm.Messages[next].IsHeader && next + 1 < Vm.Messages.Count) next++;
                MessageList.SelectedIndex = next;
            }
        }
        else
        {
            UpdateActionButtons();
        }
        UpdateBulkBar();
        UpdateListHeader();
    }

    private Task ArchiveAsync() => RunActionAsync(items => Vm.MoveAsync(items, FolderRole.Archive), removesMessages: true);
    private Task DeleteAsync() => RunActionAsync(items => Vm.DeleteAsync(items), removesMessages: true);
    private Task SpamAsync() => RunActionAsync(items =>
        Vm.MoveAsync(items, Vm.RoleOf(items[0]) == FolderRole.Spam ? FolderRole.Inbox : FolderRole.Spam), removesMessages: true);
    private Task ToggleFlagAsync() => RunActionAsync(items => Vm.SetFlaggedAsync(items, !items.All(i => i.Flagged)), removesMessages: false);
    private Task ToggleSeenAsync() => RunActionAsync(items => Vm.SetSeenAsync(items, !items.All(i => i.Seen)), removesMessages: false);

    private async void OnArchiveClick(object sender, RoutedEventArgs e) => await ArchiveAsync();
    private async void OnDeleteClick(object sender, RoutedEventArgs e) => await DeleteAsync();
    private async void OnSpamClick(object sender, RoutedEventArgs e) => await SpamAsync();
    private async void OnFlagClick(object sender, RoutedEventArgs e) => await ToggleFlagAsync();
    private async void OnSeenClick(object sender, RoutedEventArgs e) => await ToggleSeenAsync();

    private async void OnBulkSeenClick(object sender, RoutedEventArgs e) => await RunActionAsync(i => Vm.SetSeenAsync(i, true), false);
    private async void OnBulkUnseenClick(object sender, RoutedEventArgs e) => await RunActionAsync(i => Vm.SetSeenAsync(i, false), false);
    private async void OnBulkFlagClick(object sender, RoutedEventArgs e) => await ToggleFlagAsync();
    private async void OnBulkArchiveClick(object sender, RoutedEventArgs e) => await ArchiveAsync();
    private async void OnBulkDeleteClick(object sender, RoutedEventArgs e) => await DeleteAsync();
    private async void OnBulkSpamClick(object sender, RoutedEventArgs e) => await SpamAsync();

    private void OnSortFlyoutOpening(object sender, object e)
    {
        SortFlyout.Items.Clear();
        var item = _current;
        if (item is null || string.IsNullOrWhiteSpace(item.Summary.FromAddress)) return;

        var ids = MessageCategories.All.Select(c => c.ToId()).Concat(_s.Rules.CustomCategories.Select(c => c.Id));
        foreach (var id in ids)
        {
            var menuItem = new MenuFlyoutItem { Text = $"Absender immer als „{_s.CategoryName(id)}“ einsortieren", Tag = id };
            menuItem.Click += OnSortSenderClick;
            SortFlyout.Items.Add(menuItem);
        }
        if (Vm.HasSenderRule(item))
        {
            SortFlyout.Items.Add(new MenuFlyoutSeparator());
            var remove = new MenuFlyoutItem { Text = "Regel für diesen Absender entfernen" };
            remove.Click += async (_, _) =>
            {
                try { await Vm.RemoveSenderRuleAsync(item); }
                catch (Exception ex) { ShowMessage(ex.Message, isError: true); }
            };
            SortFlyout.Items.Add(remove);
        }
    }

    private void OnSortSenderClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || (sender as MenuFlyoutItem)?.Tag is not string id) return;
        Vm.SetSenderRule(_current, id);
        ShowMessage($"Nachrichten von {_current.Summary.FromAddress} werden ab jetzt als „{_s.CategoryName(id)}“ einsortiert.", isError: false);
    }

    // ===================================================== Später erinnern

    private void OnSnoozeFlyoutOpening(object sender, object e)
    {
        SnoozeFlyout.Items.Clear();
        foreach (var preset in SnoozePresets.For(DateTimeOffset.Now))
        {
            var item = new MenuFlyoutItem { Text = preset.Label, Tag = preset.Until };
            item.Click += (_, _) => SnoozeTargets(preset.Until);
            SnoozeFlyout.Items.Add(item);
        }
        SnoozeFlyout.Items.Add(new MenuFlyoutSeparator());
        var pick = new MenuFlyoutItem { Text = "Datum wählen …" };
        pick.Click += async (_, _) =>
        {
            var when = await Dialogs.PickDateTimeAsync(XamlRoot, "Später erinnern", DateTimeOffset.Now.Date.AddDays(1).AddHours(8));
            if (when is { } w) SnoozeTargets(w);
        };
        SnoozeFlyout.Items.Add(pick);
    }

    private void SnoozeTargets(DateTimeOffset until)
    {
        var targets = ActionTargets();
        if (targets.Count == 0) return;
        Vm.Snooze(targets, until);
        ClearReader();
        UpdateListHeader();
        ShowMessage($"Erinnerung am {SnoozePresets.Format(until)}.", isError: false, severity: InfoBarSeverity.Informational);
    }

    private async void OnUnsnoozeClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await Vm.UnsnoozeAsync(_current);
        ReminderBar.Visibility = Visibility.Collapsed;
    }

    // ============================================================ Geplant

    private async void OnSendScheduledNowClick(object sender, RoutedEventArgs e)
    {
        if (_current?.Outbox is not { } entry) return;
        try
        {
            await Vm.SendScheduledAsync(entry);
            ClearReader();
            ShowMessage("Die Nachricht wurde gesendet.", isError: false);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
    }

    private async void OnDeleteScheduledClick(object sender, RoutedEventArgs e)
    {
        if (_current?.Outbox is not { } entry) return;
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Geplante Nachricht löschen?", $"„{entry.Subject}“ wird nicht gesendet.", "Löschen")) return;
        Vm.DeleteScheduled(entry);
        ClearReader();
    }

    // ========================================================= Abbestellen

    private async void OnUnsubscribeClick(object sender, RoutedEventArgs e)
    {
        var item = _current;
        var detail = _currentDetail;
        if (item is null || detail is null) return;
        var info = UnsubscribeInfo.Parse(detail.ListUnsubscribe ?? item.Summary.ListUnsubscribe, detail.ListUnsubscribePost ?? item.Summary.ListUnsubscribePost);
        if (info is null) return;

        var sender2 = item.Summary.FromDisplay;
        var question = info.Method switch
        {
            UnsubscribeMethod.OneClickPost => $"Lu-Mail meldet dich direkt bei „{sender2}“ ab (ein Klick, RFC 8058).",
            UnsubscribeMethod.Mailto => $"Lu-Mail sendet von {item.Account.Email} eine Abmelde-E-Mail an {info.MailtoAddress}.",
            _ => $"Die Abmeldeseite von „{sender2}“ wird im Browser geöffnet.",
        };
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Newsletter abbestellen?", question, "Abbestellen")) return;

        try
        {
            switch (info.Method)
            {
                case UnsubscribeMethod.OneClickPost:
                    if (!await OneClickUnsubscribe.PostAsync(_s.UnsubscribeHttp, info.HttpsUrl!))
                        throw new InvalidOperationException("Der Absender hat die Abmeldung nicht bestätigt. Versuche es über die Webseite.");
                    break;
                case UnsubscribeMethod.Mailto:
                    await _s.Mail.SendAsync(item.Account, new OutgoingMessage { To = info.MailtoAddress!, Subject = info.MailtoSubject, Body = "unsubscribe" });
                    break;
                case UnsubscribeMethod.Browser:
                    AppServices.OpenInBrowser(info.HttpsUrl!);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowMessage("Abbestellen fehlgeschlagen: " + ex.Message, isError: true);
            return;
        }

        if (info.Method != UnsubscribeMethod.Browser) ShowMessage($"Von „{sender2}“ abgemeldet.", isError: false);
        if (await Dialogs.ConfirmAsync(XamlRoot, "Absender einsortieren?",
                $"Sollen weitere Nachrichten von {item.Summary.FromAddress} künftig unter „Werbung“ landen?", "Ja, als Werbung", "Nein"))
            Vm.SetSenderRule(item, MessageCategory.Promotions.ToId());
    }

    // ======================================================== Verfassen

    private Account? DefaultAccount() => _current?.Account ?? Vm.Selected?.Account ?? Vm.Accounts.FirstOrDefault();

    private void OpenCompose(OutgoingMessage? draft = null, Account? from = null)
    {
        if (Vm.Accounts.Count == 0)
        {
            _ = ShowAddAccountAsync(); // §19: „Neue E-Mail“ ohne Konto öffnet „Konto hinzufügen“
            return;
        }
        var window = new ComposeWindow();
        window.Initialize(Vm.Accounts, from ?? DefaultAccount(), draft, Vm);
        window.Activate();
    }

    private void OnNewMailClick(object sender, RoutedEventArgs e) => OpenCompose();

    private void OnNewMailAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenCompose();
    }

    private void OnReplyClick(object sender, RoutedEventArgs e) => Reply(replyAll: false);
    private void OnReplyAllClick(object sender, RoutedEventArgs e) => Reply(replyAll: true);

    private void Reply(bool replyAll)
    {
        if (_current is null || _currentDetail is null || _current.Outbox is not null) return;
        OpenCompose(MessageComposer.CreateReply(_currentDetail, _current.Account, replyAll), _current.Account);
    }

    private void OnForwardClick(object sender, RoutedEventArgs e) => Forward();

    private void Forward()
    {
        if (_current is null || _currentDetail is null || _current.Outbox is not null) return;
        OpenCompose(MessageComposer.CreateForward(_currentDetail, _current.Account), _current.Account);
    }

    // ======================================================= Tastenkürzel

    /// <summary>§6: j/k/e/#/!/r/a/f/s/u/c// – nur wenn kein Textfeld den Fokus hat.</summary>
    private async void OnRootCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (_locked || XamlRoot is null) return;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (focused is TextBox or AutoSuggestBox or PasswordBox or RichEditBox or ComboBox) return;
        var handled = true;
        switch (args.Character)
        {
            case 'j': MoveSelection(+1); break;
            case 'k': MoveSelection(-1); break;
            case 'e': await ArchiveAsync(); break;
            case '#': await DeleteAsync(); break;
            case '!': await SpamAsync(); break;
            case 'r': Reply(replyAll: false); break;
            case 'a': Reply(replyAll: true); break;
            case 'f': Forward(); break;
            case 's': await ToggleFlagAsync(); break;
            case 'u': await ToggleSeenAsync(); break;
            case 'c': OpenCompose(); break;
            case '/': SearchBox.Focus(FocusState.Keyboard); break;
            default: handled = false; break;
        }
        args.Handled = handled;
    }

    private void MoveSelection(int delta)
    {
        if (Vm.Messages.Count == 0) return;
        var index = MessageList.SelectedIndex;
        var next = index < 0 ? 0 : index + delta;
        while (next >= 0 && next < Vm.Messages.Count && Vm.Messages[next].IsHeader) next += delta;
        if (next < 0 || next >= Vm.Messages.Count) return;
        MessageList.SelectedItems.Clear();
        MessageList.SelectedIndex = next;
        MessageList.ScrollIntoView(Vm.Messages[next]);
    }

    // ===================================================== Aktualisieren

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await Vm.RefreshAllAsync();

    private async void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await Vm.RefreshAllAsync();
    }

    // ===================================================== Mitteilungen

    /// <summary>Klick auf eine Systemmitteilung oder einen Eintrag im Infobereich: Nachricht öffnen.</summary>
    public void OpenFromNotification(IDictionary<string, string> args)
    {
        if (!args.TryGetValue("account", out var accountId) || !args.TryGetValue("folder", out var folder)
            || !args.TryGetValue("uid", out var uidText) || !uint.TryParse(uidText, NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
            return;
        var item = Vm.FindMessage(accountId, folder, uid);
        if (item is not null) _ = OpenItemAsync(item);
    }

    public async Task OpenItemAsync(MessageItem item)
    {
        HostWindow?.Activate();
        var inList = Vm.Messages.FirstOrDefault(m => m.Key == item.Key && !m.IsHeader)
                     ?? Vm.Messages.FirstOrDefault(m => m.ThreadItems?.Any(t => t.Key == item.Key) == true);
        if (inList is null)
        {
            var key = item.Summary.FolderPath == "INBOX"
                ? $"cat:{item.Account.Id}:{(_s.Settings.SeparateCategories ? item.CategoryId ?? "primary" : "inbox")}"
                : $"folder:{item.Account.Id}:{item.Summary.FolderPath}";
            if (Vm.FindSidebar(key) is { } sidebarItem) await Vm.SelectAsync(sidebarItem);
            inList = Vm.Messages.FirstOrDefault(m => m.Key == item.Key && !m.IsHeader)
                     ?? Vm.Messages.FirstOrDefault(m => m.ThreadItems?.Any(t => t.Key == item.Key) == true);
        }
        if (inList is null) return;
        MessageList.SelectedItems.Clear();
        MessageList.SelectedItem = inList;
        MessageList.ScrollIntoView(inList);
    }

    // ========================================================= App-Sperre

    public void Lock(bool promptNow = false)
    {
        if (!_s.Settings.AppLock) return;
        _locked = true;
        LockOverlay.Visibility = Visibility.Visible;
        if (promptNow) DispatcherQueue.TryEnqueue(async () => await TryUnlockAsync());
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e) => await TryUnlockAsync();

    private async Task TryUnlockAsync()
    {
        if (HostWindow is null) return;
        try
        {
            if (await AppLockService.UnlockAsync(HostWindow))
            {
                _locked = false;
                LockOverlay.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            ShowMessage("Entsperren fehlgeschlagen: " + ex.Message, isError: true);
        }
    }

    /// <summary>Fenster aktiviert/deaktiviert: nach 5/15/60 Minuten im Hintergrund sperren.</summary>
    public void OnWindowActivationChanged(bool active)
    {
        if (!active)
        {
            _deactivatedAt ??= DateTimeOffset.Now;
            return;
        }
        if (_deactivatedAt is { } since && !_locked && _s.Settings.AppLock
            && DateTimeOffset.Now - since >= TimeSpan.FromMinutes(Math.Max(1, _s.Settings.AppLockMinutes)))
            Lock(promptNow: true);
        _deactivatedAt = null;
    }

    // ======================================================= Dialoge

    private bool _addAccountOpen;
    private SettingsWindow? _settingsWindow;

    private async void OnAddAccountClick(object sender, RoutedEventArgs e) => await ShowAddAccountAsync();

    /// <summary>Assistent „Konto hinzufügen“ (§17).</summary>
    public async Task ShowAddAccountAsync()
    {
        if (_addAccountOpen || XamlRoot is null) return;
        _addAccountOpen = true;
        try
        {
            var dialog = new AddAccountDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
            await dialog.ShowAsync();
            if (dialog.AddedAccount is { } account)
            {
                await Vm.RefreshAllAsync();
                var inbox = Vm.Sidebar.FirstOrDefault(i => i.Account?.Id == account.Id && i.Target == SidebarTarget.Category);
                if (inbox is not null) await Vm.SelectAsync(inbox);
                UpdateListHeader();
            }
        }
        finally
        {
            _addAccountOpen = false;
        }
    }

    /// <summary>Einstellungen als eigenes Fenster (§19).</summary>
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        var window = new SettingsWindow(Vm.Accounts, Vm);
        window.Page.AppearanceChanged += (_, _) =>
        {
            if (HostWindow is not null) ThemeService.ApplyMode(HostWindow, _s.Settings);
            RenderCurrent();
        };
        window.Page.DataChanged += (_, _) => Vm.RebuildSidebar();
        window.Closed += async (_, _) =>
        {
            _settingsWindow = null;
            await ApplySettingsAsync(window.Page.AccountsChanged);
        };
        _settingsWindow = window;
        window.Activate();
    }

    private async Task ApplySettingsAsync(bool accountsChanged)
    {
        StartRefreshTimer();
        _s.Mail.Cache = _s.Settings.OfflineCache ? _s.Cache : null;
        ThemeService.ApplyPalette(_s.Theme, _s.Settings);
        if (HostWindow is not null) ThemeService.ApplyMode(HostWindow, _s.Settings);
        if (accountsChanged)
        {
            ClearReader();
            await Vm.RefreshAllAsync();
        }
        else
        {
            Vm.RebuildSidebar();
            Vm.ApplyRulesLocally();
            await Vm.SelectAsync(Vm.Selected ?? Vm.Sidebar.First(i => i.Key == "all"));
        }
        RenderCurrent();
        UpdateListHeader();
    }

    // ======================================================== Updates

    private void UpdateUpdateBar()
    {
        var updates = App.Updates;
        var info = updates.Update;
        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdatePrimaryButton.IsEnabled = true;
        UpdateNotesButton.Visibility = info?.HtmlUrl is null ? Visibility.Collapsed : Visibility.Visible;

        switch (updates.State)
        {
            case UpdateState.Ready when info is not null:
                UpdateBar.Severity = InfoBarSeverity.Success;
                UpdateBar.Title = $"Lu-Mail {info.Version} ist bereit.";
                UpdateBar.Message = "Das Update wird beim Beenden automatisch installiert.";
                UpdatePrimaryButton.Content = "Jetzt neu starten";
                UpdatePrimaryButton.Visibility = Visibility.Visible;
                UpdateBar.IsOpen = true;
                break;
            case UpdateState.Available when info is not null:
                UpdateBar.Severity = InfoBarSeverity.Informational;
                UpdateBar.Title = $"Lu-Mail {info.Version} ist verfügbar.";
                UpdateBar.Message = $"Installiert ist Version {updates.CurrentVersion}.";
                UpdatePrimaryButton.Content = "Herunterladen und neu starten";
                UpdatePrimaryButton.Visibility = Visibility.Visible;
                UpdateBar.IsOpen = true;
                break;
            case UpdateState.Downloading when info is not null && updates.IsInteractive:
                UpdateBar.Severity = InfoBarSeverity.Informational;
                UpdateBar.Title = $"Lu-Mail {info.Version} wird heruntergeladen …";
                UpdateBar.Message = $"{updates.Progress:P0}";
                UpdateProgress.Value = updates.Progress;
                UpdateProgress.Visibility = Visibility.Visible;
                UpdatePrimaryButton.IsEnabled = false;
                UpdateBar.IsOpen = true;
                break;
            case UpdateState.Failed when updates.IsInteractive && info is not null:
                UpdateBar.Severity = InfoBarSeverity.Error;
                UpdateBar.Title = "Update fehlgeschlagen";
                UpdateBar.Message = updates.LastError ?? "";
                UpdatePrimaryButton.Content = "Erneut versuchen";
                UpdatePrimaryButton.Visibility = Visibility.Visible;
                UpdateBar.IsOpen = true;
                break;
        }
    }

    private async void OnUpdatePrimaryClick(object sender, RoutedEventArgs e)
    {
        var updates = App.Updates;
        if (updates.State == UpdateState.Ready) updates.InstallAndRestart();
        else await updates.DownloadAndRestartAsync();
    }

    private void OnUpdateNotesClick(object sender, RoutedEventArgs e) => App.Updates.OpenReleaseNotes();

    // ===================================================== Meldungen

    /// <summary>Hinweis unten rechts. Ohne <paramref name="severity"/>: Fehler bzw. Erfolg.</summary>
    public void ShowMessage(string message, bool isError, string? actionText = null, Action? action = null, InfoBarSeverity? severity = null)
    {
        var kind = severity ?? (isError ? InfoBarSeverity.Error : InfoBarSeverity.Success);
        MessageBar.Severity = kind;
        MessageBar.Title = kind switch
        {
            InfoBarSeverity.Error => "Fehler",
            InfoBarSeverity.Warning => "Hinweis",
            _ => "",
        };
        MessageBar.Message = message;
        if (actionText is not null && action is not null)
        {
            var button = new Button { Content = actionText };
            button.Click += (_, _) => action();
            MessageBar.ActionButton = button;
        }
        else
        {
            MessageBar.ActionButton = null;
        }
        MessageBar.IsOpen = true;
    }
}
