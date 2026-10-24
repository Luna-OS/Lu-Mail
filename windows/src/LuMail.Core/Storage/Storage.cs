using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using LuMail.Core.Catalog;
using LuMail.Core.Classification;
using LuMail.Core.Models;
using LuMail.Core.Resources;

namespace LuMail.Core.Storage;

/// <summary>Pfade im App-Datenordner (Windows: <c>%APPDATA%\LuMail</c>). Basisordner für Tests injizierbar.</summary>
public sealed class AppPaths
{
    public string BaseDirectory { get; }

    public AppPaths(string baseDirectory) => BaseDirectory = baseDirectory;

    /// <summary><c>%APPDATA%\LuMail</c> (bzw. <c>~/.config/LuMail</c> unter Linux).</summary>
    public static AppPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuMail"));

    public string AccountsFile => Path.Combine(BaseDirectory, "accounts.json");
    public string RulesFile => Path.Combine(BaseDirectory, "rules.json");
    public string OAuthFile => Path.Combine(BaseDirectory, "oauth.json");
    public string SecretsFile => Path.Combine(BaseDirectory, "secrets.bin");
    public string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    public AppPaths EnsureCreated()
    {
        Directory.CreateDirectory(BaseDirectory);
        return this;
    }
}

/// <summary>Sicherer Speicher für Passwörter und Refresh-Tokens (Windows: DPAPI-Datei, Tests: im Speicher).</summary>
public interface ISecretStore
{
    string? Get(string key);
    void Set(string key, string value);
    void Remove(string key);
}

public static class SecretKeys
{
    public static string Password(string accountId) => $"{accountId}/password";
    public static string RefreshToken(string accountId) => $"{accountId}/refresh_token";
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, string value) => _values[key] = value;
    public void Remove(string key) => _values.TryRemove(key, out _);
    public int Count => _values.Count;
}

/// <summary>Atomisches Schreiben (temporäre Datei + Umbenennen).</summary>
internal static class JsonFile
{
    public static void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public static void WriteAtomic(string path, byte[] content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Kontenliste ohne Geheimnisse (<c>accounts.json</c>).</summary>
public sealed class AccountStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public AccountStore(string path) => _path = path;

    public List<Account> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return new List<Account>();
            try
            {
                return JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(_path), LuJson.Options) ?? new List<Account>();
            }
            catch (JsonException)
            {
                return new List<Account>();
            }
        }
    }

    public void Save(IEnumerable<Account> accounts)
    {
        lock (_gate)
        {
            JsonFile.WriteAtomic(_path, JsonSerializer.Serialize(accounts.ToList(), LuJson.Options));
        }
    }

    /// <summary>Fügt hinzu oder ersetzt (gleiche Id).</summary>
    public void Upsert(Account account)
    {
        lock (_gate)
        {
            var list = Load();
            var idx = list.FindIndex(a => a.Id == account.Id);
            if (idx >= 0) list[idx] = account; else list.Add(account);
            Save(list);
        }
    }

    public void Remove(string accountId)
    {
        lock (_gate)
        {
            var list = Load();
            list.RemoveAll(a => a.Id == accountId);
            Save(list);
        }
    }
}

/// <summary>
/// Nutzer-Regeln „Absender immer als … einsortieren“ (<c>rules.json</c>), gelten für alle Konten.
/// Format: flaches Objekt Absender-Adresse oder Domain → Kategorie-Id, z. B.
/// <c>{ "news@shop.de": "promotions", "example.org": "social" }</c>.
/// Beim Lesen werden auch <c>{ "rules": { … } }</c> und <c>[{ "sender": …, "category": … }]</c> akzeptiert.
/// </summary>
public sealed class SenderRuleStore : ISenderRuleLookup
{
    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, MessageCategory> _rules;

    public SenderRuleStore(string? path)
    {
        _path = path;
        _rules = path is not null && File.Exists(path) ? Parse(SafeRead(path)) : new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Nur im Speicher (Tests).</summary>
    public static SenderRuleStore InMemory() => new(null);

    public event EventHandler? Changed;

    public IReadOnlyDictionary<string, MessageCategory> All
    {
        get { lock (_gate) return new Dictionary<string, MessageCategory>(_rules, StringComparer.OrdinalIgnoreCase); }
    }

    public void Set(string senderAddressOrDomain, MessageCategory category)
    {
        var key = Normalize(senderAddressOrDomain);
        if (key.Length == 0) return;
        lock (_gate)
        {
            _rules[key] = category;
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string senderAddressOrDomain)
    {
        bool removed;
        lock (_gate)
        {
            removed = _rules.Remove(Normalize(senderAddressOrDomain));
            if (removed) Persist();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>Exakte Adresse zuerst, dann Domain und übergeordnete Domains (z. B. mail.shop.de → shop.de).</summary>
    public MessageCategory? Lookup(string? senderAddress)
    {
        var address = Normalize(senderAddress);
        if (address.Length == 0) return null;
        lock (_gate)
        {
            if (_rules.Count == 0) return null;
            if (_rules.TryGetValue(address, out var exact)) return exact;
            var domain = EmailParts.GetDomain(address);
            while (domain.Contains('.'))
            {
                if (_rules.TryGetValue(domain, out var byDomain)) return byDomain;
                domain = domain[(domain.IndexOf('.') + 1)..];
            }
            return null;
        }
    }

    private static string Normalize(string? s) => (s ?? "").Trim().Trim('<', '>').ToLowerInvariant();

    private void Persist()
    {
        if (_path is null) return;
        var obj = _rules.OrderBy(r => r.Key, StringComparer.Ordinal).ToDictionary(r => r.Key, r => r.Value.ToId());
        JsonFile.WriteAtomic(_path, JsonSerializer.Serialize(obj, LuJson.Options));
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return "{}"; }
    }

    internal static Dictionary<string, MessageCategory> Parse(string json)
    {
        var result = new Dictionary<string, MessageCategory>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rules", out var nested)) root = nested;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in root.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String && MessageCategories.TryParse(p.Value.GetString(), out var c))
                        result[Normalize(p.Name)] = c;
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    string? sender = null, category = null;
                    foreach (var p in item.EnumerateObject())
                    {
                        if (p.Value.ValueKind != JsonValueKind.String) continue;
                        var name = p.Name.ToLowerInvariant();
                        if (name is "sender" or "address" or "email" or "domain" or "key" or "pattern") sender = p.Value.GetString();
                        else if (name is "category" or "categoryid") category = p.Value.GetString();
                    }
                    if (sender is not null && MessageCategories.TryParse(category, out var c)) result[Normalize(sender)] = c;
                }
            }
        }
        catch (JsonException)
        {
            // Defekte Datei: leere Regeln.
        }
        return result;
    }
}

/// <summary>App-Einstellungen (<c>settings.json</c>).</summary>
public sealed class AppSettings
{
    public int RefreshIntervalMinutes { get; set; } = 3;
    public bool SeparateCategories { get; set; } = true;
    public bool AlwaysLoadExternalImages { get; set; }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), LuJson.Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
        return new AppSettings();
    }

    public void Save(string path) => JsonFile.WriteAtomic(path, JsonSerializer.Serialize(this, LuJson.Options));
}
