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
    private bool _initialized;

    // Leseansicht
    private MessageItem? _current;
    private MessageDetail? _currentDetail;
    private bool _externalAllowed;
    private bool _webViewReady;
    private bool _navigatingInternally;
    private int _openVersion;

    // Auswahl in der Nachrichtenliste
    private string? _selectedKey;
    private bool _restoringSelection;

    public MainPage()
    {
        Vm = new MainViewModel(App.Services);
        InitializeComponent();
        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => RenderCurrent();
    }

    public MainViewModel Vm { get; }

    /// <summary>Das Fenster, in dem diese Seite angezeigt wird.</summary>
    public Window? HostWindow { get; set; }

    /// <summary>Element, das als Titelleiste (Ziehbereich) dient.</summary>
    public UIElement TitleBarElement => AppTitleBar;

    /// <summary>Mit Mica/Acrylic: Sidebar und Titelleiste durchscheinend (sonst deckend LuSidebar aus dem XAML).</summary>
    public void UseTransparentBackground() => RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    // ================================================================ Start

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        SidebarList.ItemsSource = Vm.Sidebar;
        MessageList.ItemsSource = Vm.Messages;
        Vm.PropertyChanged += OnViewModelPropertyChanged;
        Vm.MessagesReset += OnMessagesReset;
        Vm.ErrorOccurred += (_, message) => ShowMessage(message, isError: true);

        StartRefreshTimer();
        App.Updates.StateChanged += (_, _) => UpdateUpdateBar();
        App.Updates.Start(DispatcherQueue);
        UpdateUpdateBar();
        await InitWebViewAsync();
        UpdateListHeader();
        await Vm.RefreshAllAsync();
        UpdateListHeader();
    }

    private void StartRefreshTimer()
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsBusy):
                BusyBar.Visibility = Vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
                break;
            default:
                UpdateListHeader();
                break;
        }
    }

    private void UpdateListHeader()
    {
        ListTitleText.Text = Vm.ListTitle;
        ListSubtitleText.Text = Vm.ListSubtitle;
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

    private void OnMessagesReset(object? sender, EventArgs e)
    {
        UpdateListHeader();
        if (_selectedKey is null) return;
        var match = Vm.Messages.FirstOrDefault(m => m.Key == _selectedKey);
        if (match is not null) _current = match; // nach Aktualisierung neue Instanz übernehmen
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
    }

    private async void OnMessageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm.IsResetting || _restoringSelection) return;
        if (MessageList.SelectedItem is not MessageItem item)
        {
            ClearReader();
            return;
        }
        if (item.Key == _selectedKey && _currentDetail is not null && ReferenceEquals(item, _current)) return;
        _selectedKey = item.Key;
        await OpenMessageAsync(item);
    }

    // ======================================================== Leseansicht

    private async Task InitWebViewAsync()
    {
        try
        {
            await ReaderView.EnsureCoreWebView2Async();
            var core = ReaderView.CoreWebView2;
            core.Settings.IsScriptEnabled = false;            // kein JavaScript in Mails
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = true;          // Linkziel beim Überfahren anzeigen
            core.NavigationStarting += OnReaderNavigationStarting;
            core.NewWindowRequested += OnReaderNewWindowRequested;
            // Zusätzlich zur CSP: alle http(s)-Anfragen blockieren, solange externe Inhalte nicht erlaubt sind.
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
        // Links nie in der Web-Ansicht öffnen, sondern (nur auf Klick) im Standardbrowser.
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
            OpenCompose(new OutgoingMessage
            {
                To = address,
                Subject = query.TryGetValue("subject", out var subject) ? subject : "",
                Body = query.TryGetValue("body", out var body) ? body : "",
            }, _current?.Account);
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
        _externalAllowed = _s.Settings.AlwaysLoadExternalImages;
        ShowReaderHeader(item, null);
        AttachmentsPanel.Children.Clear();
        AttachmentsScroller.Visibility = Visibility.Collapsed;
        ExternalBanner.Visibility = Visibility.Collapsed;
        ReaderLoading.IsActive = true;
        try
        {
            var detail = await Vm.OpenAsync(item);
            if (version != _openVersion) return;
            _currentDetail = detail;
            ShowReaderHeader(item, detail);
            ShowAttachments(detail);
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

        ReaderSubject.Text = item.Subject;
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

    private static string FormatSize(long bytes) => bytes switch
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

    /// <summary>Zeigt die aktuelle Nachricht (mit CSP) in der Web-Ansicht.</summary>
    private void RenderCurrent()
    {
        if (_currentDetail is null || !_webViewReady) return;
        var theme = _s.Theme;
        var tokens = ActualTheme == ElementTheme.Dark ? theme.Dark : theme.Light;
        var style = new PlainTextStyle(tokens["surface"], tokens["textPrimary"], tokens["accent"]);
        var html = HtmlPreparer.Prepare(_currentDetail, _externalAllowed, style);

        ExternalBanner.Visibility = !_externalAllowed && HtmlPreparer.HasExternalContent(_currentDetail.HtmlBody)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _navigatingInternally = true;
        if (html.Length < 1_500_000)
        {
            ReaderView.CoreWebView2.NavigateToString(html);
        }
        else
        {
            // NavigateToString ist auf ca. 2 MB begrenzt → große Mails über eine temporäre Datei anzeigen.
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuMail", "Cache");
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

    private async Task RunActionAsync(Func<MessageItem, Task> action, bool removesMessage)
    {
        var item = _current;
        if (item is null) return;
        var index = Vm.Messages.IndexOf(item);
        try
        {
            await action(item);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, isError: true);
            return;
        }

        if (removesMessage)
        {
            ClearReader();
            if (index >= 0 && Vm.Messages.Count > 0) MessageList.SelectedIndex = Math.Min(index, Vm.Messages.Count - 1);
        }
        else
        {
            UpdateActionButtons();
        }
        UpdateListHeader();
    }

    private async void OnArchiveClick(object sender, RoutedEventArgs e) =>
        await RunActionAsync(item => Vm.MoveAsync(item, FolderRole.Archive), removesMessage: true);

    private async void OnDeleteClick(object sender, RoutedEventArgs e) =>
        await RunActionAsync(item => Vm.DeleteAsync(item), removesMessage: true);

    private async void OnSpamClick(object sender, RoutedEventArgs e) =>
        await RunActionAsync(item => Vm.MoveAsync(item, Vm.RoleOf(item) == FolderRole.Spam ? FolderRole.Inbox : FolderRole.Spam),
            removesMessage: true);

    private async void OnFlagClick(object sender, RoutedEventArgs e) =>
        await RunActionAsync(item => Vm.SetFlaggedAsync(item, !item.Flagged), removesMessage: false);

    private async void OnSeenClick(object sender, RoutedEventArgs e) =>
        await RunActionAsync(item => Vm.SetSeenAsync(item, !item.Seen), removesMessage: false);

    private void OnSortFlyoutOpening(object sender, object e)
    {
        SortFlyout.Items.Clear();
        var item = _current;
        if (item is null || string.IsNullOrWhiteSpace(item.Summary.FromAddress)) return;

        foreach (var category in MessageCategories.All)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = $"Absender immer als „{_s.Categories.DisplayName(category)}“ einsortieren",
                Tag = category.ToId(),
            };
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
        if (_current is null || (sender as MenuFlyoutItem)?.Tag is not string id || !MessageCategories.TryParse(id, out var category)) return;
        Vm.SetSenderRule(_current, category);
        ShowMessage($"Nachrichten von {_current.Summary.FromAddress} werden ab jetzt als „{_s.Categories.DisplayName(category)}“ einsortiert.", isError: false);
    }

    // ======================================================== Verfassen

    private Account? DefaultAccount() => _current?.Account ?? Vm.Selected?.Account ?? Vm.Accounts.FirstOrDefault();

    private void OpenCompose(OutgoingMessage? draft = null, Account? from = null)
    {
        if (Vm.Accounts.Count == 0)
        {
            ShowMessage("Bitte zuerst ein Konto hinzufügen.", isError: false);
            return;
        }
        var window = new ComposeWindow();
        window.Initialize(Vm.Accounts, from ?? DefaultAccount(), draft);
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
        if (_current is null || _currentDetail is null) return;
        OpenCompose(MessageComposer.CreateReply(_currentDetail, _current.Account, replyAll), _current.Account);
    }

    private void OnForwardClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || _currentDetail is null) return;
        OpenCompose(MessageComposer.CreateForward(_currentDetail), _current.Account);
    }

    // ===================================================== Aktualisieren

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await Vm.RefreshAllAsync();

    private async void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await Vm.RefreshAllAsync();
    }

    // ======================================================= Dialoge

    private async void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAccountDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        if (dialog.AddedAccount is { } account)
        {
            ShowMessage($"{account.Email} wurde hinzugefügt.", isError: false);
            await Vm.RefreshAllAsync();
            var inbox = Vm.Sidebar.FirstOrDefault(i => i.Account?.Id == account.Id && i.Target == SidebarTarget.Category);
            if (inbox is not null) await Vm.SelectAsync(inbox);
            UpdateListHeader();
        }
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { XamlRoot = XamlRoot };
        dialog.Initialize(Vm.Accounts);
        await dialog.ShowAsync();

        StartRefreshTimer();
        if (dialog.AccountsChanged)
        {
            ClearReader();
            await Vm.RefreshAllAsync();
        }
        else
        {
            Vm.RebuildSidebar();
            await Vm.SelectAsync(Vm.Selected ?? Vm.Sidebar.First(i => i.Key == "all"));
        }
        UpdateListHeader();
    }

    // ======================================================== Updates

    /// <summary>Zeigt den Update-Hinweis passend zum Zustand des Updaters.</summary>
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

            default:
                // Hintergrundprüfung/-download: nichts anzeigen.
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

    public void ShowMessage(string message, bool isError, string? actionText = null, Action? action = null)
    {
        MessageBar.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        MessageBar.Title = isError ? "Fehler" : "";
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
