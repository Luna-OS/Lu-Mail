import Foundation

private func readJSON<T: Decodable>(_ type: T.Type, from url: URL) -> T? {
    guard let data = try? Data(contentsOf: url) else { return nil }
    let decoder = JSONDecoder()
    decoder.dateDecodingStrategy = .iso8601
    return try? decoder.decode(type, from: data)
}

private func writeJSON<T: Encodable>(_ value: T, to url: URL) throws {
    try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    encoder.dateEncodingStrategy = .iso8601
    try encoder.encode(value).write(to: url, options: .atomic)
}

// MARK: - Kontakte (docs/FUNKTIONEN.md §5)

public struct Contact: Codable, Hashable, Sendable {
    public var name: String?
    public var count: Int
    public var lastUsed: Date
}

public struct ContactBook: Sendable {
    public private(set) var contacts: [String: Contact]

    public init(contacts: [String: Contact] = [:]) {
        self.contacts = contacts
    }

    /// Merkt sich Adressen (z. B. aus Absendern, Empfängern, gesendeten Mails).
    public mutating func record(_ addresses: [MailAddress], at date: Date = Date()) {
        for address in addresses where address.address.contains("@") {
            let key = address.address.lowercased()
            var contact = contacts[key] ?? Contact(name: nil, count: 0, lastUsed: date)
            contact.count += 1
            if contact.lastUsed < date { contact.lastUsed = date }
            if let name = address.name, !name.isEmpty { contact.name = name }
            contacts[key] = contact
        }
    }

    /// Vorschläge für ein An-/Cc-Feld: Treffer in Name oder Adresse, nach Häufigkeit und letzter Nutzung.
    public func suggestions(for query: String, limit: Int = 8) -> [MailAddress] {
        let needle = query.trimmingCharacters(in: .whitespaces).lowercased()
        guard needle.count >= 1 else { return [] }
        return contacts
            .filter { $0.key.contains(needle) || ($0.value.name?.lowercased().contains(needle) ?? false) }
            .sorted { lhs, rhs in
                lhs.value.count != rhs.value.count ? lhs.value.count > rhs.value.count : lhs.value.lastUsed > rhs.value.lastUsed
            }
            .prefix(limit)
            .map { MailAddress(name: $0.value.name, address: $0.key) }
    }
}

public struct ContactStore: Sendable {
    let url: URL
    public init(paths: AppPaths) { url = paths.baseDirectory.appendingPathComponent("contacts.json") }
    public func load() -> ContactBook { ContactBook(contacts: readJSON([String: Contact].self, from: url) ?? [:]) }
    public func save(_ book: ContactBook) throws { try writeJSON(book.contacts, to: url) }
}

// MARK: - Später erinnern (§13)

public struct SnoozedMessage: Codable, Hashable, Sendable, Identifiable {
    public var accountId: UUID
    public var folder: String
    public var messageId: String?
    public var uid: UInt32
    public var until: Date
    /// Für die Übersicht „Erinnerungen“.
    public var subject: String?
    public var sender: String?

    public init(accountId: UUID, folder: String, messageId: String?, uid: UInt32, until: Date, subject: String? = nil, sender: String? = nil) {
        self.accountId = accountId
        self.folder = folder
        self.messageId = messageId
        self.uid = uid
        self.until = until
        self.subject = subject
        self.sender = sender
    }

    public var id: String { "\(accountId.uuidString)|\(folder)|\(uid)" }

    public func matches(_ message: MessageSummary) -> Bool {
        guard message.accountId == accountId, message.folderPath == folder else { return false }
        if let messageId, let other = message.messageId { return messageId == other }
        return message.uid == uid
    }
}

public enum SnoozePreset: String, CaseIterable, Sendable {
    case thisEvening, tomorrowMorning, nextMonday

    public var title: String {
        switch self {
        case .thisEvening: return "Heute Abend (18:00)"
        case .tomorrowMorning: return "Morgen früh (08:00)"
        case .nextMonday: return "Montag (08:00)"
        }
    }

    /// Zeitpunkt relativ zu `now`; „Heute Abend“ nach 18 Uhr wird zu morgen 18 Uhr.
    public func date(from now: Date = Date(), calendar: Calendar = .current) -> Date {
        func at(_ hour: Int, daysAhead: Int) -> Date {
            let day = calendar.date(byAdding: .day, value: daysAhead, to: calendar.startOfDay(for: now))!
            return calendar.date(bySettingHour: hour, minute: 0, second: 0, of: day)!
        }
        switch self {
        case .thisEvening:
            let today = at(18, daysAhead: 0)
            return today > now ? today : at(18, daysAhead: 1)
        case .tomorrowMorning:
            return at(8, daysAhead: 1)
        case .nextMonday:
            let weekday = calendar.component(.weekday, from: now)  // 1 = Sonntag, 2 = Montag
            let days = (9 - weekday) % 7 == 0 ? 7 : (9 - weekday) % 7
            return at(8, daysAhead: days)
        }
    }
}

public struct SnoozeStore: Sendable {
    let url: URL
    public init(paths: AppPaths) { url = paths.baseDirectory.appendingPathComponent("snoozed.json") }
    public func load() -> [SnoozedMessage] { readJSON([SnoozedMessage].self, from: url) ?? [] }
    public func save(_ items: [SnoozedMessage]) throws { try writeJSON(items, to: url) }
}

// MARK: - Später senden (§13)

public struct ScheduledMessage: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var accountId: UUID
    public var sendAt: Date
    public var to: [String]
    public var cc: [String]
    public var bcc: [String]
    public var subject: String
}

public struct OutboxStore: Sendable {
    let directory: URL

    public init(paths: AppPaths) {
        directory = paths.baseDirectory.appendingPathComponent("outbox", isDirectory: true)
    }

    public func list() -> [ScheduledMessage] {
        guard let files = try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil) else { return [] }
        return files.filter { $0.pathExtension == "json" }
            .compactMap { readJSON(ScheduledMessage.self, from: $0) }
            .sorted { $0.sendAt < $1.sendAt }
    }

    public func add(_ message: OutgoingMessage, accountId: UUID, sendAt: Date) throws -> ScheduledMessage {
        let entry = ScheduledMessage(id: UUID(), accountId: accountId, sendAt: sendAt,
                                     to: message.to.map(\.address), cc: message.cc.map(\.address), bcc: message.bcc.map(\.address),
                                     subject: message.subject)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try MessageBuilder.build(message).write(to: emlURL(entry.id), options: .atomic)
        try writeJSON(entry, to: directory.appendingPathComponent("\(entry.id.uuidString).json"))
        return entry
    }

    public func rawMessage(_ entry: ScheduledMessage) throws -> Data {
        try Data(contentsOf: emlURL(entry.id))
    }

    public func remove(_ entry: ScheduledMessage) {
        try? FileManager.default.removeItem(at: emlURL(entry.id))
        try? FileManager.default.removeItem(at: directory.appendingPathComponent("\(entry.id.uuidString).json"))
    }

    public func due(at date: Date = Date()) -> [ScheduledMessage] {
        list().filter { $0.sendAt <= date }
    }

    func emlURL(_ id: UUID) -> URL { directory.appendingPathComponent("\(id.uuidString).eml") }
}

// MARK: - Offline-Cache (§3)

public struct CachedAccount: Codable, Sendable {
    public struct Folder: Codable, Sendable {
        public var path: String
        public var delimiter: String?
        public var attributes: [String]
    }

    public struct Summary: Codable, Sendable {
        public var folderPath: String
        public var uid: UInt32
        public var from: MailAddress?
        public var to: [MailAddress]
        public var cc: [MailAddress]
        public var replyTo: [MailAddress]
        public var subject: String
        public var date: Date
        public var messageId: String?
        public var flags: [String]
        public var size: Int
        public var category: MailCategory?
        public var listHeaders: [String: String]
        public var inReplyTo: String?
        public var references: String?
    }

    public var folders: [Folder]
    public var messages: [Summary]
    public var savedAt: Date

    public init(folders: [MailFolder], messages: [MessageSummary], savedAt: Date = Date()) {
        self.folders = folders.map { Folder(path: $0.path, delimiter: $0.delimiter, attributes: $0.attributes) }
        self.messages = messages.map {
            Summary(folderPath: $0.folderPath, uid: $0.uid, from: $0.from, to: $0.to, cc: $0.cc, replyTo: $0.replyTo,
                    subject: $0.subject, date: $0.date, messageId: $0.messageId, flags: Array($0.flags).sorted(), size: $0.size,
                    category: $0.category, listHeaders: $0.listHeaders, inReplyTo: $0.inReplyTo, references: $0.references)
        }
        self.savedAt = savedAt
    }

    public func restore(accountId: UUID, roleResolver: FolderRoleResolver = FolderRoleResolver()) -> (folders: [MailFolder], messages: [MessageSummary]) {
        let folders = roleResolver.assignRoles(self.folders.map { MailFolder(path: $0.path, delimiter: $0.delimiter, attributes: $0.attributes) })
        let messages = self.messages.map {
            MessageSummary(accountId: accountId, folderPath: $0.folderPath, uid: $0.uid, from: $0.from, to: $0.to, cc: $0.cc,
                           replyTo: $0.replyTo, subject: $0.subject, date: $0.date, messageId: $0.messageId, flags: Set($0.flags),
                           size: $0.size, category: $0.category, listHeaders: $0.listHeaders, inReplyTo: $0.inReplyTo, references: $0.references)
        }
        return (folders, messages)
    }
}

public struct MessageCache: Sendable {
    public let directory: URL
    public let maximumBytes: Int

    public init(paths: AppPaths, maximumBytes: Int = 200 * 1024 * 1024) {
        directory = paths.baseDirectory.appendingPathComponent("cache", isDirectory: true)
        self.maximumBytes = maximumBytes
    }

    public func saveAccount(_ account: CachedAccount, id: UUID) throws {
        try writeJSON(account, to: directory.appendingPathComponent("\(id.uuidString).json"))
    }

    public func loadAccount(id: UUID) -> CachedAccount? {
        readJSON(CachedAccount.self, from: directory.appendingPathComponent("\(id.uuidString).json"))
    }

    func bodyURL(accountId: UUID, folder: String, uid: UInt32) -> URL {
        let folderHash = SHA256.hash(Data(folder.utf8)).prefix(8).map { String(format: "%02x", $0) }.joined()
        return directory.appendingPathComponent(accountId.uuidString, isDirectory: true)
            .appendingPathComponent(folderHash, isDirectory: true)
            .appendingPathComponent("\(uid).eml")
    }

    public func saveBody(_ data: Data, accountId: UUID, folder: String, uid: UInt32) {
        let url = bodyURL(accountId: accountId, folder: folder, uid: uid)
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: url, options: .atomic)
        trim()
    }

    public func loadBody(accountId: UUID, folder: String, uid: UInt32) -> Data? {
        try? Data(contentsOf: bodyURL(accountId: accountId, folder: folder, uid: uid))
    }

    public func removeAccount(_ id: UUID) {
        try? FileManager.default.removeItem(at: directory.appendingPathComponent("\(id.uuidString).json"))
        try? FileManager.default.removeItem(at: directory.appendingPathComponent(id.uuidString, isDirectory: true))
    }

    public func clear() {
        try? FileManager.default.removeItem(at: directory)
    }

    /// Löscht die ältesten gespeicherten Nachrichten, bis höchstens `maximumBytes` belegt sind.
    public func trim() {
        guard let enumerator = FileManager.default.enumerator(at: directory, includingPropertiesForKeys: [.fileSizeKey, .contentModificationDateKey]) else { return }
        var files: [(url: URL, size: Int, date: Date)] = []
        for case let url as URL in enumerator where url.pathExtension == "eml" {
            let values = try? url.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey])
            files.append((url, values?.fileSize ?? 0, values?.contentModificationDate ?? .distantPast))
        }
        var total = files.reduce(0) { $0 + $1.size }
        for file in files.sorted(by: { $0.date < $1.date }) where total > maximumBytes {
            try? FileManager.default.removeItem(at: file.url)
            total -= file.size
        }
    }
}

// MARK: - Werbung aufräumen (§12)

public enum CleanupPlanner {
    public static let allowedDays = [0, 7, 30, 90]

    /// Welche geladenen Nachrichten aufgeräumt werden (höchstens `limit`).
    public static func messagesToClean(_ messages: [MessageSummary], days: [MailCategory: Int], now: Date = Date(), limit: Int = 100) -> [MessageSummary] {
        Array(messages.filter { message in
            guard let category = message.category, let limitDays = days[category], limitDays > 0, !message.isFlagged else { return false }
            return now.timeIntervalSince(message.date) > Double(limitDays) * 86_400
        }
        .sorted { $0.date < $1.date }
        .prefix(limit))
    }
}

/// Neue, ungelesene Nachrichten für Mitteilungen (§2).
public enum NewMailDetector {
    public static func newMessages(previous: Set<UInt32>?, current: [MessageSummary],
                                   categories: Set<MailCategory> = [.primary, .updates]) -> [MessageSummary] {
        guard let previous else { return [] }  // erstes Laden: keine Mitteilungen
        return current.filter { !previous.contains($0.uid) && !$0.isSeen && categories.contains($0.category ?? .primary) }
    }
}
