using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuMail.Core.Models;
using LuMail.Core.Resources;
using MimeKit;

namespace LuMail.Core.Storage;

// ------------------------------------------------------------------ §5 Kontakte

/// <summary>Eintrag in contacts.json.</summary>
public sealed class ContactEntry
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public DateTimeOffset LastUsed { get; set; }
}

/// <summary>Eine Vorschlagszeile der Autovervollständigung.</summary>
public sealed record ContactSuggestion(string Address, string Name, int Count, DateTimeOffset LastUsed)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Address : $"{Name} <{Address}>";
}

/// <summary>
/// Kontakte (<c>contacts.json</c>: <c>{ "&lt;adresse&gt;": { "name", "count", "lastUsed" } }</c>), gesammelt aus Absendern,
/// Empfängern und gesendeten Mails. Eine Nachricht zählt nur, wenn sie neuer als <c>lastUsed</c> ist – so erhöht erneutes
/// Laden derselben Nachrichten die Zahl nicht.
/// </summary>
public sealed class ContactStore
{
    private readonly string? _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, ContactEntry> _contacts;
    private bool _dirty;

    public ContactStore(string? path)
    {
        _path = path;
        _contacts = new Dictionary<string, ContactEntry>(StringComparer.OrdinalIgnoreCase);
        if (path is null || !File.Exists(path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ContactEntry>>(File.ReadAllText(path), LuJson.Options);
            if (loaded is not null) foreach (var (k, v) in loaded) _contacts[Normalize(k)] = v;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
        }
    }

    public static ContactStore InMemory() => new(null);

    public int Count
    {
        get { lock (_gate) return _contacts.Count; }
    }

    public ContactEntry? Get(string address)
    {
        lock (_gate) return _contacts.TryGetValue(Normalize(address), out var c) ? c : null;
    }

    /// <summary>Adresse aus einer geladenen Nachricht (Absender oder Empfänger) mit deren Datum.</summary>
    public void Observe(string? address, string? name, DateTimeOffset date)
    {
        var key = Normalize(address);
        if (!IsAddress(key)) return;
        lock (_gate)
        {
            if (!_contacts.TryGetValue(key, out var entry))
            {
                _contacts[key] = new ContactEntry { Name = name?.Trim() ?? "", Count = 1, LastUsed = date };
                _dirty = true;
                return;
            }
            if (date > entry.LastUsed)
            {
                entry.Count++;
                entry.LastUsed = date;
                if (!string.IsNullOrWhiteSpace(name)) entry.Name = name.Trim();
                _dirty = true;
            }
            else if (string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(name))
            {
                entry.Name = name.Trim();
                _dirty = true;
            }
        }
    }

    /// <summary>Empfänger einer gesendeten Mail.</summary>
    public void RecordSent(string? address, string? name, DateTimeOffset now)
    {
        var key = Normalize(address);
        if (!IsAddress(key)) return;
        lock (_gate)
        {
            if (!_contacts.TryGetValue(key, out var entry)) _contacts[key] = entry = new ContactEntry();
            entry.Count++;
            if (now > entry.LastUsed) entry.LastUsed = now;
            if (!string.IsNullOrWhiteSpace(name)) entry.Name = name.Trim();
            _dirty = true;
        }
    }

    /// <summary>Treffer in Name oder Adresse, sortiert nach count, dann lastUsed (absteigend).</summary>
    public IReadOnlyList<ContactSuggestion> Suggest(string? query, int max = 8, ISet<string>? exclude = null)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return Array.Empty<ContactSuggestion>();
        lock (_gate)
        {
            return _contacts
                .Where(c => (exclude is null || !exclude.Contains(c.Key))
                            && (c.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Value.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(c => c.Value.Count).ThenByDescending(c => c.Value.LastUsed).ThenBy(c => c.Key, StringComparer.Ordinal)
                .Take(max)
                .Select(c => new ContactSuggestion(c.Key, c.Value.Name, c.Value.Count, c.Value.LastUsed))
                .ToList();
        }
    }

    public void Save()
    {
        if (_path is null) return;
        string json;
        lock (_gate)
        {
            if (!_dirty) return;
            json = JsonSerializer.Serialize(_contacts.OrderBy(c => c.Key, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.Value), LuJson.Options);
            _dirty = false;
        }
        JsonFile.WriteAtomic(_path, json);
    }

    private static string Normalize(string? s) => (s ?? "").Trim().Trim('<', '>').ToLowerInvariant();
    private static bool IsAddress(string s) => s.Length > 3 && s.Contains('@') && !s.Contains(' ');
}

// ----------------------------------------------------------- §13 Erinnerungen

/// <summary>Eintrag in snoozed.json: <c>{ "accountId", "folder", "messageId", "uid", "until" }</c>.</summary>
public sealed class SnoozeEntry
{
    public string AccountId { get; set; } = "";
    public string Folder { get; set; } = "INBOX";
    public string? MessageId { get; set; }
    public uint Uid { get; set; }
    public DateTimeOffset Until { get; set; }

    public bool Matches(string accountId, string folder, uint uid) =>
        AccountId == accountId && Folder == folder && Uid == uid;
}

/// <summary>Später erinnern (<c>snoozed.json</c>).</summary>
public sealed class SnoozeStore
{
    private readonly string? _path;
    private readonly object _gate = new();
    private List<SnoozeEntry> _entries = new();

    public SnoozeStore(string? path)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try { _entries = JsonSerializer.Deserialize<List<SnoozeEntry>>(File.ReadAllText(path), LuJson.Options) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { }
    }

    public static SnoozeStore InMemory() => new(null);

    public event EventHandler? Changed;

    public IReadOnlyList<SnoozeEntry> All
    {
        get { lock (_gate) return _entries.OrderBy(e => e.Until).ToList(); }
    }

    public bool IsSnoozed(string accountId, string folder, uint uid)
    {
        lock (_gate) return _entries.Any(e => e.Matches(accountId, folder, uid));
    }

    public void Add(SnoozeEntry entry)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Matches(entry.AccountId, entry.Folder, entry.Uid));
            _entries.Add(entry);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string accountId, string folder, uint uid)
    {
        bool removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e => e.Matches(accountId, folder, uid)) > 0;
            if (removed) Persist();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void RemoveAccount(string accountId)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.AccountId == accountId) > 0) Persist();
        }
    }

    /// <summary>Fällige Einträge (until ≤ now).</summary>
    public IReadOnlyList<SnoozeEntry> Due(DateTimeOffset now)
    {
        lock (_gate) return _entries.Where(e => e.Until <= now).ToList();
    }

    private void Persist()
    {
        if (_path is null) return;
        JsonFile.WriteAtomic(_path, JsonSerializer.Serialize(_entries, LuJson.Options));
    }
}

// --------------------------------------------------------------- §13 Geplant

/// <summary><c>outbox/&lt;id&gt;.json</c>.</summary>
public sealed class OutboxEntry
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public DateTimeOffset SendAt { get; set; }
    public List<string> Recipients { get; set; } = new();
    public string Subject { get; set; } = "";
}

/// <summary>Später senden: <c>outbox/&lt;id&gt;.eml</c> plus <c>outbox/&lt;id&gt;.json</c>.</summary>
public sealed class OutboxStore
{
    private readonly string _dir;

    public OutboxStore(string directory) => _dir = directory;

    public string Directory => _dir;

    public event EventHandler? Changed;

    public OutboxEntry Add(string accountId, DateTimeOffset sendAt, MimeMessage message)
    {
        System.IO.Directory.CreateDirectory(_dir);
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            SendAt = sendAt,
            Recipients = message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes).Select(m => m.Address).ToList(),
            Subject = message.Subject ?? "",
        };
        using (var stream = File.Create(EmlPath(entry.Id))) message.WriteTo(stream);
        JsonFile.WriteAtomic(JsonPath(entry.Id), JsonSerializer.Serialize(entry, LuJson.Options));
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public IReadOnlyList<OutboxEntry> All()
    {
        if (!System.IO.Directory.Exists(_dir)) return Array.Empty<OutboxEntry>();
        var list = new List<OutboxEntry>();
        foreach (var file in System.IO.Directory.GetFiles(_dir, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<OutboxEntry>(File.ReadAllText(file), LuJson.Options);
                if (entry is not null && File.Exists(EmlPath(entry.Id))) list.Add(entry);
            }
            catch (Exception ex) when (ex is JsonException or IOException) { }
        }
        return list.OrderBy(e => e.SendAt).ToList();
    }

    public IReadOnlyList<OutboxEntry> Due(DateTimeOffset now) => All().Where(e => e.SendAt <= now).ToList();

    public MimeMessage Load(string id)
    {
        using var stream = File.OpenRead(EmlPath(id));
        return MimeMessage.Load(stream);
    }

    public void Delete(string id)
    {
        foreach (var p in new[] { EmlPath(id), JsonPath(id) })
            if (File.Exists(p)) File.Delete(p);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveAccount(string accountId)
    {
        foreach (var e in All().Where(e => e.AccountId == accountId)) Delete(e.Id);
    }

    private string EmlPath(string id) => Path.Combine(_dir, Safe(id) + ".eml");
    private string JsonPath(string id) => Path.Combine(_dir, Safe(id) + ".json");
    private static string Safe(string id) => new(id.Where(char.IsAsciiLetterOrDigit).ToArray());
}

// ----------------------------------------------------------- §3 Offline-Cache

/// <summary>Inhalt von <c>cache/&lt;Konto-ID&gt;.json</c>.</summary>
public sealed class AccountSnapshot
{
    public DateTimeOffset SavedAt { get; set; }
    public List<MailFolder> Folders { get; set; } = new();
    /// <summary>Ordnerpfad → Nachrichtenübersichten.</summary>
    public Dictionary<string, List<MessageSummary>> Messages { get; set; } = new();
}

/// <summary>
/// Offline-Cache: Ordnerliste und Übersichten pro Konto (<c>cache/&lt;Konto-ID&gt;.json</c>), geöffnete Nachrichten als
/// <c>cache/&lt;Konto-ID&gt;/&lt;Ordner-Hash&gt;/&lt;UID&gt;.eml</c> (max. 200 MB, älteste zuerst gelöscht).
/// </summary>
public sealed class OfflineCache
{
    public const long DefaultMaxBytes = 200L * 1024 * 1024;
    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public OfflineCache(string directory, long maxBytes = DefaultMaxBytes)
    {
        _dir = directory;
        _maxBytes = maxBytes;
    }

    public string Directory => _dir;

    /// <summary>Ordner-Hash: die ersten 16 Hex-Zeichen von SHA-256(UTF-8(Ordnerpfad)).</summary>
    public static string FolderHash(string folderPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folderPath))).ToLowerInvariant()[..16];

    private static string SafeId(string accountId) => new(accountId.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());

    public string SnapshotPath(string accountId) => Path.Combine(_dir, SafeId(accountId) + ".json");

    public string MessagePath(string accountId, string folderPath, uint uid) =>
        Path.Combine(_dir, SafeId(accountId), FolderHash(folderPath), uid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".eml");

    public void SaveSnapshot(string accountId, AccountSnapshot snapshot)
    {
        lock (_gate) JsonFile.WriteAtomic(SnapshotPath(accountId), JsonSerializer.Serialize(snapshot, LuJson.Options));
    }

    public AccountSnapshot? LoadSnapshot(string accountId)
    {
        var path = SnapshotPath(accountId);
        if (!File.Exists(path)) return null;
        try
        {
            lock (_gate) return JsonSerializer.Deserialize<AccountSnapshot>(File.ReadAllText(path), LuJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public MimeMessage? TryLoadMessage(string accountId, string folderPath, uint uid)
    {
        var path = MessagePath(accountId, folderPath, uid);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return MimeMessage.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            return null;
        }
    }

    public void SaveMessage(string accountId, string folderPath, uint uid, MimeMessage message)
    {
        var path = MessagePath(accountId, folderPath, uid);
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            using (var stream = File.Create(tmp)) message.WriteTo(stream);
            File.Move(tmp, path, overwrite: true);
            EnforceLimitLocked();
        }
    }

    /// <summary>Größe aller gespeicherten Nachrichten (.eml) in Byte.</summary>
    public long MessageBytes
    {
        get
        {
            lock (_gate) return EmlFiles().Sum(f => f.Length);
        }
    }

    public void EnforceLimit()
    {
        lock (_gate) EnforceLimitLocked();
    }

    private IEnumerable<FileInfo> EmlFiles() =>
        System.IO.Directory.Exists(_dir)
            ? new DirectoryInfo(_dir).EnumerateFiles("*.eml", SearchOption.AllDirectories)
            : Enumerable.Empty<FileInfo>();

    private void EnforceLimitLocked()
    {
        var files = EmlFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= _maxBytes) break;
            try
            {
                total -= file.Length;
                file.Delete();
            }
            catch (IOException) { }
        }
    }

    public void RemoveAccount(string accountId)
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(SnapshotPath(accountId))) File.Delete(SnapshotPath(accountId));
                var dir = Path.Combine(_dir, SafeId(accountId));
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
            catch (IOException) { }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (System.IO.Directory.Exists(_dir)) System.IO.Directory.Delete(_dir, true);
            }
            catch (IOException) { }
        }
    }
}
