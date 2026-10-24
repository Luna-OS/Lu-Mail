using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LuMail.App.Services;
using LuMail.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LuMail.App.ViewModels;

/// <summary>Eine Zeile der Nachrichtenliste.</summary>
public sealed class MessageItem : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public MessageSummary Summary { get; }
    public Account Account { get; }
    public Provider Provider { get; }

    public MessageItem(MessageSummary summary, Account account, Provider provider)
    {
        Summary = summary;
        Account = account;
        Provider = provider;
    }

    /// <summary>Zwischenüberschrift in der Liste („Weitere Treffer auf dem Server“).</summary>
    public static MessageItem Header(string text, Account account, Provider provider) =>
        new(new MessageSummary { AccountId = "", FolderPath = "header:" + text, Subject = text }, account, provider) { IsHeader = true };

    public bool IsHeader { get; private init; }
    public string HeaderText => Summary.Subject;

    /// <summary>Konversation (älteste zuerst), null im Einzelmodus.</summary>
    public IReadOnlyList<MessageSummary>? Thread { get; init; }
    /// <summary>Alle Einträge der Konversation (für Aktionen auf die ganze Konversation).</summary>
    public IReadOnlyList<MessageItem>? ThreadItems { get; init; }
    public int ThreadCount => Thread?.Count ?? 1;
    public Visibility ThreadVisibility => ThreadCount > 1 ? Visibility.Visible : Visibility.Collapsed;
    public string ThreadCountText => ThreadCount.ToString(CultureInfo.CurrentCulture);
    public string ParticipantsText => Thread is { Count: > 1 } t
        ? string.Join(", ", t.Reverse().Select(m => m.FromDisplay).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(4))
        : Sender;

    /// <summary>Geplante Nachricht (Übersicht „Geplant“).</summary>
    public LuMail.Core.Storage.OutboxEntry? Outbox { get; init; }
    /// <summary>Zurückgestellte Nachricht (Übersicht „Erinnerungen“).</summary>
    public LuMail.Core.Storage.SnoozeEntry? Snooze { get; set; }

    /// <summary>Zusatzzeile („Geplant für …“, „Erinnerung …“).</summary>
    public string InfoText =>
        Outbox is not null ? $"Geplant für {LuMail.Core.Mail.SnoozePresets.Format(Outbox.SendAt)}"
        : Snooze is not null ? $"Erinnerung {LuMail.Core.Mail.SnoozePresets.Format(Snooze.Until)}"
        : "";
    public Visibility InfoVisibility => InfoText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string Key => Summary.Key;
    public string Sender => Outbox is not null ? "An: " + string.Join(", ", Outbox.Recipients)
        : string.IsNullOrWhiteSpace(Summary.FromDisplay) ? "(Unbekannter Absender)" : Summary.FromDisplay;
    public string Subject => string.IsNullOrWhiteSpace(Summary.Subject) ? "(Kein Betreff)" : Summary.Subject;
    public string Preview => Summary.Preview;
    public SolidColorBrush ProviderBrush => BrushCache.ForProvider(Provider);
    public string AccountEmail => Account.Email;

    /// <summary>Heute: Uhrzeit, dieses Jahr: "12. Mär", sonst "12.03.2023".</summary>
    public string DateText
    {
        get
        {
            var local = Summary.Date.ToLocalTime();
            var now = DateTimeOffset.Now;
            if (local.Date == now.Date) return local.ToString("HH:mm", German);
            if (local.Date == now.Date.AddDays(-1)) return "Gestern";
            if (local.Year == now.Year) return local.ToString("d. MMM", German);
            return local.ToString("dd.MM.yyyy", German);
        }
    }

    public string FullDateText => Summary.Date.ToLocalTime().ToString("dddd, d. MMMM yyyy, HH:mm", German);

    public bool Seen
    {
        get => Summary.Seen;
        set
        {
            if (Summary.Seen == value) return;
            Summary.Seen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnreadVisibility));
            OnPropertyChanged(nameof(SenderWeight));
            OnPropertyChanged(nameof(SubjectWeight));
        }
    }

    public bool Flagged
    {
        get => Summary.Flagged;
        set
        {
            if (Summary.Flagged == value) return;
            Summary.Flagged = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FlagVisibility));
        }
    }

    /// <summary>Kategorie-Id ("primary", …, "custom-…").</summary>
    public string? CategoryId
    {
        get => Summary.CategoryId;
        set
        {
            if (Summary.CategoryId == value) return;
            Summary.CategoryId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CategoryName));
            OnPropertyChanged(nameof(CategoryBrush));
            OnPropertyChanged(nameof(CategoryVisibility));
        }
    }

    public Visibility UnreadVisibility => Seen ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FlagVisibility => Flagged ? Visibility.Visible : Visibility.Collapsed;
    public Windows.UI.Text.FontWeight SenderWeight => Seen ? FontWeights.Normal : FontWeights.SemiBold;
    public Windows.UI.Text.FontWeight SubjectWeight => Seen ? FontWeights.Normal : FontWeights.SemiBold;

    /// <summary>Kategorie-Chip nur außerhalb von „Posteingang“ (primary).</summary>
    public Visibility CategoryVisibility =>
        CategoryId is { } c && c != "primary" ? Visibility.Visible : Visibility.Collapsed;

    public string CategoryName => CategoryId is { } c ? App.Services.CategoryName(c) : "";
    public SolidColorBrush CategoryBrush => BrushCache.ForCategory(CategoryId ?? "primary");

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || Contains(Summary.FromName, query) || Contains(Summary.FromAddress, query)
        || Contains(Summary.Subject, query) || Contains(Summary.Preview, query) || Contains(Summary.To, query);

    private static bool Contains(string? text, string query) =>
        !string.IsNullOrEmpty(text) && text.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Kachel im Dialog „Konto hinzufügen“.</summary>
public sealed class ProviderTile
{
    public ProviderTile(Provider provider, bool oauthAvailable)
    {
        Provider = provider;
        OAuthAvailable = oauthAvailable;
    }

    public Provider Provider { get; }
    public bool OAuthAvailable { get; }
    public string Name => Provider.Name;
    public SolidColorBrush Brush => BrushCache.ForProvider(Provider);
    public string Initial => Provider.Name.Length > 0 ? Provider.Name[..1] : "?";
    public string MethodText => OAuthAvailable ? "Anmeldung über OAuth" : Provider.PasswordLabel;
}
