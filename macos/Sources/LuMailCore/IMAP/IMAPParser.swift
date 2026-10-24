import Foundation

/// Ein Wert in einer IMAP-Antwort.
public indirect enum IMAPValue: Equatable, Sendable {
    case atom(String)
    case string(Data)
    case list([IMAPValue])
    case nil_

    /// Atom oder String als Text (Strings als UTF-8, sonst Latin-1).
    public var text: String? {
        switch self {
        case .atom(let value): return value
        case .string(let data): return String(data: data, encoding: .utf8) ?? String(decoding: data, as: UTF8.self)
        default: return nil
        }
    }

    public var data: Data? {
        switch self {
        case .string(let data): return data
        case .atom(let value): return Data(value.utf8)
        default: return nil
        }
    }

    public var list: [IMAPValue]? {
        if case .list(let values) = self { return values }
        return nil
    }

    public var number: UInt32? {
        if case .atom(let value) = self { return UInt32(value) }
        return nil
    }
}

public enum IMAPParseError: Error, Equatable {
    case unexpectedEnd
    case unexpectedCharacter(UInt8, at: Int)
}

/// Zerlegt eine vollständige IMAP-Antwort (inklusive eingebetteter Literale) in Werte.
public struct IMAPTokenizer {
    private let bytes: [UInt8]
    private var position = 0

    public init(_ data: Data) {
        bytes = Array(data)
    }

    public var isAtEnd: Bool { position >= bytes.count }

    /// Der Rest ab der aktuellen Position als Text (für Antworttexte wie „OK Logged in“).
    public var remainderText: String {
        String(decoding: bytes[min(position, bytes.count)...], as: UTF8.self).trimmingCharacters(in: .whitespaces)
    }

    public mutating func skipSpaces() {
        while position < bytes.count, bytes[position] == 0x20 { position += 1 }
    }

    public mutating func parseAll() throws -> [IMAPValue] {
        var values: [IMAPValue] = []
        skipSpaces()
        while !isAtEnd {
            values.append(try parseValue())
            skipSpaces()
        }
        return values
    }

    public mutating func parseValue() throws -> IMAPValue {
        skipSpaces()
        guard position < bytes.count else { throw IMAPParseError.unexpectedEnd }
        switch bytes[position] {
        case UInt8(ascii: "("):
            position += 1
            var items: [IMAPValue] = []
            while true {
                skipSpaces()
                guard position < bytes.count else { throw IMAPParseError.unexpectedEnd }
                if bytes[position] == UInt8(ascii: ")") {
                    position += 1
                    return .list(items)
                }
                items.append(try parseValue())
            }
        case UInt8(ascii: "\""):
            return .string(try parseQuoted())
        case UInt8(ascii: "{"):
            return .string(try parseLiteral())
        case UInt8(ascii: ")"):
            throw IMAPParseError.unexpectedCharacter(bytes[position], at: position)
        default:
            let atom = parseAtom()
            if atom.caseInsensitiveCompare("NIL") == .orderedSame { return .nil_ }
            return .atom(atom)
        }
    }

    private mutating func parseQuoted() throws -> Data {
        position += 1
        var result = Data()
        while position < bytes.count {
            let byte = bytes[position]
            if byte == UInt8(ascii: "\\"), position + 1 < bytes.count {
                result.append(bytes[position + 1])
                position += 2
                continue
            }
            if byte == UInt8(ascii: "\"") {
                position += 1
                return result
            }
            result.append(byte)
            position += 1
        }
        throw IMAPParseError.unexpectedEnd
    }

    /// `{123}\r\n` gefolgt von 123 Bytes.
    private mutating func parseLiteral() throws -> Data {
        position += 1
        var digits = ""
        while position < bytes.count, bytes[position] != UInt8(ascii: "}") {
            let byte = bytes[position]
            if byte >= 0x30 && byte <= 0x39 { digits.append(Character(UnicodeScalar(byte))) }
            position += 1
        }
        guard position < bytes.count, let count = Int(digits) else { throw IMAPParseError.unexpectedEnd }
        position += 1  // }
        if position < bytes.count, bytes[position] == 0x0D { position += 1 }
        if position < bytes.count, bytes[position] == 0x0A { position += 1 }
        guard position + count <= bytes.count else { throw IMAPParseError.unexpectedEnd }
        let data = Data(bytes[position..<(position + count)])
        position += count
        return data
    }

    /// Atome dürfen eckige Klammern mit Leerzeichen enthalten, z. B. `BODY[HEADER.FIELDS (LIST-ID)]`.
    private mutating func parseAtom() -> String {
        let start = position
        var bracketDepth = 0
        while position < bytes.count {
            let byte = bytes[position]
            if byte == UInt8(ascii: "[") { bracketDepth += 1 }
            if byte == UInt8(ascii: "]") { bracketDepth = max(0, bracketDepth - 1) }
            if bracketDepth == 0 && (byte == 0x20 || byte == UInt8(ascii: "(") || byte == UInt8(ascii: ")")) {
                // „(“ direkt nach „BODY[...]<0>“ wäre ungewöhnlich; „(“ beendet ein Atom außerhalb von Klammern.
                break
            }
            if byte == 0x0D || byte == 0x0A { break }
            position += 1
        }
        if position == start {
            // Sicherheitsnetz gegen Endlosschleifen bei unerwarteten Zeichen.
            position += 1
            return String(UnicodeScalar(bytes[start]))
        }
        return String(decoding: bytes[start..<position], as: UTF8.self)
    }
}

/// Eine vollständige Server-Antwort.
public struct IMAPResponse: Sendable {
    public enum Kind: Equatable, Sendable {
        case untagged
        case continuation
        case tagged(String)
    }

    public var kind: Kind
    /// Die rohe Antwort ohne Tag-Präfix (z. B. `12 FETCH (…)` oder `OK Logged in`).
    public var raw: Data

    public init(kind: Kind, raw: Data) {
        self.kind = kind
        self.raw = raw
    }

    public static func parse(_ data: Data) -> IMAPResponse {
        guard let space = data.firstIndex(of: 0x20) else {
            if data.first == UInt8(ascii: "+") { return IMAPResponse(kind: .continuation, raw: Data()) }
            return IMAPResponse(kind: .untagged, raw: data)
        }
        let prefix = String(decoding: data[data.startIndex..<space], as: UTF8.self)
        let rest = Data(data[(space + 1)...])
        switch prefix {
        case "*": return IMAPResponse(kind: .untagged, raw: rest)
        case "+": return IMAPResponse(kind: .continuation, raw: rest)
        default: return IMAPResponse(kind: .tagged(prefix), raw: rest)
        }
    }

    /// Erstes Wort, groß geschrieben (OK, NO, BAD, BYE, CAPABILITY, LIST, SEARCH …) bzw. bei
    /// `* 12 FETCH` das zweite Wort.
    public var keyword: String {
        let words = text.split(separator: " ", maxSplits: 2)
        guard let first = words.first else { return "" }
        if UInt32(first) != nil, words.count > 1 { return words[1].uppercased() }
        return first.uppercased()
    }

    /// Zahl vor dem Schlüsselwort (`* 23 EXISTS` → 23).
    public var leadingNumber: UInt32? {
        text.split(separator: " ", maxSplits: 1).first.flatMap { UInt32($0) }
    }

    public var text: String { String(decoding: raw, as: UTF8.self) }

    /// Text nach dem Statuswort, z. B. „[AUTHENTICATIONFAILED] Invalid credentials“.
    public var statusText: String {
        let parts = text.split(separator: " ", maxSplits: 1)
        return parts.count > 1 ? String(parts[1]) : ""
    }

    /// Alle Werte nach dem Schlüsselwort (für FETCH: nach Nummer und „FETCH“).
    public func values() throws -> [IMAPValue] {
        var tokenizer = IMAPTokenizer(raw)
        var all = try tokenizer.parseAll()
        if let first = all.first, first.number != nil, all.count > 1 { all.removeFirst() }
        if !all.isEmpty { all.removeFirst() }
        return all
    }
}

/// Liest vollständige Antworten (Zeilen plus Literale) vom Server.
final class IMAPResponseReader {
    private let reader: LineReader

    init(transport: MailTransport) {
        reader = LineReader(transport: transport)
    }

    func next() async throws -> IMAPResponse {
        var data = Data()
        while true {
            let line = try await reader.readLine()
            data.append(line)
            guard let count = Self.trailingLiteralLength(line) else { break }
            data.append(contentsOf: [0x0D, 0x0A])
            data.append(try await reader.readBytes(count))
        }
        return IMAPResponse.parse(data)
    }

    /// Erkennt `{123}` bzw. `{123+}` am Zeilenende.
    static func trailingLiteralLength(_ line: Data) -> Int? {
        guard line.last == UInt8(ascii: "}"), let open = line.lastIndex(of: UInt8(ascii: "{")) else { return nil }
        var digits = String(decoding: line[(open + 1)..<(line.endIndex - 1)], as: UTF8.self)
        if digits.hasSuffix("+") { digits.removeLast() }
        return Int(digits)
    }
}
