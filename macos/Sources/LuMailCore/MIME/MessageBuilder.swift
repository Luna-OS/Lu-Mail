import Foundation

/// Eine zu sendende Nachricht.
public struct OutgoingMessage: Sendable {
    public var from: MailAddress
    public var to: [MailAddress]
    public var cc: [MailAddress]
    public var bcc: [MailAddress]
    public var subject: String
    public var body: String
    public var inReplyTo: String?
    public var references: String?
    public var attachments: [OutgoingAttachment]

    public init(from: MailAddress, to: [MailAddress], cc: [MailAddress] = [], bcc: [MailAddress] = [], subject: String,
                body: String, inReplyTo: String? = nil, references: String? = nil, attachments: [OutgoingAttachment] = []) {
        self.from = from
        self.to = to
        self.cc = cc
        self.bcc = bcc
        self.subject = subject
        self.body = body
        self.inReplyTo = inReplyTo
        self.references = references
        self.attachments = attachments
    }

    public var allRecipients: [MailAddress] { to + cc + bcc }
    public var attachmentBytes: Int { attachments.reduce(0) { $0 + $1.data.count } }
}

/// Ein Anhang zum Senden.
public struct OutgoingAttachment: Sendable, Hashable, Identifiable {
    public var id = UUID()
    public var filename: String
    public var mimeType: String
    public var data: Data

    public init(filename: String, mimeType: String, data: Data) {
        self.filename = filename
        self.mimeType = mimeType
        self.data = data
    }

    /// Ab dieser Gesamtgröße warnt Lu-Mail (viele Anbieter lehnen > 25 MB ab).
    public static let warningBytes = 20 * 1024 * 1024
}

/// Erzeugt eine RFC-5322-Nachricht (UTF-8, Base64-Text). Bcc wird bewusst nicht in die Header geschrieben.
public enum MessageBuilder {
    public static func build(_ message: OutgoingMessage, date: Date = Date(), messageId: String? = nil) -> Data {
        var lines: [String] = []
        let domain = message.from.domain.isEmpty ? "lumail.local" : message.from.domain
        lines.append("Date: \(RFC5322Date.format(date))")
        lines.append("From: \(message.from.headerValue)")
        if !message.to.isEmpty { lines.append("To: \(message.to.map(\.headerValue).joined(separator: ",\r\n "))") }
        if !message.cc.isEmpty { lines.append("Cc: \(message.cc.map(\.headerValue).joined(separator: ",\r\n "))") }
        lines.append("Subject: \(EncodedWords.encode(message.subject))")
        lines.append("Message-ID: \(messageId ?? "<\(UUID().uuidString.lowercased())@\(domain)>")")
        if let inReplyTo = message.inReplyTo { lines.append("In-Reply-To: \(inReplyTo)") }
        if let references = message.references { lines.append("References: \(references)") }
        lines.append("MIME-Version: 1.0")
        lines.append("User-Agent: Lu-Mail")

        let normalized = message.body.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\n", with: "\r\n")
        let textPart = base64Lines(Data(normalized.utf8))
        guard !message.attachments.isEmpty else {
            lines.append("Content-Type: text/plain; charset=utf-8")
            lines.append("Content-Transfer-Encoding: base64")
            lines.append("")
            return Data((lines.joined(separator: "\r\n") + "\r\n" + textPart + "\r\n").utf8)
        }

        let boundary = "lumail-\(UUID().uuidString.lowercased())"
        lines.append("Content-Type: multipart/mixed; boundary=\"\(boundary)\"")
        lines.append("")
        lines.append("--\(boundary)")
        lines.append("Content-Type: text/plain; charset=utf-8")
        lines.append("Content-Transfer-Encoding: base64")
        lines.append("")
        lines.append(textPart)
        for attachment in message.attachments {
            let name = encodedFilename(attachment.filename)
            lines.append("--\(boundary)")
            lines.append("Content-Type: \(attachment.mimeType); name=\"\(asciiFallback(attachment.filename))\"")
            lines.append("Content-Disposition: attachment; \(name)")
            lines.append("Content-Transfer-Encoding: base64")
            lines.append("")
            lines.append(base64Lines(attachment.data))
        }
        lines.append("--\(boundary)--")
        lines.append("")
        return Data(lines.joined(separator: "\r\n").utf8)
    }

    static func base64Lines(_ data: Data) -> String {
        data.base64EncodedString(options: [.lineLength76Characters, .endLineWithCarriageReturn, .endLineWithLineFeed])
    }

    /// RFC 2231: `filename*=UTF-8''Rechnung%20M%C3%A4rz.pdf` (plus ASCII-Variante für alte Programme).
    static func encodedFilename(_ name: String) -> String {
        let fallback = asciiFallback(name)
        guard name.unicodeScalars.contains(where: { $0.value > 0x7E }) || name.contains("\"") else {
            return "filename=\"\(fallback)\""
        }
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        let encoded = name.addingPercentEncoding(withAllowedCharacters: allowed) ?? fallback
        return "filename=\"\(fallback)\"; filename*=UTF-8''\(encoded)"
    }

    static func asciiFallback(_ name: String) -> String {
        let folded = name.folding(options: .diacriticInsensitive, locale: Locale(identifier: "de"))
        return String(folded.unicodeScalars.map { $0.value >= 0x20 && $0.value <= 0x7E && $0 != "\"" && $0 != "\\" ? Character($0) : "_" })
    }

    /// Hängt die Signatur an („-- “ ist der Standard-Trenner, RFC 3676).
    public static func bodyWithSignature(_ body: String, signature: String?) -> String {
        guard let signature = signature?.trimmingCharacters(in: .whitespacesAndNewlines), !signature.isEmpty else { return body }
        return "\n\n-- \n\(signature)" + body
    }

    /// „Re: “ bzw. „Fwd: “ voranstellen, ohne sie zu verdoppeln.
    public static func prefixedSubject(_ subject: String, prefix: String) -> String {
        let lower = subject.lowercased()
        let known = prefix == "Re:" ? ["re:", "aw:", "ответ:"] : ["fwd:", "fw:", "wg:"]
        if known.contains(where: { lower.hasPrefix($0) }) { return subject }
        return "\(prefix) \(subject)"
    }

    /// Zitiert den Originaltext mit „> “.
    public static func quotedBody(original: ParsedMessage, intro: String) -> String {
        let source = original.textBody ?? original.htmlBody.map(HTMLText.stripTags) ?? ""
        let quoted = source.split(separator: "\n", omittingEmptySubsequences: false)
            .map { "> " + $0.trimmingCharacters(in: CharacterSet(charactersIn: "\r")) }
            .joined(separator: "\n")
        return "\n\n\(intro)\n\(quoted)"
    }
}

/// Bereitet HTML für die Anzeige vor: blockiert externe Inhalte (Tracking-Pixel) per Content-Security-Policy.
public enum MailHTML {
    public static let blockingCSP = "default-src 'none'; img-src data: cid:; style-src 'unsafe-inline'; font-src data:"
    public static let permissiveCSP = "default-src 'none'; img-src * data: cid:; style-src * 'unsafe-inline'; font-src * data:"

    public static func prepare(html: String?, text: String?, allowRemoteContent: Bool, darkMode: Bool) -> String {
        let csp = allowRemoteContent ? permissiveCSP : blockingCSP
        let content: String
        if let html {
            content = html
        } else {
            content = "<pre class=\"lu-plain\">\(linkify(HTMLText.escape(text ?? "")))</pre>"
        }
        let colorScheme = darkMode ? "color-scheme: light dark;" : "color-scheme: light;"
        return """
        <!doctype html><html><head><meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="\(csp)">
        <meta name="referrer" content="no-referrer">
        <style>
        :root { \(colorScheme) }
        body { margin: 0; padding: 16px 20px; font: 14px/1.5 -apple-system, "Segoe UI Variable", "Segoe UI", sans-serif; word-wrap: break-word; }
        pre.lu-plain { white-space: pre-wrap; font: inherit; margin: 0; }
        img { max-width: 100%; height: auto; }
        blockquote { border-left: 3px solid #8B7CFF; margin: 0 0 0 4px; padding-left: 12px; color: #6F6C99; }
        </style></head><body>\(content)</body></html>
        """
    }

    /// Macht http(s)-Adressen in reinem Text klickbar (Text ist bereits escaped).
    static func linkify(_ escaped: String) -> String {
        var result = ""
        var rest = Substring(escaped)
        while let range = rest.range(of: "https://") ?? rest.range(of: "http://") {
            // Den früheren der beiden Treffer nehmen.
            var start = range
            if let other = rest.range(of: "http://"), other.lowerBound < start.lowerBound { start = other }
            if let other = rest.range(of: "https://"), other.lowerBound < start.lowerBound { start = other }
            result += rest[rest.startIndex..<start.lowerBound]
            let urlPart = rest[start.lowerBound...].prefix { !$0.isWhitespace && $0 != "&" && $0 != "<" }
            result += "<a href=\"\(urlPart)\">\(urlPart)</a>"
            rest = rest[urlPart.endIndex...]
        }
        return result + rest
    }
}
