import Foundation

/// Wie sich ein Konto anmeldet.
public enum AuthMethod: String, Codable, Sendable {
    case oauth2
    case password
    /// Anmeldung auf der Webseite des Anbieters, Postfach als Webmail (docs/FUNKTIONEN.md §20).
    case web
}

/// Ein E-Mail-Konto. Enthält keine Geheimnisse – die liegen im Schlüsselbund.
public struct Account: Codable, Hashable, Identifiable, Sendable {
    public var id: UUID
    public var providerId: String
    public var email: String
    public var displayName: String
    public var authMethod: AuthMethod
    /// Nur bei „Andere (IMAP)“.
    public var customServers: CustomServers?
    /// Signatur für neue Mails und Antworten (Text).
    public var signature: String?

    public init(id: UUID = UUID(), providerId: String, email: String, displayName: String, authMethod: AuthMethod,
                customServers: CustomServers? = nil, signature: String? = nil) {
        self.id = id
        self.providerId = providerId
        self.email = email
        self.displayName = displayName
        self.authMethod = authMethod
        self.customServers = customServers
        self.signature = signature
    }

    public var isWeb: Bool { authMethod == .web }

    /// Name in der Seitenleiste (Web-Konten kennen ihre Adresse evtl. noch nicht).
    public var label: String { email.isEmpty ? displayName : email }

    public func imapEndpoint(_ provider: Provider) -> ServerEndpoint { customServers?.imap ?? provider.imap }
    public func smtpEndpoint(_ provider: Provider) -> ServerEndpoint { customServers?.smtp ?? provider.smtp }
}

/// Ein virtueller Kategorie-Ordner unterhalb des Posteingangs – fest eingebaut oder selbst angelegt.
public struct MailCategory: RawRepresentable, Hashable, Codable, Sendable, CustomStringConvertible {
    public let rawValue: String

    public init(rawValue: String) {
        self.rawValue = rawValue
    }

    public static let primary = MailCategory(rawValue: "primary")
    public static let promotions = MailCategory(rawValue: "promotions")
    public static let social = MailCategory(rawValue: "social")
    public static let updates = MailCategory(rawValue: "updates")
    public static let forums = MailCategory(rawValue: "forums")

    /// Die fest eingebauten Kategorien in Sidebar-Reihenfolge.
    public static let builtIn: [MailCategory] = [.primary, .promotions, .social, .updates, .forums]

    public var isBuiltIn: Bool { Self.builtIn.contains(self) }
    public var description: String { rawValue }
}

/// Bekannte Rollen von Server-Ordnern.
public enum FolderRole: String, Codable, CaseIterable, Sendable {
    case inbox, sent, drafts, spam, trash, archive

    /// Reihenfolge in der Sidebar.
    public var sortOrder: Int {
        switch self {
        case .inbox: return 0
        case .sent: return 1
        case .drafts: return 2
        case .archive: return 3
        case .spam: return 4
        case .trash: return 5
        }
    }

    public var germanName: String {
        switch self {
        case .inbox: return "Posteingang"
        case .sent: return "Gesendet"
        case .drafts: return "Entwürfe"
        case .spam: return "Spam"
        case .trash: return "Papierkorb"
        case .archive: return "Archiv"
        }
    }
}

/// Ein Ordner auf dem IMAP-Server.
public struct MailFolder: Hashable, Identifiable, Sendable {
    public var path: String
    public var delimiter: String?
    public var attributes: [String]
    public var role: FolderRole?

    public var id: String { path }

    public init(path: String, delimiter: String?, attributes: [String], role: FolderRole? = nil) {
        self.path = path
        self.delimiter = delimiter
        self.attributes = attributes
        self.role = role
    }

    public var isSelectable: Bool {
        !attributes.contains { $0.caseInsensitiveCompare("\\Noselect") == .orderedSame || $0.caseInsensitiveCompare("\\NonExistent") == .orderedSame }
    }

    /// Der Pfad in Klartext (IMAP überträgt Ordnernamen in „modified UTF-7“).
    public var decodedPath: String { ModifiedUTF7.decode(path) }

    /// Anzeigename: Rolle auf Deutsch, sonst letzter Pfadteil.
    public var displayName: String {
        if let role { return role.germanName }
        let decoded = decodedPath
        if let delimiter, !delimiter.isEmpty, let last = decoded.components(separatedBy: delimiter).last, !last.isEmpty {
            return last
        }
        return decoded
    }
}

public struct MailAddress: Hashable, Codable, Sendable {
    public var name: String?
    public var address: String

    public init(name: String?, address: String) {
        self.name = name?.isEmpty == true ? nil : name
        self.address = address
    }

    public var displayName: String { name ?? address }

    public var localPart: String {
        address.split(separator: "@", maxSplits: 1).first.map(String.init)?.lowercased() ?? address.lowercased()
    }

    public var domain: String {
        guard let at = address.lastIndex(of: "@") else { return "" }
        return address[address.index(after: at)...].lowercased()
    }

    /// Formatiert für Header wie „From:“ (RFC 5322, Namen ggf. als Encoded Word).
    public var headerValue: String {
        guard let name else { return "<\(address)>" }
        return "\(EncodedWords.encodePhrase(name)) <\(address)>"
    }
}

/// Kurzinfo einer Nachricht für die Liste.
public struct MessageSummary: Hashable, Identifiable, Sendable {
    public var accountId: UUID
    public var folderPath: String
    public var uid: UInt32
    public var from: MailAddress?
    public var to: [MailAddress]
    public var cc: [MailAddress]
    public var replyTo: [MailAddress]
    public var subject: String
    public var date: Date
    public var messageId: String?
    public var flags: Set<String>
    public var size: Int
    public var category: MailCategory?
    /// Für die Sortierung relevante Header (klein geschrieben).
    public var listHeaders: [String: String]
    public var inReplyTo: String?
    public var references: String?

    public var id: String { "\(accountId.uuidString)|\(folderPath)|\(uid)" }
    public var isSeen: Bool { flags.contains("\\Seen") }
    public var isFlagged: Bool { flags.contains("\\Flagged") }

    public init(accountId: UUID, folderPath: String, uid: UInt32, from: MailAddress?, to: [MailAddress] = [], cc: [MailAddress] = [],
                replyTo: [MailAddress] = [], subject: String, date: Date, messageId: String? = nil, flags: Set<String> = [],
                size: Int = 0, category: MailCategory? = nil, listHeaders: [String: String] = [:],
                inReplyTo: String? = nil, references: String? = nil) {
        self.accountId = accountId
        self.folderPath = folderPath
        self.uid = uid
        self.from = from
        self.to = to
        self.cc = cc
        self.replyTo = replyTo
        self.subject = subject
        self.date = date
        self.messageId = messageId
        self.flags = flags
        self.size = size
        self.category = category
        self.listHeaders = listHeaders
        self.inReplyTo = inReplyTo
        self.references = references
    }

    /// Einträge für „Abbestellen“.
    public var unsubscribe: UnsubscribeMethod? {
        UnsubscribeMethod.parse(listUnsubscribe: listHeaders["list-unsubscribe"], listUnsubscribePost: listHeaders["list-unsubscribe-post"])
    }
}
