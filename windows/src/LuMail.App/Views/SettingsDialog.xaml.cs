using LuMail.App.Services;
using LuMail.App.ViewModels;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using LuMail.Core.Storage;
using LuMail.Core.Update;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace LuMail.App.Views;

/// <summary>Einstellungen: Allgemein, Konten (Signatur), Regeln, Darstellung, Datenschutz, Updates.</summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private static readonly int[] Intervals = { 1, 3, 5, 10, 15, 30, 60 };
    private static readonly (string Id, string Label)[] CleanupCategories =
    {
        ("promotions", "Werbung"), ("social", "Soziale Netzwerke"), ("updates", "Updates"), ("forums", "Foren"),
    };
    private static readonly (string Hex, string Label)[] CategoryColors =
    {
        ("#22C55E", "Grün"), ("#3B82F6", "Blau"), ("#F59E0B", "Orange"), ("#EF4444", "Rot"), ("#A855F7", "Lila"), ("#14B8A6", "Türkis"), ("#EC4899", "Rosa"),
    };

    private readonly AppServices _s = App.Services;
    private MainViewModel? _vm;
    private bool _loading = true;

    /// <summary>True, wenn ein Konto entfernt wurde.</summary>
    public bool AccountsChanged { get; private set; }

    public SettingsDialog()
    {
        InitializeComponent();
        var settings = _s.Settings;

        foreach (var minutes in Intervals)
            IntervalBox.Items.Add(new ComboBoxItem { Content = minutes == 1 ? "Jede Minute" : $"Alle {minutes} Minuten", Tag = minutes });
        var current = Array.IndexOf(Intervals, settings.RefreshIntervalMinutes);
        IntervalBox.SelectedIndex = current >= 0 ? current : 1;
        NotificationsSwitch.IsOn = settings.Notifications;
        CategoriesSwitch.IsOn = settings.SeparateCategories;
        CacheSwitch.IsOn = settings.OfflineCache;
        ExternalSwitch.IsOn = settings.AlwaysLoadExternalImages;
        DataFolderText.Text = _s.Paths.BaseDirectory;
        UpdateCacheSize();
        BuildCleanup();

        // Darstellung
        foreach (var (id, label) in new[] { ("system", "Wie Windows"), ("light", "Hell"), ("dark", "Dunkel") })
            ModeBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        ModeBox.SelectedIndex = settings.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 };
        foreach (var v in _s.Theme.Variants) VariantBox.Items.Add(new ComboBoxItem { Content = v.Name, Tag = v.Id });
        VariantBox.SelectedIndex = Math.Max(0, _s.Theme.Variants.ToList().FindIndex(v => v.Id == settings.ThemeVariant));
        AccentBox.Items.Add(new ComboBoxItem { Content = "Wie Variante", Tag = "" });
        foreach (var a in _s.Theme.Accents) AccentBox.Items.Add(ColorItem(a.Name, a.Dark, a.Id));
        AccentBox.SelectedIndex = Math.Max(0, _s.Theme.Accents.ToList().FindIndex(a => a.Id == settings.Accent) + 1);
        VariantDescription.Text = _s.Theme.Variant(settings.ThemeVariant).Description;

        // Datenschutz
        LockSwitch.IsOn = settings.AppLock;
        foreach (var minutes in new[] { 5, 15, 60 })
            LockMinutesBox.Items.Add(new ComboBoxItem { Content = $"Nach {minutes} Minuten im Hintergrund sperren", Tag = minutes });
        LockMinutesBox.SelectedIndex = settings.AppLockMinutes switch { 15 => 1, 60 => 2, _ => 0 };
        LockMinutesBox.IsEnabled = settings.AppLock;
        LockHintText.Text = "Lu-Mail sperrt sich beim Start und nach der gewählten Zeit im Hintergrund. Entsperren mit Windows Hello oder PIN.";

        // Regeln
        foreach (var (id, label) in new[] { ("from", "Absender"), ("to", "Empfänger"), ("subject", "Betreff") })
            RuleFieldBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        RuleFieldBox.SelectedIndex = 2;
        foreach (var (hex, label) in CategoryColors) CategoryColorBox.Items.Add(ColorItem(label, hex, hex));
        CategoryColorBox.SelectedIndex = 0;

        // Updates
        var updates = App.Updates;
        VersionText.Text = $"Installierte Version: {updates.CurrentVersion} ({updates.PlatformKey})"
                           + (updates.IsDevBuild ? " – Entwicklerversion, keine automatische Prüfung" : "");
        foreach (var (mode, label) in new[] { (UpdateMode.Automatic, "Automatisch"), (UpdateMode.NotifyOnly, "Nur benachrichtigen"), (UpdateMode.Off, "Aus") })
            UpdateModeBox.Items.Add(new ComboBoxItem { Content = label, Tag = mode });
        UpdateModeBox.SelectedIndex = settings.Updates switch { UpdateMode.NotifyOnly => 1, UpdateMode.Off => 2, _ => 0 };

        _loading = false;
    }

    public void Initialize(IReadOnlyList<Account> accounts, MainViewModel? vm = null)
    {
        _vm = vm;
        BuildAccounts(accounts);
        BuildRules();
        _ = UpdateLockAvailabilityAsync();
    }

    private static ComboBoxItem ColorItem(string label, string hex, string tag)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = BrushCache.FromHex(hex), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = label });
        return new ComboBoxItem { Content = panel, Tag = tag };
    }

    private static Grid Row(params (UIElement Element, GridLength Width)[] cells)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = cells[i].Width });
            Grid.SetColumn((FrameworkElement)cells[i].Element, i);
            grid.Children.Add(cells[i].Element);
        }
        return grid;
    }

    private static readonly GridLength Auto = GridLength.Auto;
    private static readonly GridLength Star = new(1, GridUnitType.Star);

    // ------------------------------------------------------------ Allgemein

    private void BuildCleanup()
    {
        CleanupPanel.Children.Clear();
        foreach (var (id, label) in CleanupCategories)
        {
            var box = new ComboBox { MinWidth = 160, Tag = id };
            foreach (var days in CleanupSelector.AllowedDays)
                box.Items.Add(new ComboBoxItem { Content = days == 0 ? "Aus" : $"nach {days} Tagen", Tag = days });
            var currentDays = _s.Settings.CleanupDays.TryGetValue(id, out var d) ? d : 0;
            box.SelectedIndex = Math.Max(0, Array.IndexOf(CleanupSelector.AllowedDays, currentDays));
            box.SelectionChanged += (s, _) =>
            {
                if (_loading || s is not ComboBox { Tag: string cat } cb || (cb.SelectedItem as ComboBoxItem)?.Tag is not int value) return;
                if (value == 0) _s.Settings.CleanupDays.Remove(cat); else _s.Settings.CleanupDays[cat] = value;
                _s.SaveSettings();
            };
            CleanupPanel.Children.Add(Row((new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 160 }, Auto), (box, Auto)));
        }
    }

    private void UpdateCacheSize()
    {
        try { CacheSizeText.Text = $"Gespeicherte Nachrichten: {MainPage.FormatSize(_s.Cache.MessageBytes)} (max. 200 MB)"; }
        catch (IOException) { CacheSizeText.Text = ""; }
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || (IntervalBox.SelectedItem as ComboBoxItem)?.Tag is not int minutes) return;
        _s.Settings.RefreshIntervalMinutes = minutes;
        _s.SaveSettings();
    }

    private void OnNotificationsToggled(object sender, RoutedEventArgs e) => Save(() => _s.Settings.Notifications = NotificationsSwitch.IsOn);
    private void OnCategoriesToggled(object sender, RoutedEventArgs e) => Save(() => _s.Settings.SeparateCategories = CategoriesSwitch.IsOn);
    private void OnExternalToggled(object sender, RoutedEventArgs e) => Save(() => _s.Settings.AlwaysLoadExternalImages = ExternalSwitch.IsOn);
    private void OnCacheToggled(object sender, RoutedEventArgs e) => Save(() => _s.Settings.OfflineCache = CacheSwitch.IsOn);

    private void Save(Action change)
    {
        if (_loading) return;
        change();
        _s.SaveSettings();
    }

    private void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        _vm?.ClearCache();
        if (_vm is null) _s.Cache.Clear();
        UpdateCacheSize();
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e) => AppServices.OpenFolder(_s.Paths.BaseDirectory);

    // ---------------------------------------------------------------- Konten

    private void BuildAccounts(IReadOnlyList<Account> accounts)
    {
        AccountsPanel.Children.Clear();
        NoAccountsText.Visibility = accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var account in accounts)
        {
            var provider = _s.Providers.Find(account.ProviderId);
            var panel = new StackPanel { Spacing = 6 };

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = account.Email, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var servers = provider?.CustomServers == true && account.CustomServers is { } cs
                ? $" · {cs.Imap.Host}:{cs.Imap.Port} / {cs.Smtp.Host}:{cs.Smtp.Port}"
                : "";
            text.Children.Add(new TextBlock { Text = $"{provider?.Name ?? account.ProviderId} · {(account.IsOAuth ? "OAuth" : "Passwort")}{servers}", FontSize = 12, Opacity = 0.7 });
            var remove = new Button { Content = "Entfernen", Tag = account, VerticalAlignment = VerticalAlignment.Center };
            remove.Click += OnRemoveAccountClick;
            panel.Children.Add(Row(
                (new Ellipse { Width = 10, Height = 10, Fill = BrushCache.ForProvider(provider), VerticalAlignment = VerticalAlignment.Center }, Auto),
                (text, Star), (remove, Auto)));

            var signature = new TextBox
            {
                Header = "Signatur",
                Text = account.Signature ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 70,
                PlaceholderText = "z. B. Viele Grüße, Max",
                Tag = account,
            };
            signature.LostFocus += (s, _) =>
            {
                if (s is not TextBox { Tag: Account acc } box) return;
                var value = box.Text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
                acc.Signature = value.Length == 0 ? null : value;
                var stored = _s.Accounts.Load().FirstOrDefault(a => a.Id == acc.Id);
                if (stored is null) return;
                stored.Signature = acc.Signature;
                _s.Accounts.Upsert(stored);
            };
            panel.Children.Add(signature);
            AccountsPanel.Children.Add(panel);
        }
    }

    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Account account } button) return;
        if (!Equals(button.Content, "Wirklich entfernen?"))
        {
            button.Content = "Wirklich entfernen?";
            return;
        }
        button.IsEnabled = false;
        await _s.Setup.RemoveAccountAsync(account); // löscht auch Cache und Erinnerungen
        _s.Outbox.RemoveAccount(account.Id);
        AccountsChanged = true;
        BuildAccounts(_s.Accounts.Load());
    }

    // ---------------------------------------------------------------- Regeln

    private IEnumerable<(string Id, string Name)> AllCategories() =>
        MessageCategories.All.Select(c => (c.ToId(), _s.CategoryName(c.ToId())))
            .Concat(_s.Rules.CustomCategories.Select(c => (c.Id, c.Name)));

    private Button RemoveButton(Action action)
    {
        var b = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(b, "Entfernen");
        b.Click += (_, _) =>
        {
            action();
            BuildRules();
            _vm?.RebuildSidebar();
        };
        return b;
    }

    private void BuildRules()
    {
        SendersPanel.Children.Clear();
        foreach (var (sender, category) in _s.Rules.Senders.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = sender;
            SendersPanel.Children.Add(Row(
                (new TextBlock { Text = $"{sender} → {_s.CategoryName(category)}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, Star),
                (RemoveButton(() => _s.Rules.RemoveSender(key)), Auto)));
        }
        if (_s.Rules.Senders.Count == 0)
            SendersPanel.Children.Add(new TextBlock { Text = "Keine – im Lesebereich über „Einsortieren“ festlegen.", FontSize = 12, Opacity = 0.7 });

        RulesPanel.Children.Clear();
        var rules = _s.Rules.Rules;
        for (var i = 0; i < rules.Count; i++)
        {
            var index = i;
            var r = rules[i];
            var field = r.Field switch { "from" => "Absender", "to" => "Empfänger", _ => "Betreff" };
            RulesPanel.Children.Add(Row(
                (new TextBlock { Text = $"{i + 1}. {field} enthält „{r.Contains}“ → {_s.CategoryName(r.Category)}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, Star),
                (RemoveButton(() => _s.Rules.RemoveRuleAt(index)), Auto)));
        }

        CustomCategoriesPanel.Children.Clear();
        foreach (var c in _s.Rules.CustomCategories)
        {
            var id = c.Id;
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            label.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = BrushCache.FromHex(c.Color), VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(new TextBlock { Text = c.Name, VerticalAlignment = VerticalAlignment.Center });
            CustomCategoriesPanel.Children.Add(Row((label, Star), (RemoveButton(() => _s.Rules.RemoveCategory(id)), Auto)));
        }

        var selectedCategory = (RuleCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        RuleCategoryBox.Items.Clear();
        foreach (var (id, name) in AllCategories()) RuleCategoryBox.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        var idx = AllCategories().Select(c => c.Id).ToList().IndexOf(selectedCategory ?? "");
        RuleCategoryBox.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        RulesStatusText.Text = "";
        var field = (RuleFieldBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "subject";
        var category = (RuleCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(RuleContainsBox.Text) || category is null)
        {
            RulesStatusText.Text = "Bitte einen Suchtext und eine Kategorie angeben.";
            return;
        }
        _s.Rules.AddRule(new ContentRule { Field = field, Contains = RuleContainsBox.Text.Trim(), Category = category });
        RuleContainsBox.Text = "";
        BuildRules();
    }

    private void OnAddCategoryClick(object sender, RoutedEventArgs e)
    {
        RulesStatusText.Text = "";
        var name = CategoryNameBox.Text.Trim();
        if (name.Length == 0)
        {
            RulesStatusText.Text = "Bitte einen Namen für die Kategorie eingeben.";
            return;
        }
        var color = (CategoryColorBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "#22C55E";
        _s.Rules.AddCategory(name, color, "tag");
        CategoryNameBox.Text = "";
        BuildRules();
        _vm?.RebuildSidebar();
    }

    // ------------------------------------------------------------ Darstellung

    private void OnAppearanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.ThemeMode = (ModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";
        _s.Settings.ThemeVariant = (VariantBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "luna";
        _s.Settings.Accent = (AccentBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        _s.SaveSettings();
        VariantDescription.Text = _s.Theme.Variant(_s.Settings.ThemeVariant).Description;
        ThemeService.ApplyPalette(_s.Theme, _s.Settings);
        RequestedTheme = ThemeService.RequestedTheme(_s.Settings);
    }

    // ------------------------------------------------------------ Datenschutz

    private async Task UpdateLockAvailabilityAsync()
    {
        if (!await AppLockService.IsAvailableAsync())
        {
            LockHintText.Text = "Windows Hello ist auf diesem Gerät nicht eingerichtet – die App-Sperre ist daher nicht verfügbar.";
            LockSwitch.IsEnabled = LockSwitch.IsOn;
        }
    }

    private void OnLockToggled(object sender, RoutedEventArgs e)
    {
        LockMinutesBox.IsEnabled = LockSwitch.IsOn;
        Save(() => _s.Settings.AppLock = LockSwitch.IsOn);
    }

    private void OnLockMinutesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || (LockMinutesBox.SelectedItem as ComboBoxItem)?.Tag is not int minutes) return;
        _s.Settings.AppLockMinutes = minutes;
        _s.SaveSettings();
    }

    // ---------------------------------------------------------------- Updates

    private void OnUpdateModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || (UpdateModeBox.SelectedItem as ComboBoxItem)?.Tag is not UpdateMode mode) return;
        _s.Settings.Updates = mode;
        _s.SaveSettings();
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesRing.IsActive = true;
        UpdateStatusText.Text = "Suche nach Updates …";
        try
        {
            UpdateStatusText.Text = await App.Updates.CheckNowAsync();
        }
        finally
        {
            CheckUpdatesRing.IsActive = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
