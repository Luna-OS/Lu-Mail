using LuMail.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LuMail.App.Views;

/// <summary>Neue E-Mail / Antwort / Weiterleitung (Klartext).</summary>
public sealed partial class ComposePage : Page
{
    private List<Account> _accounts = new();
    private OutgoingMessage _draft = new();
    private bool _sending;

    public ComposePage()
    {
        InitializeComponent();
    }

    /// <summary>Wird nach erfolgreichem Versand ausgelöst (Fenster schließen).</summary>
    public event EventHandler? Sent;

    /// <summary>Wird ausgelöst, wenn sich der Betreff ändert (Fenstertitel).</summary>
    public event EventHandler<string>? SubjectChanged;

    public UIElement TitleBarElement => TitleBarArea;

    public void Initialize(IReadOnlyList<Account> accounts, Account? from, OutgoingMessage? draft)
    {
        _accounts = accounts.ToList();
        _draft = draft ?? new OutgoingMessage();

        FromBox.Items.Clear();
        foreach (var account in _accounts)
        {
            var provider = App.Services.Providers.Find(account.ProviderId);
            FromBox.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(account.DisplayName)
                    ? $"{account.Email} ({provider?.Name})"
                    : $"{account.DisplayName} <{account.Email}> ({provider?.Name})",
                Tag = account,
            });
        }
        var index = from is null ? 0 : Math.Max(0, _accounts.FindIndex(a => a.Id == from.Id));
        if (FromBox.Items.Count > 0) FromBox.SelectedIndex = index;

        ToBox.Text = _draft.To;
        CcBox.Text = _draft.Cc;
        SubjectBox.Text = _draft.Subject;
        BodyBox.Text = _draft.Body;
        UpdateTitle();

        // Bei Antworten den Cursor an den Anfang des Textes, sonst ins An-Feld.
        if (string.IsNullOrWhiteSpace(_draft.To))
        {
            ToBox.Focus(FocusState.Programmatic);
        }
        else
        {
            BodyBox.Focus(FocusState.Programmatic);
            BodyBox.SelectionStart = 0;
        }
    }

    private void OnSubjectChanged(object sender, TextChangedEventArgs e) => UpdateTitle();

    private void UpdateTitle()
    {
        var title = string.IsNullOrWhiteSpace(SubjectBox.Text) ? "Neue E-Mail" : SubjectBox.Text;
        TitleText.Text = title;
        SubjectChanged?.Invoke(this, title);
    }

    private async void OnSendClick(object sender, RoutedEventArgs e) => await SendAsync();

    private async void OnSendAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SendAsync();
    }

    private async Task SendAsync()
    {
        if (_sending) return;
        if ((FromBox.SelectedItem as ComboBoxItem)?.Tag is not Account account)
        {
            StatusText.Text = "Bitte ein Absenderkonto wählen.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ToBox.Text) && string.IsNullOrWhiteSpace(CcBox.Text))
        {
            StatusText.Text = "Bitte mindestens einen Empfänger angeben.";
            return;
        }

        var message = new OutgoingMessage
        {
            To = ToBox.Text,
            Cc = CcBox.Text,
            Subject = SubjectBox.Text,
            Body = BodyBox.Text.Replace("\r\n", "\n").Replace("\r", "\n"),
            InReplyTo = _draft.InReplyTo,
            References = _draft.References,
        };

        _sending = true;
        SendButton.IsEnabled = false;
        SendingRing.IsActive = true;
        StatusText.Text = "";
        try
        {
            await App.Services.Mail.SendAsync(account, message);
            Sent?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _sending = false;
            SendButton.IsEnabled = true;
            SendingRing.IsActive = false;
        }
    }
}
