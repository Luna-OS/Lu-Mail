import Foundation

public enum SMTPError: Error, LocalizedError, Equatable {
    case unexpectedReply(code: Int, text: String, during: String)

    public var errorDescription: String? {
        switch self {
        case .unexpectedReply(let code, let text, let during):
            return "SMTP-Fehler bei \(during): \(code) \(text)"
        }
    }
}

public enum SMTPAuth {
    case password(user: String, password: String)
    case xoauth2(user: String, accessToken: String)
}

/// Minimaler SMTP-Client (RFC 5321) für den Versand über implizites TLS (Port 465 / Bridge-SSL).
public final class SMTPClient {
    private let transport: MailTransport
    private let reader: LineReader
    public private(set) var extensions: [String] = []

    public init(transport: MailTransport) {
        self.transport = transport
        reader = LineReader(transport: transport)
    }

    public func send(_ message: OutgoingMessage, data: Data, auth: SMTPAuth, heloName: String = "lumail.local") async throws {
        try await expect(220, during: "Begrüßung")
        try await command("EHLO \(heloName)", expect: 250, during: "EHLO", captureExtensions: true)

        switch auth {
        case .password(let user, let password):
            let token = Data("\0\(user)\0\(password)".utf8).base64EncodedString()
            try await command("AUTH PLAIN \(token)", expect: 235, during: "Anmeldung")
        case .xoauth2(let user, let accessToken):
            let token = XOAuth2.initialResponse(user: user, accessToken: accessToken)
            try await send(line: "AUTH XOAUTH2 \(token)")
            let (code, text) = try await readReply()
            if code == 334 {
                // Fehlerdetails kommen Base64-kodiert; mit leerer Zeile abschließen.
                try await send(line: "")
                let (finalCode, finalText) = try await readReply()
                throw SMTPError.unexpectedReply(code: finalCode, text: finalText, during: "Anmeldung")
            }
            guard code == 235 else { throw SMTPError.unexpectedReply(code: code, text: text, during: "Anmeldung") }
        }

        try await command("MAIL FROM:<\(message.from.address)>", expect: 250, during: "Absender")
        for recipient in message.allRecipients {
            let (code, text) = try await commandReply("RCPT TO:<\(recipient.address)>")
            guard code == 250 || code == 251 else {
                throw SMTPError.unexpectedReply(code: code, text: text, during: "Empfänger \(recipient.address)")
            }
        }
        try await command("DATA", expect: 354, during: "DATA")
        try await transport.send(Self.dotStuff(data))
        try await expect(250, during: "Nachricht senden")
        _ = try? await commandReply("QUIT")
        transport.close()
    }

    /// Zeilen, die mit „.“ beginnen, bekommen einen zusätzlichen Punkt; danach „CRLF.CRLF“.
    static func dotStuff(_ data: Data) -> Data {
        var output = Data()
        output.reserveCapacity(data.count + 64)
        var atLineStart = true
        for byte in data {
            if atLineStart && byte == UInt8(ascii: ".") { output.append(UInt8(ascii: ".")) }
            output.append(byte)
            atLineStart = byte == 0x0A
        }
        if !output.ends(with: [0x0D, 0x0A]) { output.append(contentsOf: [0x0D, 0x0A]) }
        output.append(contentsOf: Array(".\r\n".utf8))
        return output
    }

    private func send(line: String) async throws {
        try await transport.send(Data((line + "\r\n").utf8))
    }

    private func command(_ line: String, expect code: Int, during: String, captureExtensions: Bool = false) async throws {
        try await send(line: line)
        try await expect(code, during: during, captureExtensions: captureExtensions)
    }

    private func commandReply(_ line: String) async throws -> (Int, String) {
        try await send(line: line)
        return try await readReply()
    }

    private func expect(_ expected: Int, during: String, captureExtensions: Bool = false) async throws {
        let (code, text) = try await readReply(captureExtensions: captureExtensions)
        guard code == expected else { throw SMTPError.unexpectedReply(code: code, text: text, during: during) }
    }

    /// Liest eine (ggf. mehrzeilige) Antwort: „250-…“ … „250 …“.
    private func readReply(captureExtensions: Bool = false) async throws -> (Int, String) {
        var texts: [String] = []
        while true {
            let line = String(decoding: try await reader.readLine(), as: UTF8.self)
            guard line.count >= 3, let code = Int(line.prefix(3)) else {
                throw SMTPError.unexpectedReply(code: 0, text: line, during: "Antwort lesen")
            }
            let text = line.count > 4 ? String(line.dropFirst(4)) : ""
            texts.append(text)
            let isLast = line.count == 3 || line[line.index(line.startIndex, offsetBy: 3)] == " "
            if isLast {
                if captureExtensions { extensions = Array(texts.dropFirst()) }
                return (code, texts.joined(separator: " "))
            }
        }
    }
}

private extension Data {
    func ends(with suffix: [UInt8]) -> Bool {
        count >= suffix.count && Array(self.suffix(suffix.count)) == suffix
    }
}
