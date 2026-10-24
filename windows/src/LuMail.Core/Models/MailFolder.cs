namespace LuMail.Core.Models;

/// <summary>Ordnerrollen aus providers.json → folderRoles.</summary>
public enum FolderRole
{
    None,
    Inbox,
    Sent,
    Drafts,
    Spam,
    Trash,
    Archive,
}

public static class FolderRoles
{
    /// <summary>Reihenfolge der Rollenordner in der Sidebar (Gesendet / Entwürfe / Spam / Papierkorb / Archiv).</summary>
    public static IReadOnlyList<FolderRole> SidebarOrder { get; } = new[]
    {
        FolderRole.Sent, FolderRole.Drafts, FolderRole.Spam, FolderRole.Trash, FolderRole.Archive,
    };

    public static string ToId(this FolderRole role) => role switch
    {
        FolderRole.Inbox => "inbox",
        FolderRole.Sent => "sent",
        FolderRole.Drafts => "drafts",
        FolderRole.Spam => "spam",
        FolderRole.Trash => "trash",
        FolderRole.Archive => "archive",
        _ => "",
    };

    public static bool TryParse(string? id, out FolderRole role)
    {
        role = (id?.Trim().ToLowerInvariant()) switch
        {
            "inbox" => FolderRole.Inbox,
            "sent" => FolderRole.Sent,
            "drafts" => FolderRole.Drafts,
            "spam" => FolderRole.Spam,
            "trash" => FolderRole.Trash,
            "archive" => FolderRole.Archive,
            _ => FolderRole.None,
        };
        return role != FolderRole.None;
    }

    /// <summary>Deutscher Anzeigename einer Rolle.</summary>
    public static string GermanName(this FolderRole role) => role switch
    {
        FolderRole.Inbox => "Posteingang",
        FolderRole.Sent => "Gesendet",
        FolderRole.Drafts => "Entwürfe",
        FolderRole.Spam => "Spam",
        FolderRole.Trash => "Papierkorb",
        FolderRole.Archive => "Archiv",
        _ => "",
    };
}

/// <summary>Ein Server-Ordner oder eine virtuelle Kategorie (Unterordner von INBOX).</summary>
public sealed class MailFolder
{
    public string AccountId { get; init; } = "";
    /// <summary>Vollständiger IMAP-Pfad (bei Kategorien: "INBOX").</summary>
    public string Path { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public FolderRole Role { get; init; }
    public int Unread { get; set; }
    public int Total { get; set; }
    public char Separator { get; init; } = '/';
    public bool IsCategory { get; init; }
    /// <summary>Gesetzt, wenn <see cref="IsCategory"/> wahr ist.</summary>
    public MessageCategory? Category { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? CategoryId => Category?.ToId();

    public override string ToString() => DisplayName;
}
