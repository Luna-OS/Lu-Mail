import Foundation

public enum IMAPError: Error, LocalizedError, Equatable {
    case commandFailed(command: String, response: String)
    case authenticationFailed(String)
    case unexpectedResponse(String)
    case disconnected(String)

    public var errorDescription: String? {
        switch self {
        case .commandFailed(let command, let response): return "IMAP-Befehl \(command) fehlgeschlagen: \(response)"
        case .authenticationFailed(let response): return "Anmeldung fehlgeschlagen: \(response)"
        case .unexpectedResponse(let response): return "Unerwartete Server-Antwort: \(response)"
        case .disconnected(let reason): return "Der Server hat die Verbindung beendet: \(reason)"
        }
    }
}

/// Ergebnis von SELECT.
public struct IMAPMailboxStatus: Equatable, Sendable {
    public var exists: UInt32 = 0
    public var uidValidity: UInt32?
    public var uidNext: UInt32?
}

/// Ein Eintrag aus einer FETCH-Antwort.
public struct IMAPFetchItem: Sendable {
    public var sequence: UInt32
    public var uid: UInt32?
    public var flags: Set<String> = []
    public var internalDate: Date?
    public var size: Int?
    public var envelope: IMAPEnvelope?
    /// `BODY[...]`-Abschnitte, Schlüssel z. B. „BODY[]“ oder „BODY[HEADER.FIELDS (LIST-ID)]“.
    public var bodySections: [String: Data] = [:]

    /// Der komplette Nachrichtentext (`BODY[]`).
    public var fullBody: Data? {
        bodySections.first { $0.key.uppercased() == "BODY[]" }?.value
    }

    /// Der Header-Abschnitt (`BODY[HEADER…]`).
    public var headerSection: Data? {
        bodySections.first { $0.key.uppercased().hasPrefix("BODY[HEADER") }?.value
    }
}

public struct IMAPEnvelope: Sendable, Equatable {
    public var date: Date?
    public var subject: String
    public var from: [MailAddress]
    public var replyTo: [MailAddress]
    public var to: [MailAddress]
    public var cc: [MailAddress]
    public var inReplyTo: String?
    public var messageId: String?

    init(values: [IMAPValue]) {
        func addresses(_ value: IMAPValue?) -> [MailAddress] {
            guard let list = value?.list else { return [] }
            return list.compactMap { entry -> MailAddress? in
                guard let parts = entry.list, parts.count >= 4,
                      let mailbox = parts[2].text, let host = parts[3].text else { return nil }
                let name = parts[0].data.map { EncodedWords.decode(Charsets.decode($0, charset: "utf-8")) }
                return MailAddress(name: name, address: "\(mailbox)@\(host)")
            }
        }
        let value = { (index: Int) -> IMAPValue? in index < values.count ? values[index] : nil }
        date = value(0)?.text.flatMap(RFC5322Date.parse)
        subject = value(1)?.data.map { EncodedWords.decode(Charsets.decode($0, charset: "utf-8")) } ?? ""
        from = addresses(value(2))
        replyTo = addresses(value(4))
        to = addresses(value(5))
        cc = addresses(value(6))
        inReplyTo = value(8)?.text
        messageId = value(9)?.text
    }
}

public struct IMAPListEntry: Equatable, Sendable {
    public var attributes: [String]
    public var delimiter: String?
    public var path: String
}

/// Ein minimaler, aber robuster IMAP4rev1-Client (RFC 3501) mit den Erweiterungen, die Lu-Mail braucht:
/// SASL-IR/XOAUTH2, MOVE, UIDPLUS, SPECIAL-USE und Gmails X-GM-RAW.
/// Nicht threadsicher – `MailAccountSession` serialisiert alle Aufrufe.
public final class IMAPClient {
    private let transport: MailTransport
    private let reader: IMAPResponseReader
    private var tagCounter = 0
    /// Text der letzten erfolgreichen getaggten Antwort (enthält oft `[CAPABILITY …]`).
    private var lastTaggedText = ""
    public private(set) var capabilities: Set<String> = []

    public init(transport: MailTransport) {
        self.transport = transport
        reader = IMAPResponseReader(transport: transport)
    }

    // MARK: Verbindung

    /// Liest die Begrüßung des Servers.
    public func connect() async throws {
        let greeting = try await reader.next()
        if greeting.keyword == "BYE" { throw IMAPError.disconnected(greeting.statusText) }
        parseCapabilities(fromStatus: greeting.text)
        if capabilities.isEmpty { try await refreshCapabilities() }
    }

    public func refreshCapabilities() async throws {
        let responses = try await execute("CAPABILITY")
        for response in responses where response.keyword == "CAPABILITY" {
            capabilities = Set(response.text.split(separator: " ").dropFirst().map { $0.uppercased() })
        }
    }

    /// STARTTLS (RFC 3501, 6.2.1): danach ist die Verbindung verschlüsselt, Fähigkeiten werden neu gelesen.
    public func startTLS() async throws {
        guard capabilities.isEmpty || capabilities.contains("STARTTLS") else { throw TransportError.startTLSUnavailable }
        _ = try await execute("STARTTLS")
        guard !reader.hasBufferedData else {
            throw TransportError.connectionFailed("Unerwartete Daten vor dem TLS-Handshake")
        }
        try await transport.startTLS()
        capabilities = []
        try await refreshCapabilities()
    }

    public func login(username: String, password: String) async throws {
        let responses: [IMAPResponse]
        do {
            responses = try await execute("LOGIN", arguments: [.astring(username), .astring(password)])
        } catch IMAPError.commandFailed(_, let response) {
            throw IMAPError.authenticationFailed(response)
        }
        try await afterAuthentication(responses)
    }

    /// `AUTHENTICATE XOAUTH2` mit Initial Response (Gmail, Mail.ru).
    public func authenticateXOAUTH2(username: String, accessToken: String) async throws {
        let initial = XOAuth2.initialResponse(user: username, accessToken: accessToken)
        let responses: [IMAPResponse]
        do {
            responses = try await execute("AUTHENTICATE XOAUTH2 \(initial)", onContinuation: { _ in Data() })
        } catch IMAPError.commandFailed(_, let response) {
            throw IMAPError.authenticationFailed(response)
        }
        try await afterAuthentication(responses)
    }

    private func afterAuthentication(_ responses: [IMAPResponse]) async throws {
        // Viele Server schicken die Fähigkeiten nach der Anmeldung neu mit.
        capabilities = []
        parseCapabilities(fromStatus: lastTaggedText)
        for response in responses where response.keyword == "CAPABILITY" {
            capabilities = Set(response.text.split(separator: " ").dropFirst().map { $0.uppercased() })
        }
        if capabilities.isEmpty { try await refreshCapabilities() }
    }

    public func logout() async {
        _ = try? await execute("LOGOUT")
        transport.close()
    }

    public func noop() async throws {
        _ = try await execute("NOOP")
    }

    // MARK: Ordner

    public func list() async throws -> [IMAPListEntry] {
        let responses = try await execute("LIST \"\" \"*\"")
        return responses.compactMap { response -> IMAPListEntry? in
            guard response.keyword == "LIST", let values = try? response.values(), values.count >= 3 else { return nil }
            let attributes = values[0].list?.compactMap(\.text) ?? []
            let delimiter = values[1].text
            guard let path = values[2].text else { return nil }
            return IMAPListEntry(attributes: attributes, delimiter: delimiter, path: path)
        }
    }

    @discardableResult
    public func select(_ path: String, readOnly: Bool = false) async throws -> IMAPMailboxStatus {
        let responses = try await execute(readOnly ? "EXAMINE" : "SELECT", arguments: [.astring(path)])
        var status = IMAPMailboxStatus()
        for response in responses {
            if response.keyword == "EXISTS", let number = response.leadingNumber { status.exists = number }
            let text = response.text
            if let value = Self.bracketValue("UIDVALIDITY", in: text) { status.uidValidity = value }
            if let value = Self.bracketValue("UIDNEXT", in: text) { status.uidNext = value }
        }
        return status
    }

    // MARK: Suchen & Laden

    public func uidSearch(_ criteria: String) async throws -> [UInt32] {
        Self.searchResults(try await execute("UID SEARCH \(criteria)"))
    }

    /// Volltextsuche: Gmail über `X-GM-RAW`, sonst Absender/Betreff/Text (Nicht-ASCII als UTF-8-Literal).
    public func searchText(_ text: String, gmail: Bool) async throws -> [UInt32] {
        if gmail {
            return Self.searchResults(try await execute("UID SEARCH X-GM-RAW", arguments: [.astring(text)]))
        }
        let charset = Self.needsLiteral(text) ? "CHARSET UTF-8 " : ""
        return Self.searchResults(try await execute(
            "UID SEARCH \(charset)OR OR FROM", arguments: [.astring(text), .raw("SUBJECT"), .astring(text), .raw("BODY"), .astring(text)]))
    }

    static func searchResults(_ responses: [IMAPResponse]) -> [UInt32] {
        responses
            .filter { $0.keyword == "SEARCH" }
            .flatMap { $0.text.split(separator: " ").dropFirst().compactMap { UInt32($0) } }
    }

    /// `STATUS <Ordner> (UNSEEN MESSAGES)`.
    public func status(_ path: String) async throws -> (unseen: Int, messages: Int) {
        let responses = try await execute("STATUS", arguments: [.astring(path), .raw("(UNSEEN MESSAGES)")])
        var unseen = 0, messages = 0
        for response in responses where response.keyword == "STATUS" {
            guard let values = try? response.values(), let list = values.last?.list else { continue }
            var index = 0
            while index + 1 < list.count {
                switch list[index].text?.uppercased() {
                case "UNSEEN": unseen = Int(list[index + 1].number ?? 0)
                case "MESSAGES": messages = Int(list[index + 1].number ?? 0)
                default: break
                }
                index += 2
            }
        }
        return (unseen, messages)
    }

    // MARK: IDLE (RFC 2177)

    public var supportsIdle: Bool { capabilities.contains("IDLE") }

    /// Wartet per IDLE auf Änderungen im ausgewählten Ordner. `onChange` wird bei EXISTS/EXPUNGE/FETCH aufgerufen.
    /// Endet nach `timeout` Sekunden oder bei Abbruch des Tasks (dann wird `DONE` gesendet).
    public func idle(timeout: TimeInterval, onChange: @escaping @Sendable () -> Void) async throws {
        tagCounter += 1
        let tag = String(format: "L%04d", tagCounter)
        try await transport.send(Data("\(tag) IDLE\r\n".utf8))
        let first = try await reader.next()
        guard first.kind == .continuation else {
            throw IMAPError.commandFailed(command: "IDLE", response: first.text)
        }
        let transport = transport
        let doneSender = Task {
            try? await Task.sleep(nanoseconds: UInt64(max(1, timeout) * 1_000_000_000))
            try? await transport.send(Data("DONE\r\n".utf8))
        }
        defer { doneSender.cancel() }
        try await withTaskCancellationHandler {
            while true {
                let response = try await reader.next()
                switch response.kind {
                case .tagged(let responseTag) where responseTag == tag:
                    if response.keyword == "OK" { return }
                    throw IMAPError.commandFailed(command: "IDLE", response: response.statusText)
                case .untagged:
                    if ["EXISTS", "EXPUNGE", "FETCH", "RECENT"].contains(response.keyword) { onChange() }
                    if response.keyword == "BYE" { throw IMAPError.disconnected(response.statusText) }
                default:
                    continue
                }
            }
        } onCancel: {
            Task { try? await transport.send(Data("DONE\r\n".utf8)) }
        }
    }

    /// `FETCH` über Sequenznummern (z. B. „101:200“).
    public func fetch(sequenceSet: String, items: String) async throws -> [IMAPFetchItem] {
        try await fetch(command: "FETCH \(sequenceSet) (\(items))")
    }

    public func uidFetch(uidSet: String, items: String) async throws -> [IMAPFetchItem] {
        try await fetch(command: "UID FETCH \(uidSet) (\(items))")
    }

    private func fetch(command: String) async throws -> [IMAPFetchItem] {
        let responses = try await execute(command)
        return responses.compactMap { response -> IMAPFetchItem? in
            guard response.keyword == "FETCH", let sequence = response.leadingNumber,
                  let values = try? response.values(), let list = values.first?.list else { return nil }
            return Self.parseFetch(sequence: sequence, list: list)
        }
    }

    static func parseFetch(sequence: UInt32, list: [IMAPValue]) -> IMAPFetchItem {
        var item = IMAPFetchItem(sequence: sequence)
        var index = 0
        while index + 1 < list.count {
            let key = list[index].text?.uppercased() ?? ""
            let value = list[index + 1]
            switch key {
            case "UID": item.uid = value.number
            case "FLAGS": item.flags = Set(value.list?.compactMap(\.text) ?? [])
            case "INTERNALDATE": item.internalDate = value.text.flatMap(RFC5322Date.parseInternalDate)
            case "RFC822.SIZE": item.size = value.number.map(Int.init)
            case "ENVELOPE": item.envelope = value.list.map(IMAPEnvelope.init(values:))
            default:
                if key.hasPrefix("BODY[") || key.hasPrefix("BINARY[") || key == "RFC822" {
                    let normalized = key.replacingOccurrences(of: ".PEEK", with: "")
                    if let data = value.data { item.bodySections[normalized] = data } else { item.bodySections[normalized] = Data() }
                }
            }
            index += 2
        }
        return item
    }

    // MARK: Ändern

    public func uidStore(uids: [UInt32], flags: [String], add: Bool) async throws {
        guard !uids.isEmpty else { return }
        let set = uids.map(String.init).joined(separator: ",")
        _ = try await execute("UID STORE \(set) \(add ? "+" : "-")FLAGS.SILENT (\(flags.joined(separator: " ")))")
    }

    public func uidMove(uids: [UInt32], to destination: String) async throws {
        guard !uids.isEmpty else { return }
        let set = uids.map(String.init).joined(separator: ",")
        if capabilities.contains("MOVE") {
            _ = try await execute("UID MOVE \(set)", arguments: [.astring(destination)])
        } else {
            _ = try await execute("UID COPY \(set)", arguments: [.astring(destination)])
            try await uidStore(uids: uids, flags: ["\\Deleted"], add: true)
            try await expunge(uids: uids)
        }
    }

    /// Löscht endgültig (mit UIDPLUS nur die angegebenen Nachrichten).
    public func expunge(uids: [UInt32]) async throws {
        if capabilities.contains("UIDPLUS"), !uids.isEmpty {
            _ = try await execute("UID EXPUNGE \(uids.map(String.init).joined(separator: ","))")
        } else {
            _ = try await execute("EXPUNGE")
        }
    }

    /// Legt eine Nachricht in einem Ordner ab (z. B. Kopie in „Gesendet“).
    public func append(_ message: Data, to path: String, flags: [String]) async throws {
        _ = try await execute("APPEND", arguments: [.astring(path), .raw("(\(flags.joined(separator: " ")))"), .literal(message)])
    }

    // MARK: Befehle ausführen

    enum Argument {
        case astring(String)
        case literal(Data)
        case raw(String)
    }

    @discardableResult
    func execute(_ command: String, arguments: [Argument] = [],
                 onContinuation: ((IMAPResponse) -> Data?)? = nil) async throws -> [IMAPResponse] {
        tagCounter += 1
        let tag = String(format: "L%04d", tagCounter)

        // Befehl in Stücke teilen: nach jedem Literal muss auf „+“ gewartet werden (außer mit LITERAL+).
        var chunks: [Data] = []
        var current = Data("\(tag) \(command)".utf8)
        let literalPlus = capabilities.contains("LITERAL+")
        for argument in arguments {
            current.append(0x20)
            switch argument {
            case .raw(let text):
                current.append(Data(text.utf8))
            case .astring(let text):
                if Self.needsLiteral(text) {
                    let bytes = Data(text.utf8)
                    current.append(Data("{\(bytes.count)\(literalPlus ? "+" : "")}\r\n".utf8))
                    if literalPlus { current.append(bytes) } else {
                        chunks.append(current)
                        current = bytes
                    }
                } else {
                    current.append(Data(Self.quote(text).utf8))
                }
            case .literal(let bytes):
                current.append(Data("{\(bytes.count)\(literalPlus ? "+" : "")}\r\n".utf8))
                if literalPlus { current.append(bytes) } else {
                    chunks.append(current)
                    current = bytes
                }
            }
        }
        current.append(contentsOf: [0x0D, 0x0A])
        chunks.append(current)

        var untagged: [IMAPResponse] = []
        try await transport.send(chunks[0])
        var nextChunk = 1
        let commandName = command.split(separator: " ").prefix(2).joined(separator: " ")

        while true {
            let response = try await reader.next()
            switch response.kind {
            case .continuation:
                if nextChunk < chunks.count {
                    try await transport.send(chunks[nextChunk])
                    nextChunk += 1
                } else if let reply = onContinuation?(response) {
                    var line = reply
                    line.append(contentsOf: [0x0D, 0x0A])
                    try await transport.send(line)
                } else {
                    try await transport.send(Data([0x0D, 0x0A]))
                }
            case .untagged:
                if response.keyword == "BYE", commandName != "LOGOUT" {
                    throw IMAPError.disconnected(response.statusText)
                }
                untagged.append(response)
            case .tagged(let responseTag):
                guard responseTag == tag else { continue }
                if response.keyword == "OK" {
                    lastTaggedText = response.text
                    return untagged
                }
                throw IMAPError.commandFailed(command: commandName, response: response.statusText)
            }
        }
    }

    static func needsLiteral(_ text: String) -> Bool {
        text.utf8.contains { $0 >= 0x80 || $0 == 0x0D || $0 == 0x0A || $0 == 0 }
    }

    static func quote(_ text: String) -> String {
        "\"" + text.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
    }

    private func parseCapabilities(fromStatus text: String) {
        guard let start = text.range(of: "[CAPABILITY "), let end = text[start.upperBound...].firstIndex(of: "]") else { return }
        capabilities = Set(text[start.upperBound..<end].split(separator: " ").map { $0.uppercased() })
    }

    static func bracketValue(_ key: String, in text: String) -> UInt32? {
        guard let range = text.range(of: "[\(key) ") else { return nil }
        let digits = text[range.upperBound...].prefix { $0.isNumber }
        return UInt32(digits)
    }
}
