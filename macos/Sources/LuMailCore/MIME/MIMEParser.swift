import Foundation

/// Ein E-Mail-Header-Block (Reihenfolge bleibt erhalten, Namen ohne Groß-/Kleinschreibung).
public struct MailHeaders: Sendable {
    public private(set) var fields: [(name: String, value: String)] = []

    public init(_ raw: Data) {
        // Header sind 7-Bit, manchmal aber roh in UTF-8/Latin-1 – das tolerieren wir.
        let text = Charsets.utf8OrLatin1(raw)
        var current: (String, String)?
        for line in text.split(omittingEmptySubsequences: false, whereSeparator: { $0 == "\n" || $0 == "\r\n" }) {
            var line = Substring(line)
            if line.hasSuffix("\r") { line = line.dropLast() }
            if line.isEmpty { continue }
            if line.first == " " || line.first == "\t" {
                // Fortsetzungszeile (Folding)
                if let (name, value) = current { current = (name, value + " " + line.trimmingCharacters(in: .whitespaces)) }
                continue
            }
            if let current { fields.append((name: current.0, value: current.1)) }
            if let colon = line.firstIndex(of: ":") {
                current = (String(line[..<colon]).trimmingCharacters(in: .whitespaces),
                           String(line[line.index(after: colon)...]).trimmingCharacters(in: .whitespaces))
            } else {
                current = nil
            }
        }
        if let current { fields.append((name: current.0, value: current.1)) }
    }

    public subscript(name: String) -> String? {
        fields.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }?.value
    }

    /// Alle Header als Wörterbuch (Namen klein geschrieben, erster Wert gewinnt).
    public var dictionary: [String: String] {
        var result: [String: String] = [:]
        for field in fields where result[field.name.lowercased()] == nil {
            result[field.name.lowercased()] = field.value
        }
        return result
    }
}

/// „text/html; charset="utf-8"; name*=UTF-8''Rechnung%20M%C3%A4rz.pdf“
public struct HeaderValueWithParameters: Sendable {
    public var value: String
    public var parameters: [String: String]

    public init(_ raw: String) {
        var parts: [String] = []
        var current = ""
        var inQuotes = false
        for character in raw {
            if character == "\"" { inQuotes.toggle() }
            if character == ";" && !inQuotes {
                parts.append(current)
                current = ""
            } else {
                current.append(character)
            }
        }
        parts.append(current)
        value = parts.first?.trimmingCharacters(in: .whitespaces).lowercased() ?? ""

        var params: [String: String] = [:]
        var continuations: [String: [(Int, String, Bool)]] = [:]
        for part in parts.dropFirst() {
            guard let equals = part.firstIndex(of: "=") else { continue }
            var key = part[..<equals].trimmingCharacters(in: .whitespaces).lowercased()
            var val = part[part.index(after: equals)...].trimmingCharacters(in: .whitespaces)
            if val.hasPrefix("\""), val.hasSuffix("\""), val.count >= 2 {
                val = String(val.dropFirst().dropLast()).replacingOccurrences(of: "\\\"", with: "\"")
            }
            // RFC 2231: name*=charset''wert bzw. name*0*=…, name*1*=…
            let extended = key.hasSuffix("*")
            if extended { key.removeLast() }
            if let star = key.firstIndex(of: "*"), let number = Int(key[key.index(after: star)...]) {
                continuations[String(key[..<star]), default: []].append((number, val, extended))
            } else if extended {
                params[key] = Self.decodeRFC2231(val, first: true)
            } else {
                params[key] = val
            }
        }
        for (key, pieces) in continuations {
            let sorted = pieces.sorted { $0.0 < $1.0 }
            var charset = "utf-8"
            var bytes = Data()
            for (index, piece) in sorted.enumerated() {
                var value = piece.1
                if piece.2 {
                    if index == 0, let firstQuote = value.firstIndex(of: "'") {
                        charset = String(value[..<firstQuote])
                        let afterLang = value[value.index(after: firstQuote)...]
                        value = afterLang.firstIndex(of: "'").map { String(afterLang[afterLang.index(after: $0)...]) } ?? String(afterLang)
                    }
                    bytes.append(Self.percentDecode(value))
                } else {
                    bytes.append(Data(value.utf8))
                }
            }
            params[key] = Charsets.decode(bytes, charset: charset)
        }
        parameters = params
    }

    static func decodeRFC2231(_ value: String, first: Bool) -> String {
        let pieces = value.split(separator: "'", maxSplits: 2, omittingEmptySubsequences: false)
        guard pieces.count == 3 else { return Charsets.decode(percentDecode(value), charset: "utf-8") }
        return Charsets.decode(percentDecode(String(pieces[2])), charset: String(pieces[0]))
    }

    static func percentDecode(_ value: String) -> Data {
        var data = Data()
        var iterator = Array(value.utf8).makeIterator()
        while let byte = iterator.next() {
            if byte == UInt8(ascii: "%"), let h = iterator.next(), let l = iterator.next(),
               let value = UInt8(String(decoding: [h, l], as: UTF8.self), radix: 16) {
                data.append(value)
            } else {
                data.append(byte)
            }
        }
        return data
    }
}

public struct MailAttachment: Identifiable, Sendable, Hashable {
    public var id: Int
    public var filename: String
    public var mimeType: String
    public var data: Data
    public var contentId: String?
    public var isInline: Bool
}

/// Eine vollständig zerlegte Nachricht.
public struct ParsedMessage: Sendable {
    public var headers: MailHeaders
    public var subject: String
    public var from: [MailAddress]
    public var to: [MailAddress]
    public var cc: [MailAddress]
    public var replyTo: [MailAddress]
    public var date: Date?
    public var messageId: String?
    public var references: String?
    public var htmlBody: String?
    public var textBody: String?
    public var attachments: [MailAttachment]

    /// Sichtbare Anhänge (ohne eingebettete Bilder, die schon im HTML stecken).
    public var visibleAttachments: [MailAttachment] {
        attachments.filter { !($0.isInline && $0.contentId != nil && htmlBody != nil) }
    }

    /// Kurzer Vorschautext für die Liste.
    public var preview: String {
        let source = textBody ?? htmlBody.map(HTMLText.stripTags) ?? ""
        let collapsed = source.split(whereSeparator: \.isWhitespace).joined(separator: " ")
        return String(collapsed.prefix(200))
    }
}

public enum MIMEParser {
    public static func parse(_ data: Data) -> ParsedMessage {
        let (headerData, body) = splitHeaderAndBody(data)
        let headers = MailHeaders(headerData)
        var html: String?
        var text: String?
        var attachments: [MailAttachment] = []
        walk(headers: headers, body: body, html: &html, text: &text, attachments: &attachments, depth: 0)

        // Eingebettete Bilder (cid:) direkt als data:-URL einsetzen – die Ansicht lädt sonst nichts nach.
        if var htmlBody = html {
            for attachment in attachments where attachment.mimeType.hasPrefix("image/") {
                guard let cid = attachment.contentId else { continue }
                let dataURL = "data:\(attachment.mimeType);base64,\(attachment.data.base64EncodedString())"
                htmlBody = htmlBody.replacingOccurrences(of: "cid:\(cid)", with: dataURL)
            }
            html = htmlBody
        }

        return ParsedMessage(
            headers: headers,
            subject: EncodedWords.decode(headers["Subject"] ?? ""),
            from: AddressParser.parseList(headers["From"] ?? ""),
            to: AddressParser.parseList(headers["To"] ?? ""),
            cc: AddressParser.parseList(headers["Cc"] ?? ""),
            replyTo: AddressParser.parseList(headers["Reply-To"] ?? ""),
            date: headers["Date"].flatMap(RFC5322Date.parse),
            messageId: headers["Message-ID"],
            references: headers["References"],
            htmlBody: html,
            textBody: text,
            attachments: attachments
        )
    }

    static func splitHeaderAndBody(_ data: Data) -> (Data, Data) {
        let bytes = [UInt8](data)
        var index = 0
        while index < bytes.count {
            if bytes[index] == 0x0A {
                if index + 1 < bytes.count, bytes[index + 1] == 0x0A {
                    return (Data(bytes[0..<index]), Data(bytes[(index + 2)...]))
                }
                if index + 2 < bytes.count, bytes[index + 1] == 0x0D, bytes[index + 2] == 0x0A {
                    return (Data(bytes[0..<index]), Data(bytes[(index + 3)...]))
                }
            }
            index += 1
        }
        return (data, Data())
    }

    private static func walk(headers: MailHeaders, body: Data, html: inout String?, text: inout String?,
                             attachments: inout [MailAttachment], depth: Int) {
        let contentType = HeaderValueWithParameters(headers["Content-Type"] ?? "text/plain; charset=us-ascii")
        let disposition = headers["Content-Disposition"].map(HeaderValueWithParameters.init)
        let transferEncoding = headers["Content-Transfer-Encoding"]?.trimmingCharacters(in: .whitespaces).lowercased()

        if contentType.value.hasPrefix("multipart/"), let boundary = contentType.parameters["boundary"], depth < 20 {
            // Bei multipart/alternative landen Text- und HTML-Variante getrennt in text bzw. html.
            for part in splitMultipart(body, boundary: boundary) {
                let (partHeaderData, partBody) = splitHeaderAndBody(part)
                walk(headers: MailHeaders(partHeaderData), body: partBody, html: &html, text: &text,
                     attachments: &attachments, depth: depth + 1)
            }
            return
        }

        let decoded = decodeTransfer(body, encoding: transferEncoding)
        let filename = disposition?.parameters["filename"] ?? contentType.parameters["name"]
        let isAttachment = disposition?.value == "attachment" || (filename != nil && !contentType.value.hasPrefix("text/"))

        if contentType.value == "message/rfc822", !isAttachment, depth < 20 {
            // Weitergeleitete Nachricht: als Anhang zeigen.
            attachments.append(MailAttachment(id: attachments.count, filename: filename.map(EncodedWords.decode) ?? "Weitergeleitete Nachricht.eml",
                                              mimeType: contentType.value, data: decoded, contentId: nil, isInline: false))
            return
        }

        if !isAttachment, contentType.value == "text/html" {
            let value = Charsets.decode(decoded, charset: contentType.parameters["charset"])
            html = html.map { $0 + value } ?? value
            return
        }
        if !isAttachment, contentType.value == "text/plain" || contentType.value == "text" {
            let value = Charsets.decode(decoded, charset: contentType.parameters["charset"])
            text = text.map { $0 + "\n" + value } ?? value
            return
        }

        var contentId = headers["Content-ID"]?.trimmingCharacters(in: .whitespaces)
        if contentId?.hasPrefix("<") == true, contentId?.hasSuffix(">") == true {
            contentId = String(contentId!.dropFirst().dropLast())
        }
        attachments.append(MailAttachment(
            id: attachments.count,
            filename: filename.map(EncodedWords.decode) ?? "Anhang \(attachments.count + 1)",
            mimeType: contentType.value,
            data: decoded,
            contentId: contentId,
            isInline: disposition?.value == "inline" || (disposition == nil && contentId != nil)
        ))
    }

    static func decodeTransfer(_ data: Data, encoding: String?) -> Data {
        switch encoding {
        case "base64":
            let cleaned = data.filter { byte in
                (byte >= 0x41 && byte <= 0x5A) || (byte >= 0x61 && byte <= 0x7A) || (byte >= 0x30 && byte <= 0x39)
                    || byte == UInt8(ascii: "+") || byte == UInt8(ascii: "/") || byte == UInt8(ascii: "=")
            }
            var padded = Data(cleaned)
            while padded.count % 4 != 0 { padded.append(UInt8(ascii: "=")) }
            return Data(base64Encoded: padded) ?? data
        case "quoted-printable":
            return QuotedPrintable.decode(data)
        default:
            return data
        }
    }

    static func splitMultipart(_ body: Data, boundary: String) -> [Data] {
        let delimiter = Array("--\(boundary)".utf8)
        let bytes = [UInt8](body)
        var parts: [Data] = []
        var partStart: Int?
        var lineStart = 0

        while lineStart <= bytes.count {
            var lineEnd = lineStart
            while lineEnd < bytes.count, bytes[lineEnd] != 0x0A { lineEnd += 1 }
            let line = bytes[lineStart..<lineEnd]
            let trimmedLine = line.last == 0x0D ? line.dropLast() : line
            if trimmedLine.starts(with: delimiter) {
                let suffix = trimmedLine.dropFirst(delimiter.count)
                if let start = partStart {
                    // Zeilenumbruch vor der Grenze gehört zur Grenze.
                    var end = lineStart
                    if end > start, bytes[end - 1] == 0x0A { end -= 1 }
                    if end > start, bytes[end - 1] == 0x0D { end -= 1 }
                    parts.append(Data(bytes[start..<max(start, end)]))
                }
                if suffix.starts(with: [UInt8(ascii: "-"), UInt8(ascii: "-")]) { return parts }
                partStart = min(lineEnd + 1, bytes.count)
            }
            lineStart = lineEnd + 1
        }
        if let start = partStart, start < bytes.count { parts.append(Data(bytes[start...])) }
        return parts
    }
}

/// Zerlegt Adresslisten wie `"Müller, Max" <max@x.de>, =?utf-8?B?…?= <a@b.c>, c@d.e`.
public enum AddressParser {
    public static func parseList(_ raw: String) -> [MailAddress] {
        var entries: [String] = []
        var current = ""
        var inQuotes = false
        var angleDepth = 0
        for character in raw {
            switch character {
            case "\"": inQuotes.toggle()
            case "<" where !inQuotes: angleDepth += 1
            case ">" where !inQuotes: angleDepth = max(0, angleDepth - 1)
            default: break
            }
            if character == ",", !inQuotes, angleDepth == 0 {
                entries.append(current)
                current = ""
            } else {
                current.append(character)
            }
        }
        entries.append(current)
        return entries.compactMap(parseSingle)
    }

    public static func parseSingle(_ raw: String) -> MailAddress? {
        let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return nil }
        if let open = text.lastIndex(of: "<"), let close = text.lastIndex(of: ">"), open < close {
            let address = text[text.index(after: open)..<close].trimmingCharacters(in: .whitespaces)
            var name = text[..<open].trimmingCharacters(in: .whitespaces)
            if name.hasPrefix("\""), name.hasSuffix("\""), name.count >= 2 {
                name = String(name.dropFirst().dropLast()).replacingOccurrences(of: "\\\"", with: "\"")
            }
            guard address.contains("@") else { return nil }
            return MailAddress(name: name.isEmpty ? nil : EncodedWords.decode(name), address: address)
        }
        // „max@x.de (Max Müller)“ oder nur die Adresse
        var address = text
        var name: String?
        if let paren = text.firstIndex(of: "("), let close = text.lastIndex(of: ")"), paren < close {
            name = String(text[text.index(after: paren)..<close])
            address = text[..<paren].trimmingCharacters(in: .whitespaces)
        }
        guard address.contains("@") else { return nil }
        return MailAddress(name: name.map(EncodedWords.decode), address: address)
    }
}

/// Einfache Text-Hilfen für HTML.
public enum HTMLText {
    public static func stripTags(_ html: String) -> String {
        var result = ""
        var inTag = false
        var skipDepth = 0
        var tagName = ""
        for character in html {
            if character == "<" {
                inTag = true
                tagName = ""
                continue
            }
            if inTag {
                if character == ">" {
                    inTag = false
                    let lower = tagName.lowercased()
                    if lower.hasPrefix("style") || lower.hasPrefix("script") || lower.hasPrefix("head") { skipDepth += 1 }
                    if lower.hasPrefix("/style") || lower.hasPrefix("/script") || lower.hasPrefix("/head") { skipDepth = max(0, skipDepth - 1) }
                    if ["br", "br/", "/p", "/div", "/tr", "/li", "/h1", "/h2", "/h3"].contains(where: { lower.hasPrefix($0) }) {
                        result.append(" ")
                    }
                } else {
                    tagName.append(character)
                }
                continue
            }
            if skipDepth == 0 { result.append(character) }
        }
        return decodeEntities(result)
    }

    public static func decodeEntities(_ text: String) -> String {
        guard text.contains("&") else { return text }
        let named = ["&nbsp;": " ", "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": "\"", "&#39;": "'", "&apos;": "'",
                     "&auml;": "ä", "&ouml;": "ö", "&uuml;": "ü", "&Auml;": "Ä", "&Ouml;": "Ö", "&Uuml;": "Ü", "&szlig;": "ß",
                     "&euro;": "€", "&zwnj;": "", "&shy;": ""]
        var result = text
        for (entity, value) in named { result = result.replacingOccurrences(of: entity, with: value) }
        return result
    }

    public static func escape(_ text: String) -> String {
        text.replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
            .replacingOccurrences(of: "'", with: "&#39;")
    }
}
