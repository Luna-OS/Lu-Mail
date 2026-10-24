import Foundation

/// RFC 2047 „Encoded Words“: `=?utf-8?B?…?=` und `=?koi8-r?Q?…?=` in Betreffzeilen und Namen.
public enum EncodedWords {
    public static func decode(_ text: String) -> String {
        guard text.contains("=?") else { return text }
        var result = ""
        var rest = Substring(text)
        var lastWasEncoded = false

        while let start = rest.range(of: "=?") {
            let before = rest[rest.startIndex..<start.lowerBound]
            guard let word = parseWord(rest[start.lowerBound...]) else {
                result += before + "=?"
                rest = rest[start.upperBound...]
                lastWasEncoded = false
                continue
            }
            // Leerraum zwischen zwei Encoded Words wird verworfen (RFC 2047, Abschnitt 6.2).
            if !(lastWasEncoded && before.allSatisfy(\.isWhitespace)) {
                result += before
            }
            result += word.text
            rest = rest[word.end...]
            lastWasEncoded = true
        }
        result += rest
        return result
    }

    private static func parseWord(_ text: Substring) -> (text: String, end: Substring.Index)? {
        // =?charset?enc?data?=
        let parts = text.dropFirst(2)
        guard let q1 = parts.firstIndex(of: "?") else { return nil }
        let charset = parts[parts.startIndex..<q1]
        let afterCharset = parts[parts.index(after: q1)...]
        guard let q2 = afterCharset.firstIndex(of: "?") else { return nil }
        let encoding = afterCharset[afterCharset.startIndex..<q2].uppercased()
        let afterEncoding = afterCharset[afterCharset.index(after: q2)...]
        guard let endRange = afterEncoding.range(of: "?=") else { return nil }
        let payload = afterEncoding[afterEncoding.startIndex..<endRange.lowerBound]
        guard !payload.contains(" ") else { return nil }
        // Sprachangabe nach RFC 2231 („utf-8*de“) abschneiden.
        let cleanCharset = charset.split(separator: "*").first.map(String.init) ?? String(charset)

        let bytes: Data?
        switch encoding {
        case "B":
            var base64 = String(payload)
            while base64.count % 4 != 0 { base64 += "=" }
            bytes = Data(base64Encoded: base64)
        case "Q":
            bytes = QuotedPrintable.decode(Data(payload.replacingOccurrences(of: "_", with: " ").utf8))
        default:
            bytes = nil
        }
        guard let bytes else { return nil }
        return (Charsets.decode(bytes, charset: cleanCharset), endRange.upperBound)
    }

    /// Kodiert einen Text für Header (Betreff), falls er Nicht-ASCII-Zeichen enthält.
    public static func encode(_ text: String) -> String {
        guard text.unicodeScalars.contains(where: { $0.value > 0x7E || $0.value < 0x20 }) else { return text }
        // In Stücken, damit kein Encoded Word länger als 75 Zeichen wird – ohne UTF-8-Zeichen zu zerteilen.
        var words: [String] = []
        var chunk = ""
        for character in text {
            if (chunk + String(character)).utf8.count > 45 {
                words.append(chunk)
                chunk = ""
            }
            chunk.append(character)
        }
        if !chunk.isEmpty { words.append(chunk) }
        return words.map { "=?UTF-8?B?\(Data($0.utf8).base64EncodedString())?=" }.joined(separator: "\r\n ")
    }

    /// Wie `encode`, setzt reine ASCII-Namen mit Sonderzeichen aber in Anführungszeichen.
    public static func encodePhrase(_ text: String) -> String {
        let encoded = encode(text)
        if encoded != text { return encoded }
        let special = CharacterSet(charactersIn: "()<>[]:;@\\,.\"")
        if text.unicodeScalars.contains(where: special.contains) {
            return "\"" + text.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
        }
        return text
    }
}

public enum QuotedPrintable {
    public static func decode(_ data: Data) -> Data {
        var output = Data()
        output.reserveCapacity(data.count)
        let bytes = Array(data)
        var index = 0
        while index < bytes.count {
            let byte = bytes[index]
            if byte == UInt8(ascii: "=") {
                // Weicher Zeilenumbruch
                if index + 1 < bytes.count, bytes[index + 1] == 0x0A { index += 2; continue }
                if index + 2 < bytes.count, bytes[index + 1] == 0x0D, bytes[index + 2] == 0x0A { index += 3; continue }
                if index + 2 < bytes.count, let high = hexValue(bytes[index + 1]), let low = hexValue(bytes[index + 2]) {
                    output.append(high << 4 | low)
                    index += 3
                    continue
                }
            }
            output.append(byte)
            index += 1
        }
        return output
    }

    private static func hexValue(_ byte: UInt8) -> UInt8? {
        switch byte {
        case 0x30...0x39: return byte - 0x30
        case 0x41...0x46: return byte - 0x41 + 10
        case 0x61...0x66: return byte - 0x61 + 10
        default: return nil
        }
    }
}

/// Datumsformate aus E-Mail-Headern und IMAP.
public enum RFC5322Date {
    private static let formats = [
        "EEE, d MMM yyyy HH:mm:ss Z",
        "d MMM yyyy HH:mm:ss Z",
        "EEE, d MMM yyyy HH:mm Z",
        "EEE, d MMM yyyy HH:mm:ss zzz",
        "d MMM yyyy HH:mm:ss zzz",
        "EEE, d MMM yy HH:mm:ss Z",
    ]

    private static func formatter(_ format: String) -> DateFormatter {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        formatter.dateFormat = format
        return formatter
    }

    public static func parse(_ text: String) -> Date? {
        // Kommentare wie „(CEST)“ entfernen, Leerraum normalisieren.
        var cleaned = text
        if let paren = cleaned.firstIndex(of: "(") { cleaned = String(cleaned[..<paren]) }
        cleaned = cleaned.split(whereSeparator: \.isWhitespace).joined(separator: " ")
        for format in formats {
            if let date = formatter(format).date(from: cleaned) { return date }
        }
        return nil
    }

    /// IMAP INTERNALDATE: „17-Jul-1996 02:44:25 -0700“.
    public static func parseInternalDate(_ text: String) -> Date? {
        formatter("d-MMM-yyyy HH:mm:ss Z").date(from: text.trimmingCharacters(in: .whitespaces))
    }

    public static func format(_ date: Date) -> String {
        let formatter = formatter("EEE, d MMM yyyy HH:mm:ss Z")
        formatter.timeZone = .current
        return formatter.string(from: date)
    }
}
