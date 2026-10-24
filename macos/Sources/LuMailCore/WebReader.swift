import Foundation

/// Ergebnisse des Web-Lesers `shared/webmail/lu-webmail.js` (docs/FUNKTIONEN.md §21).
public enum WebReader {
    /// Zustand einer Antwort des Lesers.
    public enum State: String, Decodable, Sendable {
        case ok, loading, login, missing, unsupported, error
    }

    /// Eine Zeile der Nachrichtenliste im Webmail.
    public struct Item: Decodable, Hashable, Identifiable, Sendable {
        public var id: String
        public var from: String
        public var fromEmail: String
        public var subject: String
        public var snippet: String
        public var date: String
        public var dateTitle: String
        public var unread: Bool
        public var attachment: Bool

        public init(id: String, from: String = "", fromEmail: String = "", subject: String = "", snippet: String = "",
                    date: String = "", dateTitle: String = "", unread: Bool = false, attachment: Bool = false) {
            self.id = id
            self.from = from
            self.fromEmail = fromEmail
            self.subject = subject
            self.snippet = snippet
            self.date = date
            self.dateTitle = dateTitle
            self.unread = unread
            self.attachment = attachment
        }

        public init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            id = try c.decode(String.self, forKey: .id)
            from = try c.decodeIfPresent(String.self, forKey: .from) ?? ""
            fromEmail = try c.decodeIfPresent(String.self, forKey: .fromEmail) ?? ""
            subject = try c.decodeIfPresent(String.self, forKey: .subject) ?? ""
            snippet = try c.decodeIfPresent(String.self, forKey: .snippet) ?? ""
            date = try c.decodeIfPresent(String.self, forKey: .date) ?? ""
            dateTitle = try c.decodeIfPresent(String.self, forKey: .dateTitle) ?? ""
            unread = try c.decodeIfPresent(Bool.self, forKey: .unread) ?? false
            attachment = try c.decodeIfPresent(Bool.self, forKey: .attachment) ?? false
        }

        private enum CodingKeys: String, CodingKey {
            case id, from, fromEmail, subject, snippet, date, dateTitle, unread, attachment
        }

        /// Anzeigename des Absenders.
        public var sender: String { from.isEmpty ? (fromEmail.isEmpty ? "(unbekannt)" : fromEmail) : from }
        public var displaySubject: String { subject.isEmpty ? "(kein Betreff)" : subject }
    }

    /// Ein Teil (eine Nachricht) einer geöffneten Unterhaltung.
    public struct Part: Decodable, Hashable, Sendable {
        public var from: String
        public var fromEmail: String
        public var to: String
        public var date: String
        public var html: String

        public init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            from = try c.decodeIfPresent(String.self, forKey: .from) ?? ""
            fromEmail = try c.decodeIfPresent(String.self, forKey: .fromEmail) ?? ""
            to = try c.decodeIfPresent(String.self, forKey: .to) ?? ""
            date = try c.decodeIfPresent(String.self, forKey: .date) ?? ""
            html = try c.decodeIfPresent(String.self, forKey: .html) ?? ""
        }

        private enum CodingKeys: String, CodingKey { case from, fromEmail, to, date, html }

        public var sender: String { from.isEmpty ? fromEmail : from }
    }

    public struct ListResult: Decodable, Sendable {
        public var state: State
        public var items: [Item]

        public init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            state = (try? c.decode(State.self, forKey: .state)) ?? .error
            items = try c.decodeIfPresent([Item].self, forKey: .items) ?? []
        }

        private enum CodingKeys: String, CodingKey { case state, items }
    }

    public struct MessageResult: Decodable, Sendable {
        public var state: State
        public var subject: String
        public var parts: [Part]

        public init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            state = (try? c.decode(State.self, forKey: .state)) ?? .error
            subject = try c.decodeIfPresent(String.self, forKey: .subject) ?? ""
            parts = try c.decodeIfPresent([Part].self, forKey: .parts) ?? []
        }

        private enum CodingKeys: String, CodingKey { case state, subject, parts }
    }

    public struct ActionResult: Decodable, Sendable {
        public var state: State
    }

    public static func decode<T: Decodable>(_ type: T.Type, from json: String?) -> T? {
        guard let json, let data = json.data(using: .utf8) else { return nil }
        return try? JSONDecoder().decode(type, from: data)
    }

    /// JavaScript-Aufruf des Lesers; das Skript selbst wird jeweils vorangestellt (es lädt sich nur einmal).
    public static func script(_ call: String) -> String {
        SharedResources.webmailJS + "\n;" + call
    }

    public static func listScript(reader: String) -> String {
        script("LuWebmail.list(\(jsString(reader)))")
    }

    public static func openScript(reader: String, id: String) -> String {
        script("LuWebmail.open(\(jsString(reader)), \(jsString(id)))")
    }

    public static func messageScript(reader: String) -> String {
        script("LuWebmail.message(\(jsString(reader)))")
    }

    /// Sicheres JavaScript-String-Literal (JSON ist gültiges JavaScript).
    public static func jsString(_ value: String) -> String {
        guard let data = try? JSONEncoder().encode([value]), let array = String(data: data, encoding: .utf8) else { return "\"\"" }
        return String(array.dropFirst().dropLast())
    }

    /// Kurzer Datumstext: der Text aus dem Webmail, sonst der lange Titel.
    public static func shortDate(_ item: Item) -> String {
        item.date.isEmpty ? item.dateTitle : item.date
    }
}
