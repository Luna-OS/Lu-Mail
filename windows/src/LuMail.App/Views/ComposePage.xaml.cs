using LuMail.App.ViewModels;
using LuMail.Core.Mail;
using LuMail.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace LuMail.App.Views;

/// <summary>Neue E-Mail / Antwort / Weiterleitung (Klartext, Anhänge, Signatur, Kontakte, später senden).</summary>
public sealed partial class ComposePage : Page
{
    private List<Account> _accounts = new();
    private OutgoingMessage _draft = new();
    private readonly List<AttachmentInfo> _attachments = new();
    private MainViewModel? _vm;
    private string _autoSignature = "";
    private bool _sending;

    public ComposePage()
    {
        InitializeComponent();
    }

    public event EventHandler? Sent;
    public event EventHandler<string>? SubjectChanged;

    public UIElement TitleBarElement => TitleBarArea;

    /// <summary>Fenster (für die Dateiauswahl).</summary>
    public Window? HostWindow { get; set; }

    public void Initialize(IReadOnlyList<Account> accounts, Account? from, OutgoingMessage? draft, MainViewModel? vm)
    {
        _accounts = accounts.ToList();
        _vm = vm;
        var fromAccount = from ?? _accounts.FirstOrDefault();
        _draft = draft ?? MessageComposer.CreateNew(fromAccount);
        _autoSignature = draft is null ? _draft.Body : "";

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
        var index = fromAccount is null ? 0 : Math.Max(0, _accounts.FindIndex(a => a.Id == fromAccount.Id));
        if (FromBox.Items.Count > 0) FromBox.SelectedIndex = index;

        ToBox.Text = _draft.To;
        CcBox.Text = _draft.Cc;
        BccBox.Text = _draft.Bcc;
        SubjectBox.Text = _draft.Subject;
        BodyBox.Text = _draft.Body;
        _attachments.Clear();
        _attachments.AddRange(_draft.Attachments);
        RefreshAttachments();
        UpdateTitle();

        if (string.IsNullOrWhiteSpace(_draft.To)) ToBox.Focus(FocusState.Programmatic);
        else
        {
            BodyBox.Focus(FocusState.Programmatic);
            BodyBox.SelectionStart = 0;
        }
    }

    private Account? SelectedAccount => (FromBox.SelectedItem as ComboBoxItem)?.Tag as Account;

    /// <summary>Neue Mail: Signatur passend zum gewählten Konto austauschen.</summary>
    private void OnFromChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_autoSignature.Length == 0 && BodyBox.Text.Length > 0) return;
        var block = MessageComposer.SignatureBlock(SelectedAccount?.Signature);
        if (_autoSignature.Length > 0 && BodyBox.Text.Contains(_autoSignature, StringComparison.Ordinal))
            BodyBox.Text = BodyBox.Text.Replace(_autoSignature, block, StringComparison.Ordinal);
        else if (BodyBox.Text.Length == 0)
            BodyBox.Text = block;
        _autoSignature = block;
    }

    private void OnSubjectChanged(object sender, TextChangedEventArgs e) => UpdateTitle();

    private void UpdateTitle()
    {
        var title = string.IsNullOrWhiteSpace(SubjectBox.Text) ? "Neue E-Mail" : SubjectBox.Text;
        TitleText.Text = title;
        SubjectChanged?.Invoke(this, title);
    }

    // ------------------------------------------------------- Kontakte (§5)

    private static (string Prefix, string Token) SplitLastToken(string text)
    {
        var idx = Math.Max(text.LastIndexOf(','), text.LastIndexOf(';'));
        return idx < 0 ? ("", text.Trim()) : (text[..(idx + 1)] + " ", text[(idx + 1)..].Trim());
    }

    private void OnAddressTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var (_, token) = SplitLastToken(sender.Text);
        var own = _accounts.Select(a => a.Email.ToLowerInvariant()).ToHashSet();
        sender.ItemsSource = token.Length < 1
            ? null
            : App.Services.Contacts.Suggest(token, 8, own).Select(s => s.ToString()).ToList();
    }

    private void OnAddressSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        var (prefix, _) = SplitLastToken(sender.Text);
        sender.Text = prefix.TrimStart() + args.SelectedItem + ", ";
        sender.ItemsSource = null;
    }

    // --------------------------------------------------------- Anhänge (§5)

    private async void OnAddAttachmentClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is null) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(HostWindow));
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files) await AddFileAsync(file);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Datei konnte nicht angehängt werden: " + ex.Message;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Anhängen";
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            foreach (var item in await e.DataView.GetStorageItemsAsync())
                if (item is StorageFile file) await AddFileAsync(file);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Datei konnte nicht angehängt werden: " + ex.Message;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task AddFileAsync(StorageFile file)
    {
        byte[] content;
        if (!string.IsNullOrEmpty(file.Path) && File.Exists(file.Path))
        {
            content = await File.ReadAllBytesAsync(file.Path);
        }
        else
        {
            using var stream = await file.OpenStreamForReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            content = ms.ToArray();
        }
        _attachments.Add(new AttachmentInfo
        {
            FileName = file.Name,
            ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Content = content,
        });
        RefreshAttachments();
    }

    private void RefreshAttachments()
    {
        AttachmentsPanel.Children.Clear();
        foreach (var attachment in _attachments)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new FontIcon { Glyph = "", FontSize = 12 });
            content.Children.Add(new TextBlock { Text = $"{attachment.FileName} ({MainPage.FormatSize(attachment.Size)})" });
            content.Children.Add(new FontIcon { Glyph = "", FontSize = 10 });
            var button = new Button { Content = content, Tag = attachment };
            ToolTipService.SetToolTip(button, "Anhang entfernen");
            button.Click += (s, _) =>
            {
                if ((s as Button)?.Tag is AttachmentInfo a) _attachments.Remove(a);
                RefreshAttachments();
            };
            AttachmentsPanel.Children.Add(button);
        }
        var total = _attachments.Sum(a => a.Size);
        AttachmentHint.Visibility = total >= OutgoingMessage.LargeAttachmentThreshold ? Visibility.Visible : Visibility.Collapsed;
        AttachmentHint.Text = $"Die Anhänge sind zusammen {MainPage.FormatSize(total)} groß – viele Anbieter nehmen nur bis etwa 20–25 MB an.";
    }

    // ----------------------------------------------------------- Senden

    private async void OnSendClick(object sender, RoutedEventArgs e) => await SendAsync();

    private async void OnSendAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SendAsync();
    }

    private OutgoingMessage? BuildOutgoing(out Account? account)
    {
        account = SelectedAccount;
        if (account is null)
        {
            StatusText.Text = "Bitte ein Absenderkonto wählen.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(ToBox.Text) && string.IsNullOrWhiteSpace(CcBox.Text) && string.IsNullOrWhiteSpace(BccBox.Text))
        {
            StatusText.Text = "Bitte mindestens einen Empfänger angeben.";
            return null;
        }
        return new OutgoingMessage
        {
            To = ToBox.Text.Trim().TrimEnd(','),
            Cc = CcBox.Text.Trim().TrimEnd(','),
            Bcc = BccBox.Text.Trim().TrimEnd(','),
            Subject = SubjectBox.Text,
            Body = BodyBox.Text.Replace("\r\n", "\n").Replace("\r", "\n"),
            InReplyTo = _draft.InReplyTo,
            References = _draft.References,
            Attachments = _attachments.ToList(),
        };
    }

    private static void RecordContacts(MimeKit.MimeMessage mime)
    {
        foreach (var r in mime.To.Mailboxes.Concat(mime.Cc.Mailboxes).Concat(mime.Bcc.Mailboxes))
            App.Services.Contacts.RecordSent(r.Address, r.Name, DateTimeOffset.Now);
        try { App.Services.Contacts.Save(); } catch (IOException) { }
    }

    private async Task SendAsync()
    {
        if (_sending) return;
        var message = BuildOutgoing(out var account);
        if (message is null || account is null) return;

        _sending = true;
        SendButton.IsEnabled = false;
        ScheduleButton.IsEnabled = false;
        SendingRing.IsActive = true;
        StatusText.Text = "";
        try
        {
            var mime = MailService.BuildMimeMessage(account, message);
            await App.Services.Mail.SendMimeAsync(account, mime);
            RecordContacts(mime);
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
            ScheduleButton.IsEnabled = true;
            SendingRing.IsActive = false;
        }
    }

    /// <summary>§13 Später senden: outbox/&lt;id&gt;.eml + .json.</summary>
    private async void OnScheduleClick(object sender, RoutedEventArgs e)
    {
        var message = BuildOutgoing(out var account);
        if (message is null || account is null || _vm is null) return;
        var when = await Dialogs.PickDateTimeAsync(XamlRoot, "Später senden", DateTimeOffset.Now.Date.AddDays(1).AddHours(8));
        if (when is not { } sendAt) return;
        try
        {
            var mime = MailService.BuildMimeMessage(account, message);
            _vm.Schedule(account, mime, sendAt);
            RecordContacts(mime);
            Sent?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
