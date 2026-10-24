using LuMail.App.Services;
using LuMail.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace LuMail.App.Views;

/// <summary>Einstellungen: Aktualisierungsintervall, Kategorien, externe Bilder, Konten entfernen.</summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private static readonly int[] Intervals = { 1, 3, 5, 10, 15, 30, 60 };
    private readonly AppServices _s = App.Services;
    private bool _loading = true;
    private bool _confirmingRemoval;

    /// <summary>True, wenn ein Konto entfernt wurde.</summary>
    public bool AccountsChanged { get; private set; }

    public SettingsDialog()
    {
        InitializeComponent();
        foreach (var minutes in Intervals)
            IntervalBox.Items.Add(new ComboBoxItem { Content = minutes == 1 ? "Jede Minute" : $"Alle {minutes} Minuten", Tag = minutes });
        var current = Array.IndexOf(Intervals, _s.Settings.RefreshIntervalMinutes);
        IntervalBox.SelectedIndex = current >= 0 ? current : 1;
        CategoriesSwitch.IsOn = _s.Settings.SeparateCategories;
        ExternalSwitch.IsOn = _s.Settings.AlwaysLoadExternalImages;
        DataFolderText.Text = _s.Paths.BaseDirectory;
        _loading = false;
    }

    public void Initialize(IReadOnlyList<Account> accounts)
    {
        AccountsPanel.Children.Clear();
        NoAccountsText.Visibility = accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var account in accounts)
        {
            var provider = _s.Providers.Find(account.ProviderId);
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Ellipse { Width = 10, Height = 10, Fill = BrushCache.ForProvider(provider), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(dot);

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = account.Email, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = $"{provider?.Name ?? account.ProviderId} · {(account.IsOAuth ? "OAuth" : "Passwort")}",
                FontSize = 12,
                Opacity = 0.7,
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var remove = new Button { Content = "Entfernen", Tag = account, VerticalAlignment = VerticalAlignment.Center };
            remove.Click += OnRemoveAccountClick;
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);

            AccountsPanel.Children.Add(row);
        }
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || (IntervalBox.SelectedItem as ComboBoxItem)?.Tag is not int minutes) return;
        _s.Settings.RefreshIntervalMinutes = minutes;
        _s.SaveSettings();
    }

    private void OnCategoriesToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.SeparateCategories = CategoriesSwitch.IsOn;
        _s.SaveSettings();
    }

    private void OnExternalToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.AlwaysLoadExternalImages = ExternalSwitch.IsOn;
        _s.SaveSettings();
    }

    /// <summary>Erster Klick fragt nach („Wirklich entfernen?“), zweiter Klick entfernt.</summary>
    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Account account } button) return;
        if (!Equals(button.Content, "Wirklich entfernen?"))
        {
            button.Content = "Wirklich entfernen?";
            return;
        }
        if (_confirmingRemoval) return;
        _confirmingRemoval = true;
        button.IsEnabled = false;
        try
        {
            await _s.Setup.RemoveAccountAsync(account);
            AccountsChanged = true;
            Initialize(_s.Accounts.Load());
        }
        finally
        {
            _confirmingRemoval = false;
        }
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e) => AppServices.OpenFolder(_s.Paths.BaseDirectory);
}
