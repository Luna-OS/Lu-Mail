using System.Text.Json;
using LuMail.Core.Models;
using LuMail.Core.Resources;

namespace LuMail.Core.Catalog;

/// <summary>Anbieterkatalog aus der eingebetteten <c>shared/providers.json</c>.</summary>
public sealed class ProviderCatalog
{
    private readonly Dictionary<string, Provider> _byId;

    public IReadOnlyList<Provider> Providers { get; }
    public IReadOnlyDictionary<FolderRole, FolderRoleDefinition> FolderRoles { get; }
    public DiscoverySettings Discovery { get; }

    private ProviderCatalog(ProviderCatalogDocument doc)
    {
        Providers = doc.Providers;
        Discovery = doc.Discovery ?? new DiscoverySettings();
        _byId = doc.Providers.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<FolderRole, FolderRoleDefinition>();
        foreach (var (key, def) in doc.FolderRoles)
        {
            if (FolderRoles_TryParse(key, out var role)) roles[role] = def;
        }
        FolderRoles = roles;
    }

    private static bool FolderRoles_TryParse(string key, out FolderRole role) => Models.FolderRoles.TryParse(key, out role);

    private static readonly Lazy<ProviderCatalog> EmbeddedInstance =
        new(() => Parse(SharedResources.ReadText(SharedResources.ProvidersFile)));

    /// <summary>Lädt den eingebetteten Katalog (gecacht).</summary>
    public static ProviderCatalog LoadEmbedded() => EmbeddedInstance.Value;

    public static ProviderCatalog Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<ProviderCatalogDocument>(json, LuJson.Options)
                  ?? throw new InvalidDataException("providers.json ist leer.");
        return new ProviderCatalog(doc);
    }

    public Provider? Find(string? id) => id is not null && _byId.TryGetValue(id, out var p) ? p : null;

    public Provider Get(string id) => Find(id) ?? throw new KeyNotFoundException($"Unbekannter Anbieter '{id}'.");

    /// <summary>
    /// Sucht den Anbieter zur Domain einer E-Mail-Adresse (oder einer Domain).
    /// Treffer: domain == d oder domain endet auf "." + d.
    /// </summary>
    public Provider? FindByEmailDomain(string? emailOrDomain)
    {
        if (string.IsNullOrWhiteSpace(emailOrDomain)) return null;
        var domain = EmailParts.GetDomain(emailOrDomain.Contains('@') ? emailOrDomain : "x@" + emailOrDomain);
        if (domain.Length == 0) return null;
        return Providers.FirstOrDefault(p => p.Domains.Any(d => EmailParts.DomainMatches(domain, d)));
    }
}

/// <summary>Hilfsfunktionen für E-Mail-Adressen.</summary>
public static class EmailParts
{
    /// <summary>Domain (klein geschrieben) – Teil nach dem letzten '@'.</summary>
    public static string GetDomain(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "";
        var a = address.Trim().Trim('<', '>');
        var at = a.LastIndexOf('@');
        return at < 0 ? "" : a[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
    }

    /// <summary>Lokaler Teil (klein geschrieben) – Teil vor dem letzten '@'.</summary>
    public static string GetLocalPart(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "";
        var a = address.Trim().Trim('<', '>');
        var at = a.LastIndexOf('@');
        return at < 0 ? "" : a[..at].Trim().ToLowerInvariant();
    }

    /// <summary>domain == d ODER domain endet auf '.' + d (case-insensitive).</summary>
    public static bool DomainMatches(string domain, string d)
    {
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(d)) return false;
        domain = domain.ToLowerInvariant();
        d = d.Trim().ToLowerInvariant();
        return domain == d || domain.EndsWith("." + d, StringComparison.Ordinal);
    }
}
