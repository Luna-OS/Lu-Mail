namespace LuMail.Core.Models;

/// <summary>Eine E-Mail-Adresse mit optionalem Namen.</summary>
public sealed record MailAddress(string? Name, string Address)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name!;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name) ? Address : $"{Name} <{Address}>";
}

/// <summary>Kopfdaten einer Nachricht für die Nachrichtenliste.</summary>
public sealed class MessageSummary
{
    public string AccountId { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public uint Uid { get; init; }
    public string FromName { get; init; } = "";
    public string FromAddress { get; init; } = "";
    /// <summary>Empfänger, kommagetrennt.</summary>
    public string To { get; init; } = "";
    public string Subject { get; init; } = "";
    public DateTimeOffset Date { get; init; }
    public bool Seen { get; set; }
    public bool Flagged { get; set; }
    /// <summary>Nur für INBOX-Nachrichten gesetzt.</summary>
    public MessageCategory? Category { get; set; }
    public string Preview { get; init; } = "";

    public string FromDisplay => string.IsNullOrWhiteSpace(FromName) ? FromAddress : FromName;

    /// <summary>Eindeutiger Schlüssel über Konten und Ordner hinweg.</summary>
    public string Key => $"{AccountId}|{FolderPath}|{Uid}";
}

/// <summary>Ein Anhang (Inhalt bereits dekodiert).</summary>
public sealed class AttachmentInfo
{
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "application/octet-stream";
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public long Size => Content.LongLength;
}

/// <summary>Eingebettete Ressource (cid:), wird für die Anzeige zu einer data:-URL.</summary>
public sealed record InlineResource(string ContentType, byte[] Data);

/// <summary>Vollständig geladene Nachricht für die Leseansicht.</summary>
public sealed class MessageDetail
{
    public string AccountId { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public uint Uid { get; init; }
    public string Subject { get; init; } = "";
    public MailAddress? From { get; init; }
    public IReadOnlyList<MailAddress> To { get; init; } = Array.Empty<MailAddress>();
    public IReadOnlyList<MailAddress> Cc { get; init; } = Array.Empty<MailAddress>();
    public IReadOnlyList<MailAddress> ReplyTo { get; init; } = Array.Empty<MailAddress>();
    public DateTimeOffset Date { get; init; }
    public string? MessageId { get; init; }
    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();
    public string? HtmlBody { get; init; }
    public string? TextBody { get; init; }
    public IReadOnlyList<AttachmentInfo> Attachments { get; init; } = Array.Empty<AttachmentInfo>();
    /// <summary>Content-ID (ohne spitze Klammern) → Ressource.</summary>
    public IReadOnlyDictionary<string, InlineResource> InlineResources { get; init; } =
        new Dictionary<string, InlineResource>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Eine zu sendende Nachricht (Klartext).</summary>
public sealed class OutgoingMessage
{
    /// <summary>Kommagetrennte Adresslisten (RFC 5322).</summary>
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Bcc { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? InReplyTo { get; set; }
    public List<string> References { get; set; } = new();
}
