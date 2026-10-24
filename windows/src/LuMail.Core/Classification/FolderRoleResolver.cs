using LuMail.Core.Models;

namespace LuMail.Core.Classification;

/// <summary>Ein Server-Ordner aus IMAP LIST (plattformneutral, unabhängig von MailKit).</summary>
/// <param name="Path">Vollständiger Pfad, z. B. "[Gmail]/Sent Mail" oder "INBOX.Sent".</param>
/// <param name="Separator">Hierarchie-Trenner des Servers.</param>
/// <param name="SpecialUse">SPECIAL-USE-Attribute (RFC 6154), z. B. "\Sent".</param>
/// <param name="NoSelect">\Noselect bzw. \NonExistent.</param>
public sealed record FolderInfo(string Path, char Separator, IReadOnlyCollection<string> SpecialUse, bool NoSelect = false)
{
    public FolderInfo(string path, char separator = '/', params string[] specialUse)
        : this(path, separator, (IReadOnlyCollection<string>)specialUse) { }
}

/// <summary>
/// Ordnet Server-Ordnern Rollen zu (providers.json → folderRoles):
/// zuerst über SPECIAL-USE, sonst über den Namen (ohne Groß-/Kleinschreibung, Pfad-Trenner beachten).
/// Ein Ordner bekommt höchstens eine Rolle, jede Rolle höchstens einen Ordner.
/// \All zählt für "archive" nur, wenn kein Ordner \Archive trägt.
/// </summary>
public sealed class FolderRoleResolver
{
    private static readonly FolderRole[] Order =
    {
        FolderRole.Inbox, FolderRole.Sent, FolderRole.Drafts, FolderRole.Spam, FolderRole.Trash, FolderRole.Archive,
    };

    private readonly IReadOnlyDictionary<FolderRole, FolderRoleDefinition> _definitions;

    public FolderRoleResolver(IReadOnlyDictionary<FolderRole, FolderRoleDefinition> definitions)
    {
        _definitions = definitions;
    }

    /// <summary>Liefert Pfad → Rolle (nur Ordner mit Rolle).</summary>
    public IReadOnlyDictionary<string, FolderRole> Resolve(IEnumerable<FolderInfo> folders)
    {
        var selectable = folders.Where(f => !f.NoSelect).ToList();
        var assigned = new Dictionary<string, FolderRole>(StringComparer.Ordinal);
        var taken = new HashSet<FolderRole>();

        // Durchgang 1: SPECIAL-USE. Die Attribute werden in Listenreihenfolge geprüft,
        // daher gewinnt \Archive vor \All.
        foreach (var role in Order)
        {
            if (!_definitions.TryGetValue(role, out var def)) continue;
            foreach (var attribute in def.SpecialUse)
            {
                var match = selectable.FirstOrDefault(f =>
                    !assigned.ContainsKey(f.Path)
                    && f.SpecialUse.Any(a => string.Equals(a, attribute, StringComparison.OrdinalIgnoreCase)));
                if (match is null) continue;
                assigned[match.Path] = role;
                taken.Add(role);
                break;
            }
        }

        // Durchgang 2: Namen (für Rollen ohne SPECIAL-USE-Treffer).
        foreach (var role in Order)
        {
            if (taken.Contains(role) || !_definitions.TryGetValue(role, out var def)) continue;
            foreach (var name in def.Names)
            {
                var match = selectable.FirstOrDefault(f => !assigned.ContainsKey(f.Path) && NameMatches(f, name));
                if (match is null) continue;
                assigned[match.Path] = role;
                taken.Add(role);
                break;
            }
        }

        return assigned;
    }

    /// <summary>
    /// Vergleicht ohne Groß-/Kleinschreibung. Pfad-Trenner werden vereinheitlicht:
    /// der Server-Trenner im Pfad und '.'/'/' im Katalognamen gelten als Hierarchie-Trenner,
    /// so passt z. B. "INBOX.Sent" auch zu "INBOX/Sent" auf einem Server mit '/'.
    /// </summary>
    internal static bool NameMatches(FolderInfo folder, string name)
    {
        if (string.Equals(folder.Path, name, StringComparison.OrdinalIgnoreCase)) return true;
        var path = folder.Separator == '\0' ? folder.Path : folder.Path.Replace(folder.Separator, '/');
        var normalizedName = name.Replace('.', '/');
        return string.Equals(path, normalizedName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(path, name, StringComparison.OrdinalIgnoreCase);
    }
}
