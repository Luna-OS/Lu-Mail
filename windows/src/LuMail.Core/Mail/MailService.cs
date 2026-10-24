using System.Collections.Concurrent;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;
using LuMail.Core.Catalog;
using LuMail.Core.Classification;
using LuMail.Core.Models;
using MailFolder = LuMail.Core.Models.MailFolder;
using MessageSummary = LuMail.Core.Models.MessageSummary;

namespace LuMail.Core.Mail;

public sealed class MailServiceOptions
{
    /// <summary>INBOX: neueste 200 Nachrichten (Spezifikation).</summary>
    public int InboxFetchCount { get; init; } = 200;
    /// <summary>Andere Ordner: neueste 100 Nachrichten (Spezifikation).</summary>
    public int FolderFetchCount { get; init; } = 100;
    /// <summary>Vorschautext zusätzlich laden (MailKit PreviewText).</summary>
    public bool FetchPreviews { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// IMAP/SMTP-Zugriff über MailKit. Ein <see cref="ImapClient"/> pro Konto, serialisiert per SemaphoreSlim,
/// mit automatischem Neuverbinden bei Verbindungsfehlern.
/// </summary>
public sealed class MailService : IAsyncDisposable
{
    /// <summary>Zusätzlich geladene Header für den Klassifizierer.</summary>
    public static readonly string[] ClassifierHeaders = { "LIST-UNSUBSCRIBE", "LIST-ID", "LIST-POST", "PRECEDENCE" };

    private readonly ProviderCatalog _providers;
    private readonly CategoryCatalog _categories;
    private readonly IMailCredentialSource _credentials;
    private readonly ISenderRuleLookup? _senderRules;
    private readonly MailServiceOptions _options;
    private readonly FolderRoleResolver _roleResolver;
    private readonly ConcurrentDictionary<string, AccountSession> _sessions = new();

    public MailService(ProviderCatalog providers, CategoryCatalog categories, IMailCredentialSource credentials,
        ISenderRuleLookup? senderRules = null, MailServiceOptions? options = null)
    {
        _providers = providers;
        _categories = categories;
        _credentials = credentials;
        _senderRules = senderRules;
        _options = options ?? new MailServiceOptions();
        _roleResolver = new FolderRoleResolver(providers.FolderRoles);
    }

    private AccountSession Session(Account account) =>
        _sessions.GetOrAdd(account.Id, _ => new AccountSession(account, _providers.Get(account.ProviderId), _credentials, _options));

    /// <summary>Trennt die Verbindung eines Kontos (z. B. nach Entfernen oder neuen Zugangsdaten).</summary>
    public async Task ForgetAccountAsync(string accountId)
    {
        if (_sessions.TryRemove(accountId, out var session)) await session.DisposeAsync().ConfigureAwait(false);
    }

    // --------------------------------------------------------------- Ordner

    /// <summary>Alle auswählbaren Server-Ordner mit Rolle und Ungelesen-Zahl (ohne \Noselect).</summary>
    public Task<IReadOnlyList<MailFolder>> ListFoldersAsync(Account account, CancellationToken ct = default)
    {
        var session = Session(account);
        return session.RunAsync<IReadOnlyList<MailFolder>>(async (client, token) =>
        {
            var personal = client.PersonalNamespaces.Count > 0 ? client.PersonalNamespaces[0] : new FolderNamespace('/', "");
            IList<IMailFolder> folders;
            try
            {
                folders = await client.GetFoldersAsync(personal, StatusItems.Unread | StatusItems.Count, false, token).ConfigureAwait(false);
            }
            catch (ImapCommandException)
            {
                folders = await client.GetFoldersAsync(personal, StatusItems.None, false, token).ConfigureAwait(false);
            }

            var all = folders.ToList();
            if (!all.Any(f => string.Equals(f.FullName, "INBOX", StringComparison.OrdinalIgnoreCase)))
            {
                var inboxFolder = InboxOf(client);
                all.Insert(0, inboxFolder);
                try { await inboxFolder.StatusAsync(StatusItems.Unread | StatusItems.Count, token).ConfigureAwait(false); }
                catch (ImapCommandException) { }
            }

            var infos = all.Select(f => new FolderInfo(f.FullName, f.DirectorySeparator, SpecialUse(f.Attributes),
                f.Attributes.HasFlag(FolderAttributes.NoSelect) || f.Attributes.HasFlag(FolderAttributes.NonExistent))).ToList();
            var roles = _roleResolver.Resolve(infos);
            session.RolePaths = roles.ToDictionary(r => r.Value, r => r.Key);

            var result = new List<MailFolder>();
            foreach (var f in all)
            {
                if (f.Attributes.HasFlag(FolderAttributes.NoSelect) || f.Attributes.HasFlag(FolderAttributes.NonExistent)) continue;
                var role = roles.TryGetValue(f.FullName, out var r) ? r : FolderRole.None;
                result.Add(new MailFolder
                {
                    AccountId = account.Id,
                    Path = f.FullName,
                    DisplayName = role != FolderRole.None ? role.GermanName() : DisplayPath(f.FullName, f.DirectorySeparator),
                    Role = role,
                    Unread = f.Unread,
                    Total = f.Count,
                    Separator = f.DirectorySeparator,
                });
            }

            return result
                .OrderBy(f => f.Role == FolderRole.Inbox ? 0 : f.Role != FolderRole.None ? 1 : 2)
                .ThenBy(f => f.Role == FolderRole.None ? 0 : IndexOf(FolderRoles.SidebarOrder, f.Role))
                .ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, ct);
    }

    private static int IndexOf(IReadOnlyList<FolderRole> list, FolderRole role)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == role) return i;
        return int.MaxValue;
    }

    internal static IReadOnlyCollection<string> SpecialUse(FolderAttributes a)
    {
        var list = new List<string>();
        if (a.HasFlag(FolderAttributes.All)) list.Add("\\All");
        if (a.HasFlag(FolderAttributes.Archive)) list.Add("\\Archive");
        if (a.HasFlag(FolderAttributes.Drafts)) list.Add("\\Drafts");
        if (a.HasFlag(FolderAttributes.Flagged)) list.Add("\\Flagged");
        if (a.HasFlag(FolderAttributes.Junk)) list.Add("\\Junk");
        if (a.HasFlag(FolderAttributes.Sent)) list.Add("\\Sent");
        if (a.HasFlag(FolderAttributes.Trash)) list.Add("\\Trash");
        if (a.HasFlag(FolderAttributes.Important)) list.Add("\\Important");
        return list;
    }

    /// <summary>Anzeigename: Pfad mit " / " getrennt, ohne führendes "INBOX" (z. B. Hostinger "INBOX.Projekte").</summary>
    internal static string DisplayPath(string fullName, char separator)
    {
        var parts = separator == '\0' ? new[] { fullName } : fullName.Split(separator);
        if (parts.Length > 1 && string.Equals(parts[0], "INBOX", StringComparison.OrdinalIgnoreCase)) parts = parts[1..];
        return string.Join(" / ", parts);
    }

    // ----------------------------------------------------------- Nachrichten

    /// <summary>Neueste 200 INBOX-Nachrichten inkl. Kategorie (Gmail: X-GM-RAW, sonst lokaler Klassifizierer).</summary>
    public Task<IReadOnlyList<MessageSummary>> LoadInboxAsync(Account account, CancellationToken ct = default)
    {
        var session = Session(account);
        var provider = session.Provider;
        return session.RunAsync<IReadOnlyList<MessageSummary>>(async (client, token) =>
        {
            var inbox = InboxOf(client);
            await OpenAsync(inbox, token).ConfigureAwait(false);
            await client.NoOpAsync(token).ConfigureAwait(false); // neue EXISTS-Antworten abholen, falls der Ordner schon offen war
            var summaries = await FetchNewestAsync(inbox, _options.InboxFetchCount, withHeaders: true, token).ConfigureAwait(false);
            if (summaries.Count == 0) return Array.Empty<MessageSummary>();

            Dictionary<uint, MessageCategory>? serverCategories = null;
            if (provider.UsesGmailCategories)
                serverCategories = await GmailCategoriesAsync(inbox, summaries, token).ConfigureAwait(false);

            var classifier = new CategoryClassifier(_categories, _senderRules);
            return summaries
                .Select(s =>
                {
                    var model = ToModel(account.Id, inbox.FullName, s);
                    MessageCategory? server = null;
                    if (serverCategories is not null)
                        server = serverCategories.TryGetValue(s.UniqueId.Id, out var c) ? c : MessageCategory.Primary;
                    model.Category = classifier.Classify(new ClassificationInput(model.FromAddress, model.Subject, HeadersOf(s), server));
                    return model;
                })
                .OrderByDescending(m => m.Date)
                .ToList();
        }, ct);
    }

    /// <summary>
    /// Gmail: je Kategorie (außer primary) <c>UID SEARCH UID min:max X-GM-RAW "category:…"</c>.
    /// Treffer bekommen die Kategorie, der Rest wird primary. Schlägt die Suche fehl, null → lokaler Klassifizierer.
    /// </summary>
    private async Task<Dictionary<uint, MessageCategory>?> GmailCategoriesAsync(IMailFolder inbox, IList<IMessageSummary> summaries, CancellationToken ct)
    {
        var min = summaries.Min(s => s.UniqueId.Id);
        var max = summaries.Max(s => s.UniqueId.Id);
        var range = new UniqueIdRange(new UniqueId(inbox.UidValidity, min), new UniqueId(inbox.UidValidity, max));
        var result = new Dictionary<uint, MessageCategory>();
        try
        {
            foreach (var category in new[] { MessageCategory.Promotions, MessageCategory.Social, MessageCategory.Updates, MessageCategory.Forums })
            {
                var query = SearchQuery.Uids(range).And(SearchQuery.GMailRawSearch(_categories.GmailQuery(category)));
                var hits = await inbox.SearchAsync(query, ct).ConfigureAwait(false);
                foreach (var uid in hits) result.TryAdd(uid.Id, category);
            }
            return result;
        }
        catch (ImapCommandException)
        {
            return null;
        }
    }

    /// <summary>Neueste 100 Nachrichten eines anderen Ordners (ohne Kategorisierung).</summary>
    public Task<IReadOnlyList<MessageSummary>> LoadFolderAsync(Account account, string folderPath, CancellationToken ct = default)
    {
        var session = Session(account);
        return session.RunAsync<IReadOnlyList<MessageSummary>>(async (client, token) =>
        {
            var folder = await GetFolderAsync(client, folderPath, token).ConfigureAwait(false);
            await OpenAsync(folder, token).ConfigureAwait(false);
            await client.NoOpAsync(token).ConfigureAwait(false);
            var summaries = await FetchNewestAsync(folder, _options.FolderFetchCount, withHeaders: false, token).ConfigureAwait(false);
            return summaries.Select(s => ToModel(account.Id, folder.FullName, s)).OrderByDescending(m => m.Date).ToList();
        }, ct);
    }

    private async Task<IList<IMessageSummary>> FetchNewestAsync(IMailFolder folder, int count, bool withHeaders, CancellationToken ct)
    {
        if (folder.Count == 0) return new List<IMessageSummary>();
        var items = MessageSummaryItems.UniqueId | MessageSummaryItems.Flags | MessageSummaryItems.InternalDate
                    | MessageSummaryItems.Size | MessageSummaryItems.Envelope;
        if (_options.FetchPreviews) items |= MessageSummaryItems.PreviewText;
        var request = new FetchRequest(items);
        if (withHeaders) request.Headers = new HeaderSet(ClassifierHeaders);
        var start = Math.Max(0, folder.Count - count);
        return await folder.FetchAsync(start, -1, request, ct).ConfigureAwait(false);
    }

    private static Dictionary<string, string> HeadersOf(IMessageSummary s)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (s.Headers is null) return dict;
        foreach (var header in s.Headers)
        {
            dict[header.Field] = dict.TryGetValue(header.Field, out var existing) ? existing + ", " + header.Value : header.Value;
        }
        return dict;
    }

    private static MessageSummary ToModel(string accountId, string folderPath, IMessageSummary s)
    {
        var envelope = s.Envelope;
        var from = envelope?.From?.Mailboxes.FirstOrDefault();
        var flags = s.Flags ?? MessageFlags.None;
        return new MessageSummary
        {
            AccountId = accountId,
            FolderPath = folderPath,
            Uid = s.UniqueId.Id,
            FromName = from?.Name ?? "",
            FromAddress = from?.Address ?? "",
            To = envelope?.To is null ? "" : string.Join(", ", envelope.To.Mailboxes.Select(m => string.IsNullOrEmpty(m.Name) ? m.Address : m.Name)),
            Subject = envelope?.Subject ?? "",
            Date = envelope?.Date ?? s.InternalDate ?? DateTimeOffset.MinValue,
            Seen = flags.HasFlag(MessageFlags.Seen),
            Flagged = flags.HasFlag(MessageFlags.Flagged),
            Preview = (s.PreviewText ?? "").Trim(),
        };
    }

    /// <summary>Lädt die vollständige Nachricht; markiert sie beim Öffnen als gelesen (<c>UID STORE +FLAGS (\Seen)</c>).</summary>
    public Task<MessageDetail> FetchMessageAsync(Account account, string folderPath, uint uid, bool markSeen = true, CancellationToken ct = default)
    {
        var session = Session(account);
        return session.RunAsync(async (client, token) =>
        {
            var folder = await GetFolderAsync(client, folderPath, token).ConfigureAwait(false);
            await OpenAsync(folder, token).ConfigureAwait(false);
            var id = new UniqueId(uid);
            var message = await folder.GetMessageAsync(id, token).ConfigureAwait(false);
            if (markSeen && folder.Access == FolderAccess.ReadWrite)
                await folder.AddFlagsAsync(id, MessageFlags.Seen, true, token).ConfigureAwait(false);
            return ToDetail(account.Id, folder.FullName, uid, message);
        }, ct);
    }

    internal static MessageDetail ToDetail(string accountId, string folderPath, uint uid, MimeMessage message)
    {
        var attachments = new List<AttachmentInfo>();
        foreach (var entity in message.Attachments)
        {
            if (entity is MimePart part && part.Content is not null)
            {
                using var ms = new MemoryStream();
                part.Content.DecodeTo(ms);
                attachments.Add(new AttachmentInfo
                {
                    FileName = part.FileName ?? "Anhang",
                    ContentType = part.ContentType.MimeType,
                    Content = ms.ToArray(),
                });
            }
            else if (entity is MessagePart rfc822 && rfc822.Message is not null)
            {
                using var ms = new MemoryStream();
                rfc822.Message.WriteTo(ms);
                attachments.Add(new AttachmentInfo
                {
                    FileName = (rfc822.ContentDisposition?.FileName ?? rfc822.Message.Subject ?? "Nachricht") + ".eml",
                    ContentType = "message/rfc822",
                    Content = ms.ToArray(),
                });
            }
        }

        var inline = new Dictionary<string, InlineResource>(StringComparer.OrdinalIgnoreCase);
        long inlineBytes = 0;
        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            if (string.IsNullOrEmpty(part.ContentId) || part.Content is null) continue;
            if (!part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)) continue;
            using var ms = new MemoryStream();
            part.Content.DecodeTo(ms);
            inlineBytes += ms.Length;
            if (inlineBytes > 20 * 1024 * 1024) break;
            inline[part.ContentId.Trim('<', '>')] = new InlineResource(part.ContentType.MimeType, ms.ToArray());
        }

        static List<MailAddress> Addresses(InternetAddressList list) =>
            list.Mailboxes.Select(m => new MailAddress(string.IsNullOrEmpty(m.Name) ? null : m.Name, m.Address)).ToList();

        var from = message.From.Mailboxes.FirstOrDefault();
        return new MessageDetail
        {
            AccountId = accountId,
            FolderPath = folderPath,
            Uid = uid,
            Subject = message.Subject ?? "",
            From = from is null ? null : new MailAddress(string.IsNullOrEmpty(from.Name) ? null : from.Name, from.Address),
            To = Addresses(message.To),
            Cc = Addresses(message.Cc),
            ReplyTo = Addresses(message.ReplyTo),
            Date = message.Date,
            MessageId = message.MessageId,
            References = message.References.ToList(),
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            Attachments = attachments,
            InlineResources = inline,
        };
    }

    // --------------------------------------------------------------- Aktionen

    public Task SetSeenAsync(Account account, string folderPath, uint uid, bool seen, CancellationToken ct = default) =>
        SetFlagAsync(account, folderPath, uid, MessageFlags.Seen, seen, ct);

    public Task SetFlaggedAsync(Account account, string folderPath, uint uid, bool flagged, CancellationToken ct = default) =>
        SetFlagAsync(account, folderPath, uid, MessageFlags.Flagged, flagged, ct);

    private Task SetFlagAsync(Account account, string folderPath, uint uid, MessageFlags flag, bool value, CancellationToken ct) =>
        Session(account).RunAsync(async (client, token) =>
        {
            var folder = await GetFolderAsync(client, folderPath, token).ConfigureAwait(false);
            await OpenAsync(folder, token, requireWrite: true).ConfigureAwait(false);
            var id = new UniqueId(uid);
            if (value) await folder.AddFlagsAsync(id, flag, true, token).ConfigureAwait(false);
            else await folder.RemoveFlagsAsync(id, flag, true, token).ConfigureAwait(false);
        }, ct);

    /// <summary>Verschiebt in den Ordner mit der Rolle (Archivieren, Spam, Kein Spam → Inbox, Löschen → Papierkorb).</summary>
    public async Task MoveToRoleAsync(Account account, string folderPath, uint uid, FolderRole target, CancellationToken ct = default)
    {
        var session = Session(account);
        await EnsureRolesAsync(account, ct).ConfigureAwait(false);
        await session.RunAsync(async (client, token) =>
        {
            var targetPath = target == FolderRole.Inbox ? "INBOX" : session.RolePaths.GetValueOrDefault(target);
            if (string.IsNullOrEmpty(targetPath))
                throw new MailServiceException($"Kein Ordner „{target.GermanName()}“ gefunden.");
            if (string.Equals(targetPath, folderPath, StringComparison.Ordinal)) return;

            var source = await GetFolderAsync(client, folderPath, token).ConfigureAwait(false);
            var destination = await GetFolderAsync(client, targetPath, token).ConfigureAwait(false);
            await OpenAsync(source, token, requireWrite: true).ConfigureAwait(false);
            await MoveAsync(client, source, new UniqueId(uid), destination, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>UID MOVE</c>, falls MOVE angeboten wird, sonst <c>UID COPY</c> + <c>+FLAGS (\Deleted)</c> + <c>UID EXPUNGE</c>/<c>EXPUNGE</c>.
    /// </summary>
    private static async Task MoveAsync(ImapClient client, IMailFolder source, UniqueId uid, IMailFolder destination, CancellationToken ct)
    {
        if (client.Capabilities.HasFlag(ImapCapabilities.Move))
        {
            await source.MoveToAsync(uid, destination, ct).ConfigureAwait(false);
            return;
        }
        await source.CopyToAsync(uid, destination, ct).ConfigureAwait(false);
        await ExpungeAsync(client, source, uid, ct).ConfigureAwait(false);
    }

    private static async Task ExpungeAsync(ImapClient client, IMailFolder folder, UniqueId uid, CancellationToken ct)
    {
        await folder.AddFlagsAsync(uid, MessageFlags.Deleted, true, ct).ConfigureAwait(false);
        if (client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
            await folder.ExpungeAsync(new[] { uid }, ct).ConfigureAwait(false);
        else
            await folder.ExpungeAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Löschen: in den Papierkorb verschieben; im Papierkorb endgültig löschen.</summary>
    public async Task DeleteAsync(Account account, string folderPath, uint uid, CancellationToken ct = default)
    {
        var session = Session(account);
        await EnsureRolesAsync(account, ct).ConfigureAwait(false);
        var trash = session.RolePaths.GetValueOrDefault(FolderRole.Trash);
        if (trash is null)
            throw new MailServiceException("Kein Papierkorb gefunden – die Nachricht wurde nicht gelöscht.");

        if (!string.Equals(trash, folderPath, StringComparison.Ordinal))
        {
            await MoveToRoleAsync(account, folderPath, uid, FolderRole.Trash, ct).ConfigureAwait(false);
            return;
        }

        // Im Papierkorb: endgültig löschen.
        await session.RunAsync(async (client, token) =>
        {
            var folder = await GetFolderAsync(client, folderPath, token).ConfigureAwait(false);
            await OpenAsync(folder, token, requireWrite: true).ConfigureAwait(false);
            await ExpungeAsync(client, folder, new UniqueId(uid), token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Rolle eines Ordnerpfads (nach <see cref="ListFoldersAsync"/> bekannt).</summary>
    public FolderRole RoleOf(Account account, string folderPath)
    {
        if (string.Equals(folderPath, "INBOX", StringComparison.OrdinalIgnoreCase)) return FolderRole.Inbox;
        foreach (var (role, path) in Session(account).RolePaths)
            if (string.Equals(path, folderPath, StringComparison.Ordinal)) return role;
        return FolderRole.None;
    }

    private async Task EnsureRolesAsync(Account account, CancellationToken ct)
    {
        if (Session(account).RolePaths.Count == 0) await ListFoldersAsync(account, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ Senden

    /// <summary>Erstellt die MIME-Nachricht (Klartext, UTF-8).</summary>
    public static MimeMessage BuildMimeMessage(Account account, OutgoingMessage outgoing)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(account.DisplayName) ? null : account.DisplayName, account.Email));
        AddAddresses(message.To, outgoing.To, "An");
        AddAddresses(message.Cc, outgoing.Cc, "Cc");
        AddAddresses(message.Bcc, outgoing.Bcc, "Bcc");
        if (message.To.Count + message.Cc.Count + message.Bcc.Count == 0)
            throw new MailServiceException("Bitte mindestens einen Empfänger angeben.");
        message.Subject = outgoing.Subject ?? "";
        message.Date = DateTimeOffset.Now;
        var domain = Catalog.EmailParts.GetDomain(account.Email);
        message.MessageId = MimeUtils.GenerateMessageId(string.IsNullOrEmpty(domain) ? "lumail.local" : domain);
        if (!string.IsNullOrEmpty(outgoing.InReplyTo)) message.InReplyTo = outgoing.InReplyTo;
        foreach (var reference in outgoing.References.Where(r => !string.IsNullOrWhiteSpace(r))) message.References.Add(reference);
        message.Body = new TextPart("plain") { Text = outgoing.Body ?? "" };
        return message;
    }

    private static void AddAddresses(InternetAddressList target, string? list, string field)
    {
        if (string.IsNullOrWhiteSpace(list)) return;
        var normalized = list.Replace(';', ',');
        if (!InternetAddressList.TryParse(normalized, out var parsed))
            throw new MailServiceException($"Ungültige Adresse im Feld „{field}“: {list}");
        target.AddRange(parsed);
    }

    /// <summary>
    /// Sendet per SMTP (gleiche Authentifizierung wie IMAP). Bei <c>saveSentViaImap</c> wird die Nachricht danach
    /// per IMAP APPEND (\Seen) im Ordner mit Rolle "sent" abgelegt.
    /// </summary>
    public async Task SendAsync(Account account, OutgoingMessage outgoing, CancellationToken ct = default)
    {
        var provider = _providers.Get(account.ProviderId);
        var message = BuildMimeMessage(account, outgoing);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var credential = await _credentials.GetCredentialAsync(account, provider, attempt > 0, ct).ConfigureAwait(false);
                try
                {
                    using var smtp = await MailConnector.ConnectSmtpAsync(provider, credential, _options.Timeout, ct).ConfigureAwait(false);
                    await smtp.SendAsync(message, ct).ConfigureAwait(false);
                    await smtp.DisconnectAsync(true, ct).ConfigureAwait(false);
                    break;
                }
                catch (AuthenticationException) when (attempt == 0 && credential.IsOAuth)
                {
                    // Token evtl. widerrufen/abgelaufen → einmal mit frischem Token versuchen.
                }
            }
        }
        catch (Exception ex) when (ex is not MailServiceException)
        {
            throw MailConnector.Translate(ex, provider, account);
        }

        if (provider.SaveSentViaImap) await AppendToSentAsync(account, message, ct).ConfigureAwait(false);
    }

    private async Task AppendToSentAsync(Account account, MimeMessage message, CancellationToken ct)
    {
        var session = Session(account);
        await EnsureRolesAsync(account, ct).ConfigureAwait(false);
        var sentPath = session.RolePaths.GetValueOrDefault(FolderRole.Sent);
        if (sentPath is null)
            throw new MailServiceException("Die Nachricht wurde gesendet, aber es gibt keinen Ordner „Gesendet“ für eine Kopie.");
        await session.RunAsync(async (client, token) =>
        {
            var sent = await GetFolderAsync(client, sentPath, token).ConfigureAwait(false);
            await sent.AppendAsync(message, MessageFlags.Seen, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------- Zugangsprüfung

    /// <summary>Testet Zugangsdaten per IMAP-LOGIN. Liefert null bei Erfolg, sonst eine deutsche Fehlermeldung.</summary>
    public async Task<string?> VerifyPasswordAsync(Provider provider, string email, string password, CancellationToken ct = default)
    {
        try
        {
            using var client = await MailConnector.ConnectImapAsync(provider, new MailCredential(email, password, false), _options.Timeout, ct)
                .ConfigureAwait(false);
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);
            return null;
        }
        catch (AuthenticationException)
        {
            return $"Anmeldung fehlgeschlagen: E-Mail-Adresse oder {provider.PasswordLabel} ist falsch.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MailConnector.Translate(ex, provider).Message;
        }
    }

    // ------------------------------------------------------------------ Helfer

    private static async Task<IMailFolder> GetFolderAsync(ImapClient client, string path, CancellationToken ct) =>
        string.Equals(path, "INBOX", StringComparison.OrdinalIgnoreCase)
            ? InboxOf(client)
            : await client.GetFolderAsync(path, ct).ConfigureAwait(false);

    private static IMailFolder InboxOf(ImapClient client) =>
        client.Inbox ?? throw new MailServiceException("Der Server liefert keinen Posteingang (INBOX).");

    /// <summary>Öffnet den Ordner (bevorzugt Lesen/Schreiben, sonst nur Lesen).</summary>
    private static async Task OpenAsync(IMailFolder folder, CancellationToken ct, bool requireWrite = false)
    {
        if (folder.IsOpen && folder.Access == FolderAccess.ReadWrite) return;
        if (folder.IsOpen && !requireWrite) return;
        try
        {
            await folder.OpenAsync(FolderAccess.ReadWrite, ct).ConfigureAwait(false);
        }
        catch (ImapCommandException) when (!requireWrite)
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values) await session.DisposeAsync().ConfigureAwait(false);
        _sessions.Clear();
    }
}

/// <summary>Eine IMAP-Verbindung pro Konto; alle Operationen laufen nacheinander.</summary>
internal sealed class AccountSession : IAsyncDisposable
{
    private readonly IMailCredentialSource _credentials;
    private readonly MailServiceOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImapClient? _client;

    public Account Account { get; }
    public Provider Provider { get; }
    /// <summary>Rolle → Ordnerpfad (aus dem letzten LIST).</summary>
    public Dictionary<FolderRole, string> RolePaths { get; set; } = new();

    public AccountSession(Account account, Provider provider, IMailCredentialSource credentials, MailServiceOptions options)
    {
        Account = account;
        Provider = provider;
        _credentials = credentials;
        _options = options;
    }

    public async Task<T> RunAsync<T>(Func<ImapClient, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var forceRefresh = false;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var client = await EnsureConnectedAsync(forceRefresh, ct).ConfigureAwait(false);
                    return await operation(client, ct).ConfigureAwait(false);
                }
                catch (AuthenticationException) when (attempt == 0 && Account.IsOAuth)
                {
                    ResetClient();
                    forceRefresh = true;
                }
                catch (Exception ex) when (attempt == 0 && MailConnector.IsConnectionError(ex) && !ct.IsCancellationRequested)
                {
                    ResetClient();
                }
            }
        }
        catch (Exception ex) when (ex is not MailServiceException && !(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            throw MailConnector.Translate(ex, Provider, Account);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RunAsync(Func<ImapClient, CancellationToken, Task> operation, CancellationToken ct) =>
        RunAsync<bool>(async (c, t) => { await operation(c, t).ConfigureAwait(false); return true; }, ct);

    private async Task<ImapClient> EnsureConnectedAsync(bool forceRefresh, CancellationToken ct)
    {
        if (_client is { IsConnected: true, IsAuthenticated: true }) return _client;
        ResetClient();
        var credential = await _credentials.GetCredentialAsync(Account, Provider, forceRefresh, ct).ConfigureAwait(false);
        _client = await MailConnector.ConnectImapAsync(Provider, credential, _options.Timeout, ct).ConfigureAwait(false);
        return _client;
    }

    private void ResetClient()
    {
        var c = _client;
        _client = null;
        if (c is null) return;
        try
        {
            if (c.IsConnected) c.Disconnect(false);
        }
        catch (Exception)
        {
            // Verbindung ist ohnehin defekt.
        }
        c.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is { IsConnected: true })
            {
                try { await _client.DisconnectAsync(true).ConfigureAwait(false); }
                catch (Exception) { }
            }
            _client?.Dispose();
            _client = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
