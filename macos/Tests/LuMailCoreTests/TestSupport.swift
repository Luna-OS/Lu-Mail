import Foundation
@testable import LuMailCore

/// Ein simulierter Server: Jede gesendete Zeile geht an `responder`, dessen Antwort wird in die Empfangswarteschlange gelegt.
/// `{tag}` in der Antwort wird durch das Tag des Befehls ersetzt.
final class ScriptedTransport: MailTransport, @unchecked Sendable {
    private var incoming: [Data] = []
    private(set) var sentLines: [String] = []
    private let responder: (String) -> String?
    private(set) var isClosed = false
    private var lastTag = ""

    init(greeting: String, responder: @escaping (String) -> String?) {
        self.responder = responder
        incoming.append(Data(greeting.utf8))
    }

    func send(_ data: Data) async throws {
        let text = String(decoding: data, as: UTF8.self)
        sentLines.append(text)
        // Befehl ohne Tag an den Responder geben.
        var tag = ""
        var command = text.trimmingCharacters(in: .newlines)
        if let space = command.firstIndex(of: " "), command.hasPrefix("L") {
            tag = String(command[..<space])
            command = String(command[command.index(after: space)...])
            lastTag = tag
        } else {
            // Fortsetzung eines Befehls (z. B. Literal-Daten) gehört zum letzten Tag.
            tag = lastTag
        }
        if text.hasSuffix("}\r\n") {
            incoming.append(Data("+ Ready for literal\r\n".utf8))
            return
        }
        if let reply = responder(command) {
            incoming.append(Data(reply.replacingOccurrences(of: "{tag}", with: tag).utf8))
        }
    }

    func receive() async throws -> Data {
        guard !incoming.isEmpty else { throw TransportError.closed }
        return incoming.removeFirst()
    }

    func close() {
        isClosed = true
    }
}

struct ScriptedFactory: MailTransportFactory {
    let make: @Sendable (ServerEndpoint) -> MailTransport

    func connect(to endpoint: ServerEndpoint, allowInsecureLoopbackCertificate: Bool) async throws -> MailTransport {
        make(endpoint)
    }
}

func literal(_ text: String) -> String {
    "{\(Data(text.utf8).count)}\r\n\(text)"
}
