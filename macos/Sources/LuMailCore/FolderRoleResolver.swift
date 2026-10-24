import Foundation

/// Ordnet Server-Ordnern Rollen zu (Gesendet, Spam, …) – erst per SPECIAL-USE, dann per Name.
public struct FolderRoleResolver: Sendable {
    let hints: [FolderRole: FolderRoleHints]

    public init(catalog: ProviderCatalog = .shared) {
        hints = catalog.folderRoles
    }

    public func assignRoles(_ folders: [MailFolder]) -> [MailFolder] {
        var result = folders.map { folder -> MailFolder in
            var folder = folder
            folder.role = nil
            return folder
        }
        var taken = Set<FolderRole>()
        let roles = FolderRole.allCases

        // INBOX ist laut RFC 3501 immer „INBOX“ (ohne Groß-/Kleinschreibung).
        if let index = result.firstIndex(where: { $0.path.caseInsensitiveCompare("INBOX") == .orderedSame }) {
            result[index].role = .inbox
            taken.insert(.inbox)
        }

        // 1. SPECIAL-USE (RFC 6154). Für „archive“ hat \Archive Vorrang vor \All (Gmail „Alle Nachrichten“).
        for role in roles where !taken.contains(role) {
            guard let specialUse = hints[role]?.specialUse, !specialUse.isEmpty else { continue }
            for flag in specialUse {
                if let index = result.firstIndex(where: { folder in
                    folder.role == nil && folder.isSelectable
                        && folder.attributes.contains { $0.caseInsensitiveCompare(flag) == .orderedSame }
                }) {
                    result[index].role = role
                    taken.insert(role)
                    break
                }
            }
        }

        // 2. Namen
        for role in roles where !taken.contains(role) {
            guard let names = hints[role]?.names else { continue }
            outer: for name in names {
                for index in result.indices where result[index].role == nil && result[index].isSelectable {
                    if Self.matches(folder: result[index], name: name) {
                        result[index].role = role
                        taken.insert(role)
                        break outer
                    }
                }
            }
        }
        return result
    }

    /// Ein Name passt, wenn der ganze Pfad oder (bei Namen ohne Trenner) der letzte Pfadteil passt.
    static func matches(folder: MailFolder, name: String) -> Bool {
        let path = folder.decodedPath
        if path.caseInsensitiveCompare(name) == .orderedSame { return true }
        guard let delimiter = folder.delimiter, !delimiter.isEmpty else { return false }
        let normalizedName = name.replacingOccurrences(of: "/", with: delimiter).replacingOccurrences(of: ".", with: delimiter)
        if path.caseInsensitiveCompare(normalizedName) == .orderedSame { return true }
        if name.contains("/") || name.contains(".") { return false }
        let parts = path.components(separatedBy: delimiter)
        // Nur direkte Unterordner von INBOX oder Ordner der obersten Ebene, damit „Projekte/Archiv“ nicht greift.
        guard parts.count == 1 || (parts.count == 2 && parts[0].caseInsensitiveCompare("INBOX") == .orderedSame) else { return false }
        return parts.last?.caseInsensitiveCompare(name) == .orderedSame
    }
}
