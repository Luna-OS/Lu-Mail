using System.Globalization;
using System.Text;
using LuMail.Core.Models;

namespace LuMail.Core.Mail;

/// <summary>Hilfen für Antworten und Weiterleiten (Re:/Fwd:, zitierter Text).</summary>
public static class MessageComposer
{
    private static readonly string[] ReplyPrefixes = { "re:", "aw:", "antw:" };
    private static readonly string[] ForwardPrefixes = { "fwd:", "fw:", "wg:" };

    public static string ReplySubject(string? subject)
    {
        var s = (subject ?? "").Trim();
        return ReplyPrefixes.Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? s : "Re: " + s;
    }

    public static string ForwardSubject(string? subject)
    {
        var s = (subject ?? "").Trim();
        return ForwardPrefixes.Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? s : "Fwd: " + s;
    }

    /// <summary>Jede Zeile mit "&gt; " einleiten.</summary>
    public static string Quote(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Select(l => l.StartsWith('>') ? ">" + l : "> " + l));
    }

    /// <summary>Signaturblock <c>\n\n-- \n&lt;Signatur&gt;</c> (leer ohne Signatur).</summary>
    public static string SignatureBlock(string? signature) =>
        string.IsNullOrWhiteSpace(signature) ? "" : "\n\n-- \n" + signature.Replace("\r\n", "\n").TrimEnd();

    /// <summary>Neue E-Mail: nur die Signatur.</summary>
    public static OutgoingMessage CreateNew(Account? account) => new() { Body = SignatureBlock(account?.Signature) };

    public static string BodyText(MessageDetail message) =>
        !string.IsNullOrWhiteSpace(message.TextBody) ? message.TextBody!.Trim() : HtmlPreparer.HtmlToPlainText(message.HtmlBody);

    private static string FormatDate(DateTimeOffset date) =>
        date.ToLocalTime().ToString("dd.MM.yyyy 'um' HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Antwort bzw. „Allen antworten“ vorbereiten.</summary>
    public static OutgoingMessage CreateReply(MessageDetail message, Account account, bool replyAll)
    {
        var own = account.Email.Trim();
        var primary = message.ReplyTo.Count > 0
            ? message.ReplyTo.ToList()
            : (message.From is null ? new List<MailAddress>() : new List<MailAddress> { message.From });

        var to = primary.Where(a => !SameAddress(a.Address, own)).ToList();
        if (to.Count == 0) to = primary; // Antwort auf eigene Nachricht

        var cc = new List<MailAddress>();
        if (replyAll)
        {
            foreach (var a in message.To.Concat(message.Cc))
            {
                if (SameAddress(a.Address, own)) continue;
                if (to.Any(t => SameAddress(t.Address, a.Address)) || cc.Any(c => SameAddress(c.Address, a.Address))) continue;
                cc.Add(a);
            }
        }

        var sender = message.From?.ToString() ?? "Unbekannt";
        var body = new StringBuilder()
            .Append(SignatureBlock(account.Signature))
            .Append("\n\n")
            .Append("Am ").Append(FormatDate(message.Date)).Append(" schrieb ").Append(sender).Append(":\n")
            .Append(Quote(BodyText(message)))
            .ToString();

        var references = message.References.ToList();
        if (!string.IsNullOrEmpty(message.MessageId) && !references.Contains(message.MessageId)) references.Add(message.MessageId);

        return new OutgoingMessage
        {
            To = string.Join(", ", to.Select(a => a.ToString())),
            Cc = string.Join(", ", cc.Select(a => a.ToString())),
            Subject = ReplySubject(message.Subject),
            Body = body,
            InReplyTo = message.MessageId,
            References = references,
        };
    }

    /// <summary>Weiterleitung vorbereiten (Klartext, übernimmt die Anhänge).</summary>
    public static OutgoingMessage CreateForward(MessageDetail message, Account? account = null)
    {
        var body = new StringBuilder()
            .Append(SignatureBlock(account?.Signature))
            .Append("\n\n---------- Weitergeleitete Nachricht ----------\n")
            .Append("Von: ").Append(message.From?.ToString() ?? "").Append('\n')
            .Append("Datum: ").Append(FormatDate(message.Date)).Append('\n')
            .Append("Betreff: ").Append(message.Subject).Append('\n')
            .Append("An: ").Append(string.Join(", ", message.To.Select(a => a.ToString()))).Append('\n');
        if (message.Cc.Count > 0) body.Append("Cc: ").Append(string.Join(", ", message.Cc.Select(a => a.ToString()))).Append('\n');
        body.Append('\n').Append(BodyText(message));

        return new OutgoingMessage
        {
            Subject = ForwardSubject(message.Subject),
            Body = body.ToString(),
            Attachments = message.Attachments.ToList(),
        };
    }

    private static bool SameAddress(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
