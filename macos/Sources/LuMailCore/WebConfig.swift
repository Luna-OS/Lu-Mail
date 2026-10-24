import Foundation

/// Webmail eines Anbieters für Web-Konten (docs/FUNKTIONEN.md §20).
public struct WebConfig: Codable, Hashable, Sendable {
    public var loginUrl: String
    public var homeUrl: String
    public var loggedInPrefixes: [String]
    /// Ordner-Schlüssel (Kategorie oder Rolle) → Webadresse.
    public var folders: [String: String]
    public var unreadTitlePattern: String?

    public init(loginUrl: String, homeUrl: String, loggedInPrefixes: [String], folders: [String: String],
                unreadTitlePattern: String? = nil) {
        self.loginUrl = loginUrl
        self.homeUrl = homeUrl
        self.loggedInPrefixes = loggedInPrefixes
        self.folders = folders
        self.unreadTitlePattern = unreadTitlePattern
    }

    /// Ein Eintrag in der Seitenleiste eines Web-Kontos.
    public struct Folder: Hashable, Sendable, Identifiable {
        public var key: String
        public var title: String
        public var url: String
        public var isCategory: Bool
        public var id: String { key }
    }

    /// Reihenfolge der Ordner in der Seitenleiste.
    public static let folderOrder: [(key: String, title: String)] = [
        ("primary", "Posteingang"), ("promotions", "Werbung"), ("social", "Soziale Netzwerke"),
        ("updates", "Updates"), ("forums", "Foren"), ("sent", "Gesendet"), ("drafts", "Entwürfe"),
        ("archive", "Archiv"), ("spam", "Spam"), ("trash", "Papierkorb"),
    ]

    /// Ordner in Sidebar-Reihenfolge; ohne getrennte Kategorien nur Posteingang und Rollen.
    public func sidebarFolders(separateCategories: Bool = true) -> [Folder] {
        Self.folderOrder.compactMap { entry in
            guard let url = folders[entry.key] else { return nil }
            let isCategory = MailCategory(rawValue: entry.key).isBuiltIn
            if isCategory, entry.key != "primary", !separateCategories { return nil }
            return Folder(key: entry.key, title: entry.title, url: url, isCategory: isCategory)
        }
    }

    public func folderURL(_ key: String) -> String {
        folders[key] ?? homeUrl
    }

    /// Ist die Anmeldung abgeschlossen? (Adresse beginnt mit einem der `loggedInPrefixes`.)
    public func isLoggedIn(url: String) -> Bool {
        loggedInPrefixes.contains { url.hasPrefix($0) }
    }

    /// Ist die Seite gerade im Posteingang? Nur dann zählt die Zahl im Titel als „ungelesen“.
    public func isOnInbox(url: String) -> Bool {
        let candidates = [folders["primary"], homeUrl].compactMap { $0 }
        return candidates.contains { url.hasPrefix($0) || url == Self.trimmingSlash($0) }
    }

    /// Anzahl ungelesener Mails aus dem Seitentitel, z. B. „Inbox (3) - name@gmail.com - Gmail“ → 3.
    public func unreadCount(fromTitle title: String) -> Int? {
        guard let pattern = unreadTitlePattern,
              let regex = try? NSRegularExpression(pattern: pattern) else { return nil }
        let range = NSRange(title.startIndex..., in: title)
        guard let match = regex.firstMatch(in: title, range: range), match.numberOfRanges > 1,
              let group = Range(match.range(at: 1), in: title) else { return nil }
        return Int(title[group])
    }

    /// Erste E-Mail-Adresse im Seitentitel (Gmail, Proton, … zeigen sie dort an).
    public static func email(fromTitle title: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: #"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}"#) else { return nil }
        let range = NSRange(title.startIndex..., in: title)
        guard let match = regex.firstMatch(in: title, range: range), let r = Range(match.range, in: title) else { return nil }
        return String(title[r])
    }

    /// Gehört die Adresse zum Anbieter? (Gleiche letzte zwei Host-Teile wie eine der Web-Adressen.)
    public func isInternal(url: String) -> Bool {
        guard let host = Self.host(of: url) else { return false }
        let site = Self.site(of: host)
        let known = ([loginUrl, homeUrl] + loggedInPrefixes + Array(folders.values)).compactMap(Self.host(of:)).map(Self.site(of:))
        return known.contains(site)
    }

    /// Weiterleitungs-Links wie „https://www.google.com/url?q=https://ziel.de“ → „https://ziel.de“.
    public static func redirectTarget(of url: String) -> String? {
        guard let components = URLComponents(string: url), components.path == "/url" else { return nil }
        let target = components.queryItems?.first { $0.name == "q" || $0.name == "url" }?.value
        guard let target, target.hasPrefix("http://") || target.hasPrefix("https://") else { return nil }
        return target
    }

    static func host(of url: String) -> String? {
        URL(string: url)?.host?.lowercased()
    }

    static func site(of host: String) -> String {
        host.split(separator: ".").suffix(2).joined(separator: ".")
    }

    static func trimmingSlash(_ url: String) -> String {
        url.hasSuffix("/") ? String(url.dropLast()) : url
    }
}
