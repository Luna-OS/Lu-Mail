import Foundation

/// Eine verschlüsselte Byte-Verbindung zu einem Mail-Server. Unter macOS mit Network.framework umgesetzt,
/// in den Tests mit einem Skript-Server.
public protocol MailTransport: AnyObject {
    func send(_ data: Data) async throws
    /// Liefert die nächsten empfangenen Bytes (mindestens eines). Wirft `TransportError.closed` am Ende.
    func receive() async throws -> Data
    func close()
}

public enum TransportError: Error, LocalizedError, Equatable {
    case closed
    case timeout
    case connectionFailed(String)

    public var errorDescription: String? {
        switch self {
        case .closed: return "Die Verbindung zum Server wurde geschlossen."
        case .timeout: return "Der Server antwortet nicht."
        case .connectionFailed(let reason): return "Verbindung fehlgeschlagen: \(reason)"
        }
    }
}

/// Puffert eingehende Bytes und liefert Zeilen bzw. feste Byte-Mengen.
final class LineReader {
    private let transport: MailTransport
    private var buffer = Data()

    init(transport: MailTransport) {
        self.transport = transport
    }

    /// Liest bis einschließlich CRLF (oder LF) und gibt die Zeile ohne Zeilenende zurück.
    func readLine() async throws -> Data {
        while true {
            if let newline = buffer.firstIndex(of: 0x0A) {
                var line = buffer[buffer.startIndex..<newline]
                if line.last == 0x0D { line = line.dropLast() }
                buffer = Data(buffer[(newline + 1)...])
                return Data(line)
            }
            buffer.append(try await transport.receive())
        }
    }

    func readBytes(_ count: Int) async throws -> Data {
        while buffer.count < count {
            buffer.append(try await transport.receive())
        }
        let chunk = Data(buffer.prefix(count))
        buffer = Data(buffer.dropFirst(count))
        return chunk
    }
}
