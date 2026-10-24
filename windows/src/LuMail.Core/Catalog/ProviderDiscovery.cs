using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using LuMail.Core.Models;

namespace LuMail.Core.Catalog;

/// <summary>Ergebnis der Anbieter-Erkennung.</summary>
/// <param name="Provider">Erkannter Anbieter (sonst „Andere (IMAP)“).</param>
/// <param name="Servers">Bei „Andere (IMAP)“ über Autoconfig gefundene Server, sonst null.</param>
/// <param name="Source">"domain", "mx", "autoconfig" oder "none".</param>
public sealed record DiscoveryResult(Provider Provider, CustomServerSettings? Servers, string Source);

/// <summary>
/// Anbieter-Erkennung (FUNKTIONEN §17): bekannte Domain → MX per DNS-over-HTTPS (mxSuffixes) →
/// Autoconfig (Thunderbird-Format) → „Andere (IMAP)“. Gesamtdauer höchstens <c>discovery.timeoutSeconds</c>.
/// </summary>
public sealed class ProviderDiscovery
{
    private readonly ProviderCatalog _catalog;
    private readonly HttpClient _http;

    public ProviderDiscovery(ProviderCatalog catalog, HttpClient http)
    {
        _catalog = catalog;
        _http = http;
    }

    private Provider Custom => _catalog.Providers.FirstOrDefault(p => p.CustomServers) ?? _catalog.Providers[^1];

    public async Task<DiscoveryResult> DiscoverAsync(string email, CancellationToken ct = default)
    {
        var domain = EmailParts.GetDomain(email);
        if (domain.Length == 0) return new DiscoveryResult(Custom, null, "none");

        // 1. Bekannte Domain – ohne Netzwerk.
        if (_catalog.FindByEmailDomain(email) is { } known) return new DiscoveryResult(known, null, "domain");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _catalog.Discovery.TimeoutSeconds)));
        try
        {
            // 2. MX per DNS-over-HTTPS
            foreach (var host in await LookupMxAsync(domain, timeout.Token).ConfigureAwait(false))
            {
                var byMx = _catalog.Providers.FirstOrDefault(p => p.MxSuffixes.Any(s => EmailParts.DomainMatches(host, s)));
                if (byMx is not null) return new DiscoveryResult(byMx, null, "mx");
            }

            // 3. Autoconfig
            foreach (var template in _catalog.Discovery.Autoconfig)
            {
                var url = template.Replace("{domain}", Uri.EscapeDataString(domain), StringComparison.Ordinal)
                                  .Replace("{email}", Uri.EscapeDataString(email.Trim()), StringComparison.Ordinal);
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
                var servers = await TryAutoconfigAsync(uri, domain, email.Trim(), timeout.Token).ConfigureAwait(false);
                if (servers is not null) return new DiscoveryResult(Custom, servers, "autoconfig");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Zeitüberschreitung → „Andere (IMAP)“
        }
        return new DiscoveryResult(Custom, null, "none");
    }

    /// <summary>MX-Hosts (klein, ohne Punkt am Ende) nach Priorität.</summary>
    internal async Task<IReadOnlyList<string>> LookupMxAsync(string domain, CancellationToken ct)
    {
        var url = _catalog.Discovery.MxLookup.Replace("{domain}", Uri.EscapeDataString(domain), StringComparison.Ordinal);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Array.Empty<string>();
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseMx(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>„10 mx1.example.com.“ aus <c>Answer[].data</c> → (10, "mx1.example.com"), nach Priorität sortiert.</summary>
    internal static IReadOnlyList<string> ParseMx(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Answer", out var answers) || answers.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<(int Priority, string Host)>();
        foreach (var a in answers.EnumerateArray())
        {
            if (!a.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String) continue;
            var parts = (data.GetString() ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var host = parts[^1].TrimEnd('.').ToLowerInvariant();
            var priority = parts.Length > 1 && int.TryParse(parts[0], out var p) ? p : 0;
            if (host.Length > 0) list.Add((priority, host));
        }
        return list.OrderBy(x => x.Priority).Select(x => x.Host).ToList();
    }

    private async Task<CustomServerSettings?> TryAutoconfigAsync(Uri uri, string domain, string email, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(uri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseAutoconfig(xml, domain, email);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Erster IMAP- und SMTP-Server mit socketType SSL (→ tls) oder STARTTLS (→ starttls); unverschlüsselte werden ignoriert.</summary>
    public static CustomServerSettings? ParseAutoconfig(string xml, string domain, string email)
    {
        var doc = XDocument.Parse(xml);
        ServerEndpoint? Pick(string element, string type) =>
            doc.Descendants().Where(e => e.Name.LocalName == element && (string?)e.Attribute("type") == type)
                .Select(e => ToEndpoint(e, domain, email)).FirstOrDefault(ep => ep is not null);
        var imap = Pick("incomingServer", "imap");
        var smtp = Pick("outgoingServer", "smtp");
        return imap is null || smtp is null ? null : new CustomServerSettings { Imap = imap, Smtp = smtp };
    }

    private static ServerEndpoint? ToEndpoint(XElement server, string domain, string email)
    {
        string? Child(string name) => server.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
        var security = (Child("socketType") ?? "").ToUpperInvariant() switch
        {
            "SSL" => "tls",
            "STARTTLS" => "starttls",
            _ => null,
        };
        var host = (Child("hostname") ?? "").Replace("%EMAILDOMAIN%", domain, StringComparison.OrdinalIgnoreCase)
                                             .Replace("%EMAILADDRESS%", email, StringComparison.OrdinalIgnoreCase);
        if (security is null || host.Length == 0 || host.Contains('%')) return null;
        if (!int.TryParse(Child("port"), out var port) || port is <= 0 or > 65535) return null;
        return new ServerEndpoint { Host = host.ToLowerInvariant(), Port = port, Security = security };
    }
}

/// <summary>TCP-Test auf 127.0.0.1 (Proton Mail Bridge läuft?).</summary>
public static class LocalPortCheck
{
    public static async Task<bool> IsOpenAsync(int port, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(1));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
