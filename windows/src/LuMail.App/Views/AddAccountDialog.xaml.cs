using LuMail.App.Services;
using LuMail.Core.Catalog;
using LuMail.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LuMail.App.Views;

/// <summary>
/// Assistent „Konto hinzufügen“ (FUNKTIONEN §17): Schritt 1 E-Mail-Adresse oder Anbieter, Schritt 2 „So geht's“
/// und Anmeldung im Browser. Der Haupt-Button sitzt in der Fußzeile des Dialogs und ist immer sichtbar.
/// </summary>
public sealed partial class AddAccountDialog : ContentDialog
{
    private enum Step { Email, Provider, Waiting, Success }

    private readonly AppServices _s = App.Services;
    private Step _step = Step.Email;
    private Provider? _provider;
    private CustomServerSettings? _servers;
    private CancellationTokenSource? _cts;
    private bool _discovering;
    /// <summary>
    /// Vom Standardweg abgewichen (Link unter der Anleitung): Standard ist die Web-Anmeldung, bei Anbietern mit
    /// <see cref="Provider.PrefersImap"/> (§20, Hostinger) dagegen IMAP mit dem normalen Passwort.
    /// </summary>
    private bool _alternateMode;

    /// <summary>Das neu hinzugefügte Konto (null = abgebrochen).</summary>
    public Account? AddedAccount { get; private set; }

    public AddAccountDialog()
    {
        InitializeComponent();
        BuildProviderGrid();
        Closing += (_, _) => _cts?.Cancel();
        Opened += (_, _) => EmailBox.Focus(FocusState.Programmatic);
        ShowStep(Step.Email);
    }

    // ------------------------------------------------------------ Schritt 1

    /// <summary>Kompakte Anbieter-Kacheln (3 Spalten, alle ohne Scrollen sichtbar).</summary>
    private void BuildProviderGrid()
    {
        const int columns = 3;
        var providers = _s.Providers.Providers;
        for (var c = 0; c < columns; c++) ProviderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < (providers.Count + columns - 1) / columns; r++) ProviderGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });

        for (var i = 0; i < providers.Count; i++)
        {
            var provider = providers[i];
            var tile = new Grid { Background = BrushCache.ForProvider(provider), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 0, 8, 0) };
            tile.Children.Add(new TextBlock
            {
                Text = provider.Name,
                Foreground = BrushCache.FromHex(IsLight(provider.Color) ? "#1B1A33" : "#FFFFFF"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var button = new Button
            {
                Content = tile,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                BorderThickness = new Thickness(0),
                Tag = provider,
            };
            ToolTipService.SetToolTip(button, provider.Name);
            button.Click += (s, _) =>
            {
                if ((s as Button)?.Tag is not Provider p) return;
                _alternateMode = false;
                ShowProvider(p, null);
            };
            Grid.SetRow(button, i / columns);
            Grid.SetColumn(button, i % columns);
            ProviderGrid.Children.Add(button);
        }
    }

    /// <summary>Helle Markenfarben (z. B. WEB.DE-Gelb) bekommen dunkle Schrift.</summary>
    private static bool IsLight(string hex)
    {
        var (_, r, g, b) = ThemeCatalog.ParseHex(hex);
        return 0.299 * r + 0.587 * g + 0.114 * b > 170;
    }

    private string Email => EmailBox.Text.Trim();
    private bool EmailLooksValid => Email.Length > 3 && Email.Contains('@') && EmailParts.GetDomain(Email).Contains('.');

    private void OnEmailChanged(object sender, TextChangedEventArgs e) => UpdateButtons();

    private async void OnEmailKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        if (EmailLooksValid) await DiscoverAsync();
    }

    private async Task DiscoverAsync()
    {
        if (_discovering || !EmailLooksValid) return;
        _discovering = true;
        DiscoveringText.Text = $"Suche Einstellungen für {EmailParts.GetDomain(Email)} …";
        DiscoveringPanel.Visibility = Visibility.Visible;
        UpdateButtons();
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _s.Discovery.DiscoverAsync(Email, _cts.Token);
            _alternateMode = false;
            ShowProvider(result.Provider, result.Servers);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _discovering = false;
            DiscoveringPanel.Visibility = Visibility.Collapsed;
            UpdateButtons();
        }
    }

    // ------------------------------------------------------------ Schritt 2

    /// <summary>Web-Anmeldung (§20) – Standard für alle Anbieter mit <c>web</c>, außer wo das normale Passwort genügt.</summary>
    private bool WebMode => _provider is { SupportsWeb: true } provider && (provider.PrefersImap ? _alternateMode : !_alternateMode);
    private bool OAuthAvailable => _provider is not null && _s.Setup.CanUseOAuth(_provider);
    private bool OAuthOnlyWithoutClient => !WebMode && _provider is { SupportsPassword: false } && !OAuthAvailable;

    private string LoginButtonText
    {
        get
        {
            if (_provider is null) return "Im Browser anmelden";
            if (WebMode) return $"Bei {_provider.Name} anmelden";
            if (!OAuthAvailable && !OAuthOnlyWithoutClient) return "Im Browser anmelden";
            return _provider.Id switch
            {
                "gmail" => "Mit Google anmelden",
                "outlook" => "Mit Microsoft anmelden",
                "mailru" => "Mit Mail.ru anmelden",
                _ => $"Mit {_provider.Name} anmelden",
            };
        }
    }

    private void ShowProvider(Provider provider, CustomServerSettings? servers)
    {
        _provider = provider;
        _servers = servers;
        ProviderBadge.Background = BrushCache.ForProvider(provider);
        ProviderInitial.Text = provider.Name.Length > 0 ? provider.Name[..1] : "?";
        ProviderInitial.Foreground = BrushCache.FromHex(IsLight(provider.Color) ? "#1B1A33" : "#FFFFFF");
        ProviderNameText.Text = provider.Name;
        ProviderEmailText.Text = EmailLooksValid ? Email : "";
        ProviderEmailText.Visibility = EmailLooksValid ? Visibility.Visible : Visibility.Collapsed;

        BuildSteps(provider);
        GuidePanel.Visibility = WebMode ? Visibility.Collapsed : Visibility.Visible;
        WebModePanel.Visibility = WebMode ? Visibility.Visible : Visibility.Collapsed;
        WebModeDetail.Text = $"Lu-Mail öffnet die echte Anmeldeseite von {provider.Name} in einem eigenen Fenster – auch mit " +
                             "Bestätigung in zwei Schritten. Lu-Mail speichert kein Passwort, nur die Anmeldung (Cookies) dieses Kontos.";
        ModeLink.Visibility = provider.SupportsWeb ? Visibility.Visible : Visibility.Collapsed;
        ModeLink.Content = provider.PrefersImap
            ? (WebMode ? "Direkt in Lu-Mail verbinden (normales Passwort)" : "Stattdessen auf der Webseite anmelden")
            : (WebMode ? "Erweitert: per IMAP verbinden (App-Passwort/OAuth)" : "Einfacher: auf der Webseite anmelden");

        ServersText.Visibility = provider.CustomServers && !WebMode ? Visibility.Visible : Visibility.Collapsed;
        ServersText.Text = servers is null
            ? "Die Server trägst du auf der Anmeldeseite im Browser ein."
            : $"Gefundene Server: {Describe(servers.Imap)} (IMAP) · {Describe(servers.Smtp)} (SMTP) – auf der Anmeldeseite änderbar.";

        HintBar.IsOpen = false;
        if (OAuthOnlyWithoutClient)
        {
            HintBar.Severity = InfoBarSeverity.Warning;
            HintBar.Title = "Anmeldung derzeit nicht möglich";
            HintBar.Message = $"{provider.Name} erlaubt nur die Anmeldung über OAuth, und diese Lu-Mail-Version enthält keine Client-ID dafür. " +
                              $"Du kannst eine eigene Client-ID für „{provider.Id}“ in {_s.Paths.OAuthFile} eintragen.";
            HintBar.IsOpen = true;
        }

        BridgePanel.Visibility = provider.Setup.CheckLocalPort is null || WebMode ? Visibility.Collapsed : Visibility.Visible;
        if (provider.Setup.CheckLocalPort is { } port && !WebMode) _ = CheckBridgeAsync(port);

        ShowStep(Step.Provider);
    }

    private static string Describe(ServerEndpoint e) =>
        $"{e.Host}:{e.Port} ({(e.Security == "starttls" ? "STARTTLS" : "SSL/TLS")})";

    private void BuildSteps(Provider provider)
    {
        StepsPanel.Children.Clear();
        var steps = OAuthAvailable && provider.Setup.OAuthSteps.Count > 0 ? provider.Setup.OAuthSteps : provider.Setup.PasswordSteps;
        if (steps.Count == 0) steps = new List<SetupStep> { new() { Text = $"Auf „{LoginButtonText}“ klicken." } };

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var number = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Top,
                Background = BrushCache.ForProvider(provider),
            };
            number.Child = new TextBlock
            {
                Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = BrushCache.FromHex(IsLight(provider.Color) ? "#1B1A33" : "#FFFFFF"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(number);

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock { Text = step.Text, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(step.Url) && Uri.TryCreate(step.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                var link = new Button { Content = (step.Button ?? "Öffnen") + "  ↗", Padding = new Thickness(10, 3, 10, 4), Tag = uri };
                link.Click += (s, _) =>
                {
                    if ((s as Button)?.Tag is Uri u) AppServices.OpenInBrowser(u);
                };
                content.Children.Add(link);
            }
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            StepsPanel.Children.Add(row);
        }
    }

    private async Task CheckBridgeAsync(int port)
    {
        BridgeText.Text = "Prüfe Proton Mail Bridge …";
        BridgeText.Foreground = BrushCache.FromHex(Palette("textSecondary"));
        var running = await LocalPortCheck.IsOpenAsync(port);
        BridgeText.Text = running ? "✓ Bridge läuft" : "✗ Bridge nicht gefunden – bitte starten";
        BridgeText.Foreground = BrushCache.FromHex(Palette(running ? "success" : "danger"));
    }

    private string Palette(string token) =>
        _s.Theme.Palette(_s.Settings.ThemeVariant, ActualTheme == ElementTheme.Dark, _s.Settings.Accent).TryGetValue(token, out var hex) ? hex : "#8B7CFF";

    private async void OnRecheckBridgeClick(object sender, RoutedEventArgs e)
    {
        if (_provider?.Setup.CheckLocalPort is { } port) await CheckBridgeAsync(port);
    }

    // ------------------------------------------------------------ Anmelden

    private void OnModeLinkClick(object sender, RoutedEventArgs e)
    {
        if (_provider is null) return;
        _alternateMode = !_alternateMode;
        ShowProvider(_provider, _servers);
    }

    /// <summary>§20: Anmeldung auf der Webseite in einem eigenen Fenster mit dem Profil des neuen Kontos.</summary>
    private async Task WebLoginAsync(Provider provider)
    {
        var accountId = Guid.NewGuid().ToString().ToUpperInvariant();
        _cts = new CancellationTokenSource();
        WaitingDetail.Text = $"Melde dich im geöffneten Fenster bei {provider.Name} an. Sobald du dein Postfach siehst, geht es automatisch weiter (oder klicke dort auf „Fertig“).";
        ShowStep(Step.Waiting);
        var window = new WebLoginWindow(provider, accountId);
        using var registration = _cts.Token.Register(window.Cancel);
        window.Activate();
        try
        {
            if (await window.Completion)
            {
                AddedAccount = _s.Setup.AddWebAccount(provider, accountId, EmailLooksValid ? Email : null);
                SuccessText.Text = $"✓ {AddedAccount.Email} wurde hinzugefügt";
                ShowStep(Step.Success);
                await Task.Delay(1500);
                Hide();
            }
            else if (_step == Step.Waiting)
            {
                ShowStep(Step.Provider);
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task LoginAsync()
    {
        if (_provider is null || OAuthOnlyWithoutClient) return;
        if (WebMode)
        {
            await WebLoginAsync(_provider);
            return;
        }
        var provider = _provider;
        _cts = new CancellationTokenSource();
        WaitingDetail.Text = OAuthAvailable
            ? $"Melde dich im geöffneten Browserfenster bei {provider.Name} an und erlaube den Zugriff für Lu-Mail."
            : $"Gib im geöffneten Browserfenster deine E-Mail-Adresse und dein {provider.PasswordLabel} ein.";
        ShowStep(Step.Waiting);
        try
        {
            AddedAccount = await _s.Setup.AddAccountAsync(provider, EmailLooksValid ? Email : null, _cts.Token, _servers);
            // Gab es dieselbe Adresse schon als Web-Konto, ersetzt das IMAP-Konto es (kein doppelter Eintrag,
            // Cookies des Web-Kontos werden gelöscht).
            if (App.Web.FindWebTwin(AddedAccount) is { } twin) await App.Web.ReplaceWithImapAsync(twin, AddedAccount);
            SuccessText.Text = $"✓ {AddedAccount.Email} wurde hinzugefügt";
            ShowStep(Step.Success);
            await Task.Delay(1500);
            Hide();
        }
        catch (OperationCanceledException)
        {
            if (_step == Step.Waiting) ShowStep(Step.Provider);
        }
        catch (Exception ex)
        {
            HintBar.Severity = InfoBarSeverity.Error;
            HintBar.Title = "Anmeldung fehlgeschlagen";
            HintBar.Message = Dialogs.LoginErrorText(ex);
            HintBar.IsOpen = true;
            ShowStep(Step.Provider, retry: true);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnCancelWaitingClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ------------------------------------------------------------ Fußzeile

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Dialog bleibt offen; wir steuern die Schritte selbst.
        switch (_step)
        {
            case Step.Email:
                await DiscoverAsync();
                break;
            case Step.Provider:
                await LoginAsync();
                break;
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_step == Step.Provider) ShowStep(Step.Email);
    }

    private bool _retry;

    private void ShowStep(Step step, bool retry = false)
    {
        _step = step;
        _retry = retry;
        Step1Panel.Visibility = step == Step.Email ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == Step.Provider ? Visibility.Visible : Visibility.Collapsed;
        WaitingPanel.Visibility = step == Step.Waiting ? Visibility.Visible : Visibility.Collapsed;
        SuccessPanel.Visibility = step == Step.Success ? Visibility.Visible : Visibility.Collapsed;
        if (step == Step.Email) EmailBox.Focus(FocusState.Programmatic);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        switch (_step)
        {
            case Step.Email:
                PrimaryButtonText = "Weiter";
                IsPrimaryButtonEnabled = EmailLooksValid && !_discovering;
                SecondaryButtonText = "";
                CloseButtonText = "Abbrechen";
                break;
            case Step.Provider:
                PrimaryButtonText = _retry ? "Erneut versuchen" : LoginButtonText;
                IsPrimaryButtonEnabled = !OAuthOnlyWithoutClient;
                SecondaryButtonText = "Zurück";
                CloseButtonText = "Abbrechen";
                break;
            case Step.Waiting:
                PrimaryButtonText = "";
                SecondaryButtonText = "";
                CloseButtonText = "Abbrechen";
                break;
            case Step.Success:
                PrimaryButtonText = "";
                SecondaryButtonText = "";
                CloseButtonText = "Schließen";
                break;
        }
    }
}
