using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace LuMail.Core.Update;

/// <summary>
/// Prüft GitHub-Releases auf neue Versionen und lädt das passende Paket mit SHA-256-Prüfung
/// (ARCHITEKTUR.md „Downloads &amp; Auto-Updater“, Schritte 1–4). Plattformneutral; die Installation macht die App.
/// </summary>
public sealed class UpdateService
{
    private readonly HttpClient _http;
    private readonly UpdateConfig _config;
    private readonly string _downloadDirectory;

    public SemanticVersion CurrentVersion { get; }
    public string PlatformKey { get; }

    /// <param name="http">HttpClient (in Tests mit Fake-Handler).</param>
    /// <param name="config">Inhalt von update.json.</param>
    /// <param name="currentVersion">Version der laufenden App.</param>
    /// <param name="platformKey">Schlüssel in assetSuffixes, z. B. "windows-x64".</param>
    /// <param name="downloadDirectory">Zielordner; Standard <c>%TEMP%\LuMail-Update</c>.</param>
    public UpdateService(HttpClient http, UpdateConfig config, SemanticVersion currentVersion, string platformKey,
        string? downloadDirectory = null)
    {
        _http = http;
        _config = config;
        CurrentVersion = currentVersion;
        PlatformKey = platformKey;
        _downloadDirectory = downloadDirectory ?? DefaultDownloadDirectory;
    }

    public static string DefaultDownloadDirectory => Path.Combine(Path.GetTempPath(), "LuMail-Update");

    public string UserAgent => $"Lu-Mail/{CurrentVersion}";

    private HttpRequestMessage NewRequest(string url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    /// <summary>
    /// Liefert das neueste Update, wenn es neuer als <see cref="CurrentVersion"/> ist, sonst null.
    /// Wirft <see cref="UpdateException"/>, wenn ein neueres Release kein passendes Paket oder keine Prüfsumme hat.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        GitHubRelease release;
        using (var request = NewRequest(_config.LatestReleaseApi, "application/vnd.github+json"))
        {
            using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null; // noch kein Release
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"Die Update-Prüfung ist fehlgeschlagen (HTTP {(int)response.StatusCode}).");
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                release = GitHubRelease.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new UpdateException("Die Antwort von GitHub ist ungültig.", ex);
            }
        }

        if (release.Draft || release.Prerelease) return null;
        if (!SemanticVersion.TryParse(release.TagName, out var version)) return null; // z. B. "v1.2.0-beta"
        if (version <= CurrentVersion) return null;

        if (!_config.AssetSuffixes.TryGetValue(PlatformKey, out var suffix) || string.IsNullOrEmpty(suffix))
            throw new UpdateException($"Für die Plattform „{PlatformKey}“ ist kein Paket konfiguriert.");
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    ?? throw new UpdateException($"Lu-Mail {version} enthält kein Paket für diese Plattform ({PlatformKey}).");

        var sumsAsset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, _config.ChecksumsAsset, StringComparison.OrdinalIgnoreCase))
                        ?? throw new UpdateException($"Lu-Mail {version}: {_config.ChecksumsAsset} fehlt – Update wird aus Sicherheitsgründen nicht geladen.");

        string sumsText;
        using (var request = NewRequest(sumsAsset.BrowserDownloadUrl, "application/octet-stream"))
        {
            using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"{_config.ChecksumsAsset} konnte nicht geladen werden (HTTP {(int)response.StatusCode}).");
            sumsText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        if (!Sha256Sums.Parse(sumsText).TryGetValue(asset.Name, out var sha))
            throw new UpdateException($"In {_config.ChecksumsAsset} fehlt die Prüfsumme für {asset.Name} – Update abgebrochen.");

        return new UpdateInfo
        {
            Version = version,
            TagName = release.TagName,
            Name = string.IsNullOrWhiteSpace(release.Name) ? $"Lu-Mail {version}" : release.Name!,
            Notes = release.Body ?? "",
            HtmlUrl = release.HtmlUrl,
            Asset = asset,
            Sha256 = sha,
        };
    }

    /// <summary>
    /// Lädt das Paket in den temporären Ordner und prüft die SHA-256-Summe.
    /// Bei Abweichung wird die Datei gelöscht und eine <see cref="UpdateException"/> geworfen.
    /// </summary>
    /// <param name="progress">Fortschritt 0…1.</param>
    /// <returns>Pfad der geprüften Datei.</returns>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_downloadDirectory);
        var fileName = Path.GetFileName(update.Asset.Name);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName is "." or "..")
            throw new UpdateException("Ungültiger Paketname.");
        var target = Path.Combine(_downloadDirectory, fileName);

        // Bereits vollständig geladen?
        if (File.Exists(target) && string.Equals(await HashFileAsync(target, ct).ConfigureAwait(false), update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(1);
            return target;
        }

        // Reste älterer Updates im eigenen Ordner entfernen.
        foreach (var stale in Directory.GetFiles(_downloadDirectory))
        {
            if (!string.Equals(stale, target, StringComparison.OrdinalIgnoreCase)) TryDelete(stale);
        }

        var partial = target + ".part";
        try
        {
            using var request = NewRequest(update.Asset.BrowserDownloadUrl, "application/octet-stream");
            using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"Download fehlgeschlagen (HTTP {(int)response.StatusCode}).");

            var total = response.Content.Headers.ContentLength ?? (update.Asset.Size > 0 ? update.Asset.Size : (long?)null);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total is > 0) progress?.Report(Math.Min(1.0, (double)done / total.Value));
                }
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partial);
                throw new UpdateException("Die Prüfsumme des heruntergeladenen Updates stimmt nicht – die Datei wurde verworfen.");
            }

            File.Move(partial, target, overwrite: true);
            progress?.Report(1);
            return target;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption option, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, option, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Keine Verbindung zum Update-Server: " + ex.Message, ex);
        }
    }

    internal static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var bytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
