import Foundation

/// Womit sich eine Sitzung anmeldet (frisch aus dem Schlüsselbund bzw. nach Token-Erneuerung).
public enum MailCredential: Sendable {
    case password(user: String, password: String)
    case oauth(user: String, accessToken: String)
}

/// Baut verschlüsselte Verbindungen auf (macOS: Network.framework).
public protocol MailTransportFactory: Sendable {
    func connect(to endpoint: ServerEndpoint, allowInsecureLoopbackCertificate: Bool) async throws -> MailTransport
}

/// Alles, was ein Konto mit dem Server macht. Ein Actor pro Konto – IMAP-Befehle laufen nacheinander.
public actor MailAccountSession {
    public let account: Account
    public let provider: Provider
    private let transports: MailTransportFactory
    private let credentials: @Sendable () async throws -> MailCredential
    private let classifier: CategoryClassifier
    private let roleResolver: FolderRoleResolver

    private var client: IMAPClient?
    private var selectedPath: String?
    private var cachedFolders: [MailFolder]?

    /// Wie viele Nachrichten aus dem Posteingang bzw. anderen Ordnern geladen werden.
    public var inboxLimit = 200
    public var folderLimit = 100

    static let listHeaderFields = "LIST-UNSUBSCRIBE LIST-UNSUBSCRIBE-POST LIST-ID LIST-POST PRECEDENCE REFERENCES"
    static let summaryItems = "UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE BODY.PEEK[HEADER.FIELDS (\(listHeaderFields))]"

    public init(account: Account, provider: Provider, transports: MailTransportFactory,
                classifier: CategoryClassifier = .shared, roleResolver: FolderRoleResolver = FolderRoleResolver(),
                credentials: @escaping @Sendable () async throws -> MailCredential) {
        self.account = account
        self.provider = provider
        self.transports = transports
        self.credentials = credentials
        self.classifier = classifier
        self.roleResolver = roleResolver
    }

    // MARK: Verbindung

    /// Testet Zugangsdaten, ohne eine Sitzung anzulegen (für die Browser-Anmeldeseite).
    public static func verify(provider: Provider, endpoint: ServerEndpoint? = nil, credential: MailCredential,
                              transports: MailTransportFactory) async throws {
        let endpoint = endpoint ?? provider.imap
        let transport = try await transports.connect(to: endpoint, allowInsecureLoopbackCertificate: provider.allowInsecureLoopbackCertificate)
        let client = IMAPClient(transport: transport)
        try await client.connect()
        if endpoint.usesSTARTTLS { try await client.startTLS() }
        try await authenticate(client, credential)
        await client.logout()
    }

    private static func authenticate(_ client: IMAPClient, _ credential: MailCredential) async throws {
        switch credential {
        case .password(let user, let password): try await client.login(username: user, password: password)
        case .oauth(let user, let token): try await client.authenticateXOAUTH2(username: user, accessToken: token)
        }
    }

    private func connectedClient() async throws -> IMAPClient {
        if let client { return client }
        let endpoint = account.imapEndpoint(provider)
        let transport = try await transports.connect(to: endpoint, allowInsecureLoopbackCertificate: provider.allowInsecureLoopbackCertificate)
        let newClient = IMAPClient(transport: transport)
        try await newClient.connect()
        if endpoint.usesSTARTTLS { try await newClient.startTLS() }
        try await Self.authenticate(newClient, try await credentials())
        client = newClient
        selectedPath = nil
        return newClient
    }

    /// Führt eine Operation aus; bei Verbindungsabbruch einmal neu verbinden und wiederholen.
    private func withClient<T>(_ operation: (IMAPClient) async throws -> T) async throws -> T {
        do {
            return try await operation(try await connectedClient())
        } catch let error as IMAPError {
            if case .authenticationFailed = error { throw error }
            if case .commandFailed = error { throw error }
            dropConnection()
            return try await operation(try await connectedClient())
        } catch is TransportError {
            dropConnection()
            return try await operation(try await connectedClient())
        }
    }

    private func dropConnection() {
        client = nil
        selectedPath = nil
    }

    public func disconnect() async {
        if let client { await client.logout() }
        dropConnection()
    }

    private func select(_ client: IMAPClient, _ path: String, force: Bool = false) async throws -> IMAPMailboxStatus? {
        if selectedPath == path && !force { return nil }
        let status = try await client.select(path)
        selectedPath = path
        return status
    }

    // MARK: Ordner

    public func folders(refresh: Bool = false) async throws -> [MailFolder] {
        if let cachedFolders, !refresh { return cachedFolders }
        let entries = try await withClient { try await $0.list() }
        let folders = roleResolver.assignRoles(entries.map {
            MailFolder(path: $0.path, delimiter: $0.delimiter, attributes: $0.attributes)
        })
        cachedFolders = folders
        return folders
    }

    public func folder(for role: FolderRole) async throws -> MailFolder? {
        try await folders().first { $0.role == role }
    }

    // MARK: Nachrichten laden

    /// Lädt die neuesten Nachrichten des Posteingangs und sortiert sie in Kategorien.
    public func loadInbox(rules: RuleSet) async throws -> [MessageSummary] {
        let inboxPath = try await folder(for: .inbox)?.path ?? "INBOX"
        let limit = inboxLimit
        let usesGmail = provider.usesGmailCategories
        let classifier = classifier
        return try await withClient { client in
            guard let status = try await self.select(client, inboxPath, force: true), status.exists > 0 else { return [] }
            let first = status.exists > UInt32(limit) ? status.exists - UInt32(limit) + 1 : 1
            let items = try await client.fetch(sequenceSet: "\(first):\(status.exists)", items: Self.summaryItems)

            // Gmail kennt die Kategorien selbst.
            var serverCategories: [UInt32: MailCategory] = [:]
            let uids = items.compactMap(\.uid)
            if usesGmail, let minUID = uids.min(), let maxUID = uids.max() {
                for info in classifier.categories {
                    guard let category = info.category, category != .primary else { continue }
                    let query = IMAPClient.quote(info.gmailQuery)
                    for uid in try await client.uidSearch("UID \(minUID):\(maxUID) X-GM-RAW \(query)") where serverCategories[uid] == nil {
                        serverCategories[uid] = category
                    }
                }
            }
            return items.compactMap { item in
                self.summary(from: item, folder: inboxPath) { summary in
                    let serverCategory: MailCategory? = usesGmail ? (serverCategories[summary.uid] ?? .primary) : nil
                    return classifier.classify(from: summary.from, to: summary.to + summary.cc, subject: summary.subject,
                                               headers: summary.listHeaders, serverCategory: serverCategory, rules: rules)
                }
            }
        }
    }

    /// Lädt die neuesten Nachrichten eines beliebigen Ordners (ohne Kategorien).
    public func loadFolder(_ path: String) async throws -> [MessageSummary] {
        let limit = folderLimit
        return try await withClient { client in
            guard let status = try await self.select(client, path, force: true), status.exists > 0 else { return [] }
            let first = status.exists > UInt32(limit) ? status.exists - UInt32(limit) + 1 : 1
            let items = try await client.fetch(sequenceSet: "\(first):\(status.exists)", items: Self.summaryItems)
            return items.compactMap { self.summary(from: $0, folder: path, categorize: nil) }
        }
    }

    private nonisolated func summary(from item: IMAPFetchItem, folder: String,
                                     categorize: ((MessageSummary) -> MailCategory)?) -> MessageSummary? {
        guard let uid = item.uid else { return nil }
        let envelope = item.envelope
        var listHeaders: [String: String] = [:]
        if let header = item.headerSection {
            listHeaders = MailHeaders(header).dictionary
        }
        var summary = MessageSummary(
            accountId: account.id, folderPath: folder, uid: uid,
            from: envelope?.from.first, to: envelope?.to ?? [], cc: envelope?.cc ?? [], replyTo: envelope?.replyTo ?? [],
            subject: envelope?.subject ?? "", date: item.internalDate ?? envelope?.date ?? Date(timeIntervalSince1970: 0),
            messageId: envelope?.messageId, flags: item.flags, size: item.size ?? 0, listHeaders: listHeaders,
            inReplyTo: envelope?.inReplyTo, references: listHeaders["references"])
        summary.category = categorize?(summary)
        return summary
    }

    public func fetchMessage(folder: String, uid: UInt32) async throws -> ParsedMessage {
        MIMEParser.parse(try await fetchRawMessage(folder: folder, uid: uid))
    }

    /// Die komplette Nachricht als RFC-822-Daten (für den Offline-Cache).
    public func fetchRawMessage(folder: String, uid: UInt32) async throws -> Data {
        try await withClient { client in
            _ = try await self.select(client, folder)
            let items = try await client.uidFetch(uidSet: String(uid), items: "UID BODY.PEEK[]")
            guard let body = items.first(where: { $0.uid == uid })?.fullBody ?? items.first?.fullBody else {
                throw IMAPError.unexpectedResponse("Nachricht \(uid) nicht gefunden")
            }
            return body
        }
    }

    // MARK: Zähler & Suche

    /// Ungelesene Nachrichten je Ordner (ohne Posteingang, Gesendet, Entwürfe, Papierkorb, Archiv).
    public func unreadCounts() async throws -> [String: Int] {
        let folders = try await folders().filter { folder in
            guard folder.isSelectable else { return false }
            guard let role = folder.role else { return true }
            return role == .spam
        }
        return try await withClient { client in
            var result: [String: Int] = [:]
            for folder in folders {
                if let status = try? await client.status(folder.path) { result[folder.path] = status.unseen }
            }
            return result
        }
    }

    /// Serversuche (docs/FUNKTIONEN.md §4): höchstens `limit` neueste Treffer.
    public func search(_ text: String, in path: String, limit: Int = 50) async throws -> [MessageSummary] {
        let query = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !query.isEmpty else { return [] }
        let gmail = provider.usesGmailCategories
        return try await withClient { client in
            _ = try await self.select(client, path)
            let uids = try await client.searchText(query, gmail: gmail).sorted(by: >).prefix(limit)
            guard !uids.isEmpty else { return [] }
            let items = try await client.uidFetch(uidSet: uids.map(String.init).joined(separator: ","), items: Self.summaryItems)
            return items.compactMap { self.summary(from: $0, folder: path, categorize: nil) }
        }
    }

    // MARK: Push (IDLE)

    /// Hält eine eigene IDLE-Verbindung für den Posteingang offen und ruft `onChange` bei neuen Nachrichten auf.
    /// Läuft, bis der Task abgebrochen wird; liefert `false`, wenn der Server kein IDLE kann.
    public nonisolated func watchInbox(onChange: @escaping @Sendable () -> Void) async -> Bool {
        var backoff: UInt64 = 30
        while !Task.isCancelled {
            do {
                let endpoint = account.imapEndpoint(provider)
                let transport = try await transports.connect(to: endpoint, allowInsecureLoopbackCertificate: provider.allowInsecureLoopbackCertificate)
                let client = IMAPClient(transport: transport)
                defer { transport.close() }
                try await client.connect()
                if endpoint.usesSTARTTLS { try await client.startTLS() }
                try await Self.authenticate(client, try await credentials())
                guard client.supportsIdle else { return false }
                try await client.select("INBOX")
                backoff = 30
                while !Task.isCancelled {
                    try await client.idle(timeout: 25 * 60, onChange: onChange)
                }
            } catch is CancellationError {
                break
            } catch {
                if Task.isCancelled { break }
                try? await Task.sleep(nanoseconds: backoff * 1_000_000_000)
                backoff = min(backoff * 2, 600)
            }
        }
        return true
    }

    // MARK: Ändern

    public func setFlag(_ flag: String, add: Bool, uids: [UInt32], folder: String) async throws {
        try await withClient { client in
            _ = try await self.select(client, folder)
            try await client.uidStore(uids: uids, flags: [flag], add: add)
        }
    }

    /// Verschiebt in den Ordner mit der angegebenen Rolle (Archiv, Papierkorb, Spam, Posteingang).
    public func move(uids: [UInt32], from folder: String, to role: FolderRole) async throws {
        guard let destination = try await self.folder(for: role) else {
            throw IMAPError.unexpectedResponse("Kein Ordner „\(role.germanName)“ gefunden")
        }
        try await withClient { client in
            _ = try await self.select(client, folder)
            try await client.uidMove(uids: uids, to: destination.path)
        }
    }

    public func deletePermanently(uids: [UInt32], folder: String) async throws {
        try await withClient { client in
            _ = try await self.select(client, folder)
            try await client.uidStore(uids: uids, flags: ["\\Deleted"], add: true)
            try await client.expunge(uids: uids)
        }
    }

    // MARK: Senden

    public func send(_ message: OutgoingMessage) async throws {
        try await send(raw: MessageBuilder.build(message), envelope: message)
    }

    /// Sendet bereits erzeugte Nachrichtendaten (z. B. aus dem Postausgang „Später senden“).
    /// `envelope` liefert nur Absender und Empfänger für SMTP.
    public func send(raw data: Data, envelope message: OutgoingMessage) async throws {
        let credential = try await credentials()
        let auth: SMTPAuth
        switch credential {
        case .password(let user, let password): auth = .password(user: user, password: password)
        case .oauth(let user, let token): auth = .xoauth2(user: user, accessToken: token)
        }
        let smtpEndpoint = account.smtpEndpoint(provider)
        let transport = try await transports.connect(to: smtpEndpoint, allowInsecureLoopbackCertificate: provider.allowInsecureLoopbackCertificate)
        try await SMTPClient(transport: transport).send(message, data: data, auth: auth, startTLS: smtpEndpoint.usesSTARTTLS)

        if provider.saveSentViaImap == true, let sent = try await folder(for: .sent) {
            try await withClient { try await $0.append(data, to: sent.path, flags: ["\\Seen"]) }
        }
    }
}
