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
    public string ContactsFile => Path.Combine(BaseDirectory, "contacts.json");
    public string SnoozedFile => Path.Combine(BaseDirectory, "snoozed.json");
    public string OutboxDirectory => Path.Combine(BaseDirectory, "outbox");
    public string CacheDirectory => Path.Combine(BaseDirectory, "cache");

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

/// <summary>App-Einstellungen (<c>settings.json</c>).</summary>
public sealed class AppSettings
{
    public int RefreshIntervalMinutes { get; set; } = 3;
    public bool SeparateCategories { get; set; } = true;
    public bool AlwaysLoadExternalImages { get; set; }

    /// <summary>Einstellung „Updates“: "Automatic" (Standard), "NotifyOnly" oder "Off".</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public Update.UpdateMode Updates { get; set; } = Update.UpdateMode.Automatic;

    /// <summary>„Mitteilungen“ (an).</summary>
    public bool Notifications { get; set; } = true;

    /// <summary>„Offline-Cache“ (an).</summary>
    public bool OfflineCache { get; set; } = true;

    /// <summary>„Als Konversationen“ in der Nachrichtenliste.</summary>
    public bool Conversations { get; set; }

    /// <summary>„Aufräumen nach“ je Kategorie-Id (promotions/social/updates/forums) in Tagen; 0/fehlend = aus.</summary>
    public Dictionary<string, int> CleanupDays { get; set; } = new();

    /// <summary>„Lu-Mail sperren mit Windows Hello“ (aus).</summary>
    public bool AppLock { get; set; }

    /// <summary>Sperren nach so vielen Minuten im Hintergrund (5 / 15 / 60).</summary>
    public int AppLockMinutes { get; set; } = 5;

    /// <summary>Darstellung: "system", "light", "dark".</summary>
    public string ThemeMode { get; set; } = "system";

    /// <summary>Variante: "luna", "neumond", "vollmond".</summary>
    public string ThemeVariant { get; set; } = "luna";

    /// <summary>Akzentfarbe: Schlüssel aus lu-theme.json → accents, leer = Akzent der Variante.</summary>
    public string Accent { get; set; } = "";

    /// <summary>Web-Konten: Ansicht pro Konto-Id, "lu" (Standard) oder "web" (§21).</summary>
    public Dictionary<string, string> WebViewModes { get; set; } = new();

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
