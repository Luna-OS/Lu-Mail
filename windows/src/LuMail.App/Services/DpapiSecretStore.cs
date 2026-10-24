using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuMail.Core.Storage;

namespace LuMail.App.Services;

/// <summary>
/// Passwörter und Refresh-Tokens in <c>%APPDATA%\LuMail\secrets.bin</c>,
/// verschlüsselt mit DPAPI (<see cref="DataProtectionScope.CurrentUser"/>) – nur der angemeldete Windows-Benutzer kann sie lesen.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("de.lunaos.lumail/secrets/v1");

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _values;

    public DpapiSecretStore(string path)
    {
        _path = path;
        _values = Load(path);
    }

    public string? Get(string key)
    {
        lock (_gate) return _values.TryGetValue(key, out var v) ? v : null;
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
            Save();
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            if (_values.Remove(key)) Save();
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>();
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new Dictionary<string, string>();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            // Unlesbar (z. B. anderer Benutzer): Datei sichern und neu beginnen – Konten müssen neu angemeldet werden.
            try { File.Copy(path, path + ".unreadable", overwrite: true); } catch (IOException) { }
            return new Dictionary<string, string>();
        }
    }

    private void Save()
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(_values);
        try
        {
            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
