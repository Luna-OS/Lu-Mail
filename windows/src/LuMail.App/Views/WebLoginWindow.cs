using LuMail.App.Services;
using LuMail.Core.Catalog;
using LuMail.Core.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace LuMail.App.Views;

/// <summary>
/// Anmeldung auf der echten Webseite des Anbieters (FUNKTIONEN §20) mit dem WebView2-Profil des neuen Kontos.
/// Fertig, sobald die Adresse zu <c>loggedInPrefixes</c> passt oder „Fertig“ geklickt wird; „Abbrechen“ löscht das Profil.
/// </summary>
public sealed class WebLoginWindow : Window
{
    private readonly Provider _provider;
    private readonly string _accountId;
    private readonly WebView2 _view = new();
    private readonly ProgressBar _progress = new() { IsIndeterminate = true };
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _finished;
    private bool _started;

    /// <summary>True = angemeldet (bzw. „Fertig“), false = abgebrochen.</summary>
    public Task<bool> Completion => _done.Task;

    public WebLoginWindow(Provider provider, string accountId)
    {
        _provider = provider;
        _accountId = accountId;
        Title = $"Bei {provider.Name} anmelden – Lu-Mail";

        var root = new Grid { Background = (Brush)Application.Current.Resources["LuTransparent"] };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Padding = new Thickness(16, 10, 12, 10), ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 12, Height = 12, Fill = BrushCache.ForProvider(provider), VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock
        {
            Text = $"Melde dich bei {provider.Name} an – ganz normal mit deinem Passwort.",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var cancel = new Button { Content = "Abbrechen" };
        cancel.Click += (_, _) => Finish(false);
        var done = new Button { Content = "Fertig", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        ToolTipService.SetToolTip(done, "Klicken, wenn du dein Postfach siehst");
        done.Click += (_, _) => Finish(true);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(cancel, 2);
        Grid.SetColumn(done, 3);
        header.Children.Add(dot);
        header.Children.Add(text);
        header.Children.Add(cancel);
        header.Children.Add(done);
        root.Children.Add(header);
        Grid.SetRow(_progress, 1);
        root.Children.Add(_progress);
        Grid.SetRow(_view, 2);
        root.Children.Add(_view);

        Content = root;
        if (MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop();
        WindowSizing.ResizeAndCenter(this, 1000, 760);
        ThemeService.ApplyMode(this, App.Services.Settings);
        root.Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) =>
        {
            if (!_finished) Finish(false);
        };
    }

    private async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        try
        {
            var (environment, options) = await App.Web.ProfileOptionsAsync(_accountId);
            await _view.EnsureCoreWebView2Async(environment, options);
            var core = _view.CoreWebView2;
            await WebAccountService.InstallCaptureAsync(core);
            core.SourceChanged += (_, _) => Check();
            core.HistoryChanged += (_, _) => Check();
            core.NavigationCompleted += (_, _) =>
            {
                _progress.Visibility = Visibility.Collapsed;
                Check();
            };
            core.NavigationStarting += (_, _) => _progress.Visibility = Visibility.Visible;
            core.NewWindowRequested += (sender, args) =>
            {
                args.Handled = true;
                var uri = args.Uri ?? "";
                if (WebMail.IsInternalHost(_provider.Web!, uri)) sender.Navigate(uri);
                else if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http") AppServices.OpenInBrowser(parsed);
            };
            core.Navigate(_provider.Web!.LoginUrl);
        }
        catch (Exception ex)
        {
            _progress.Visibility = Visibility.Collapsed;
            Content = new TextBlock
            {
                Text = "Die eingebettete Anmeldung konnte nicht gestartet werden (WebView2): " + ex.Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24),
            };
        }
    }

    private void Check()
    {
        if (_view.CoreWebView2 is { } core && WebMail.IsLoggedIn(_provider.Web!, core.Source)) Finish(true);
    }

    /// <summary>Von außen abbrechen (z. B. „Abbrechen“ im Assistenten).</summary>
    public void Cancel() => Finish(false);

    private void Finish(bool success)
    {
        if (_finished) return;
        _finished = true;
        if (!success)
        {
            // Abbrechen löscht den gerade angelegten Speicher.
            if (_view.CoreWebView2 is { } core)
            {
                try { core.Profile.Delete(); } catch (Exception) { }
            }
            else
            {
                WebAccountService.DeleteProfileFolder(_accountId);
            }
        }
        _done.TrySetResult(success);
        try { _view.Close(); } catch (Exception) { }
        Close();
    }
}
