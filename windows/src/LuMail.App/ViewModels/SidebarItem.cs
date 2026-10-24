using CommunityToolkit.Mvvm.ComponentModel;
using LuMail.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LuMail.App.ViewModels;

public enum SidebarItemKind
{
    /// <summary>Abschnittsüberschrift („Übersicht“).</summary>
    Section,
    /// <summary>Anbietergruppe mit Farbpunkt.</summary>
    Provider,
    /// <summary>Konto (E-Mail-Adresse).</summary>
    Account,
    /// <summary>Auswählbarer Eintrag (Übersicht, Kategorie oder Ordner).</summary>
    Folder,
}

public enum SidebarTarget
{
    None,
    AllInboxes,
    Unread,
    Flagged,
    /// <summary>Später erinnern: alle zurückgestellten Nachrichten.</summary>
    Reminders,
    /// <summary>Später senden: geplante Nachrichten (outbox/).</summary>
    Scheduled,
    /// <summary>„Alle &lt;Anbieter&gt;-Posteingänge“ (Anbieter mit mehreren Konten).</summary>
    ProviderInbox,
    /// <summary>Ordner eines Web-Kontos (FolderPath = Ordner-Schlüssel aus providers.json → web.folders).</summary>
    Web,
    /// <summary>Kategorie (virtueller INBOX-Unterordner) oder – ohne Kategorientrennung – ganzer Posteingang.</summary>
    Category,
    /// <summary>Server-Ordner.</summary>
    Folder,
}

/// <summary>Eine Zeile der Sidebar (flache Liste mit Einrückung).</summary>
public sealed class SidebarItem : ObservableObject
{
    public SidebarItemKind Kind { get; init; }
    public SidebarTarget Target { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Glyph { get; init; } = "";
    public Account? Account { get; init; }
    public Provider? Provider { get; init; }
    /// <summary>Bei <see cref="SidebarTarget.Category"/>: Kategorie; null = ganzer Posteingang.</summary>
    public string? CategoryId { get; init; }
    public string? FolderPath { get; init; }
    public FolderRole Role { get; init; }
    public SolidColorBrush? DotBrush { get; init; }
    public Thickness Indent { get; init; }

    /// <summary>Stabiler Schlüssel, um die Auswahl nach einem Neuaufbau wiederherzustellen.</summary>
    public string Key { get; init; } = "";

    public bool IsSelectable => Kind == SidebarItemKind.Folder || Target == SidebarTarget.ProviderInbox;

    /// <summary>Überschrift der Nachrichtenliste.</summary>
    public string ListTitle { get; init; } = "";

    private int _unread;
    public int Unread
    {
        get => _unread;
        set
        {
            if (SetProperty(ref _unread, value))
            {
                OnPropertyChanged(nameof(UnreadText));
                OnPropertyChanged(nameof(BadgeVisibility));
            }
        }
    }

    public string UnreadText => Unread > 999 ? "999+" : Unread.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public Visibility BadgeVisibility => Unread > 0 ? Visibility.Visible : Visibility.Collapsed;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(SelectionVisibility));
        }
    }

    public Visibility SelectionVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    private string _error = "";
    /// <summary>Fehlermeldung eines Kontos (wird als Tooltip/Warnsymbol gezeigt).</summary>
    public string Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(ErrorVisibility));
        }
    }

    public Visibility ErrorVisibility => string.IsNullOrEmpty(Error) ? Visibility.Collapsed : Visibility.Visible;
}
