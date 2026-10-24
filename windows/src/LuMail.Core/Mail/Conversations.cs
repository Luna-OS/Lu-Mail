using System.Text.RegularExpressions;
using LuMail.Core.Models;

namespace LuMail.Core.Mail;

/// <summary>Eine Konversation (Nachrichten eines Kontos und Ordners, älteste zuerst).</summary>
public sealed class Conversation
{
    public IReadOnlyList<MessageSummary> Messages { get; init; } = Array.Empty<MessageSummary>();
    public MessageSummary Latest => Messages[^1];
    public int Count => Messages.Count;
    public bool HasUnread => Messages.Any(m => !m.Seen);

    /// <summary>Teilnehmer (Absender, neueste zuerst, ohne Dubletten).</summary>
    public IReadOnlyList<string> Participants =>
        Messages.Reverse().Select(m => m.FromDisplay).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>
/// Gruppiert Nachrichten zu Konversationen (FUNKTIONEN §8): pro Konto und Ordner über Message-ID, In-Reply-To und References;
/// Nachrichten ohne Bezug über den normalisierten Betreff und gemeinsame Teilnehmer.
/// </summary>
public static partial class ConversationBuilder
{
    /// <summary>Betreff ohne Präfixe Re:/AW:/Fwd:/WG:/FW:/Ответ: (auch mehrfach, z. B. "Re: AW: x"), klein geschrieben.</summary>
    public static string NormalizeSubject(string? subject)
    {
        var s = (subject ?? "").Trim();
        while (true)
        {
            var m = Prefix().Match(s);
            if (!m.Success) break;
            s = s[m.Length..].TrimStart();
        }
        return Whitespace().Replace(s, " ").Trim().ToLowerInvariant();
    }

    private static string Id(string? id) => (id ?? "").Trim().Trim('<', '>').ToLowerInvariant();

    private static HashSet<string> Participants(MessageSummary m)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(m.FromAddress)) set.Add(m.FromAddress.Trim());
        foreach (var r in m.Recipients) if (!string.IsNullOrWhiteSpace(r.Address)) set.Add(r.Address.Trim());
        return set;
    }

    /// <summary>Gruppiert; Ergebnis nach neuester Nachricht absteigend sortiert.</summary>
    public static List<Conversation> Group(IEnumerable<MessageSummary> messages)
    {
        var result = new List<Conversation>();
        foreach (var bucket in messages.GroupBy(m => (m.AccountId, m.FolderPath)))
        {
            var list = bucket.OrderBy(m => m.Date).ThenBy(m => m.Uid).ToList();
            var parent = Enumerable.Range(0, list.Count).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }

            // 1. Header-Bezüge
            var byId = new Dictionary<string, int>();
            for (var i = 0; i < list.Count; i++)
            {
                var id = Id(list[i].MessageId);
                if (id.Length > 0) byId.TryAdd(id, i);
            }
            var linked = new bool[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                var refs = list[i].References.Select(Id).Append(Id(list[i].InReplyTo)).Where(r => r.Length > 0);
                foreach (var r in refs)
                {
                    if (byId.TryGetValue(r, out var j) && j != i)
                    {
                        Union(i, j);
                        linked[i] = linked[j] = true;
                    }
                }
            }

            // 2. Ohne Bezug: gleicher normalisierter Betreff + gemeinsamer Teilnehmer
            for (var i = 0; i < list.Count; i++)
            {
                if (linked[i]) continue;
                var subject = NormalizeSubject(list[i].Subject);
                if (subject.Length == 0) continue;
                var people = Participants(list[i]);
                for (var j = 0; j < i; j++)
                {
                    if (Find(j) == Find(i)) continue;
                    if (NormalizeSubject(list[j].Subject) != subject) continue;
                    if (!Participants(list[j]).Overlaps(people)) continue;
                    Union(i, j);
                    break;
                }
            }

            foreach (var group in Enumerable.Range(0, list.Count).GroupBy(Find))
                result.Add(new Conversation { Messages = group.Select(i => list[i]).OrderBy(m => m.Date).ThenBy(m => m.Uid).ToList() });
        }
        return result.OrderByDescending(c => c.Latest.Date).ToList();
    }

    [GeneratedRegex(@"^(re|aw|fwd|wg|fw|ответ)\s*(\[\d+\])?\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex Prefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
