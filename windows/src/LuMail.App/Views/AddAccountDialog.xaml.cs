using LuMail.App.ViewModels;
using LuMail.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuMail.App.Views;

/// <summary>
/// „Konto hinzufügen“: Anbieter wählen, optional E-Mail-Adresse eingeben, „Im Browser anmelden“.
/// OAuth, wenn oauth.json eine Client-ID enthält, sonst Passwort-Seite im Browser.
/// </summary>
public sealed partial class AddAccountDialog : ContentDialog
{
    private readonly List<ProviderTile> _tiles;
    private CancellationTokenSource? _cts;

    /// <summary>Das neu hinzugefügte Konto (null = abgebrochen).</summary>
    public Account? AddedAccount { get; private set; }

    public AddAccountDialog()
    {
        InitializeComponent();
        var services = App.Services;
        _tiles = services.Providers.Providers.Select(p => new ProviderTile(p, services.Setup.CanUseOAuth(p))).ToList();
        ProviderGrid.ItemsSource = _tiles;
        Closing += OnClosing;
    }

    private ProviderTile? SelectedTile => ProviderGrid.SelectedItem as ProviderTile;

    private void OnProviderSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHint();

    /// <summary>Anbieter anhand der eingegebenen Adresse vorauswählen.</summary>
    private void OnEmailChanged(object sender, TextChangedEventArgs e)
    {
        var provider = App.Services.Providers.FindByEmailDomain(EmailBox.Text);
        if (provider is not null && SelectedTile?.Provider.Id != provider.Id)
            ProviderGrid.SelectedItem = _tiles.FirstOrDefault(t => t.Provider.Id == provider.Id);
    }

    private void UpdateHint()
    {
        var tile = SelectedTile;
        LoginButton.IsEnabled = tile is not null;
        ErrorBar.IsOpen = false;
        if (tile is null)
        {
            HintBar.IsOpen = false;
            return;
        }

        var provider = tile.Provider;
        if (provider.SupportsOAuth && !tile.OAuthAvailable)
        {
            HintBar.Title = "OAuth nicht eingerichtet";
            HintBar.Message =
                $"Für {provider.Name} ist keine eigene OAuth-Client-ID hinterlegt ({App.Services.Paths.OAuthFile}). " +
                $"Du meldest dich deshalb im Browser mit deinem {provider.PasswordLabel} an. {provider.PasswordHelp}";
        }
        else if (tile.OAuthAvailable)
        {
            HintBar.Title = "Anmeldung über OAuth";
            HintBar.Message = $"Du meldest dich direkt bei {provider.Name} an. Lu-Mail erhält nur ein Zugriffstoken, nie dein Passwort.";
        }
        else
        {
            HintBar.Title = provider.PasswordLabel;
            HintBar.Message = provider.PasswordHelp;
        }
        HintBar.IsOpen = true;
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        var tile = SelectedTile;
        if (tile is null) return;

        _cts = new CancellationTokenSource();
        ShowWaiting(true, tile.OAuthAvailable
            ? $"Melde dich im geöffneten Browserfenster bei {tile.Name} an und erlaube den Zugriff für Lu-Mail."
            : $"Gib im geöffneten Browserfenster deine E-Mail-Adresse und dein {tile.Provider.PasswordLabel} ein.");
        try
        {
            AddedAccount = await App.Services.Setup.AddAccountAsync(tile.Provider, EmailBox.Text, _cts.Token);
            Hide();
        }
        catch (OperationCanceledException)
        {
            ShowWaiting(false, "");
        }
        catch (Exception ex)
        {
            ShowWaiting(false, "");
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnCancelWaitingClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args) => _cts?.Cancel();

    private void ShowWaiting(bool waiting, string detail)
    {
        ChoosePanel.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
        WaitingPanel.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        WaitingDetail.Text = detail;
    }
}
