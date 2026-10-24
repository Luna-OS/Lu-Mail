import Foundation
import LuMailCore
import Network
import Security

/// TLS-Verbindung über Network.framework.
final class NetworkTransport: MailTransport, @unchecked Sendable {
    private let connection: NWConnection
    private let queue: DispatchQueue
    /// Ohne Daten nach dieser Zeit gilt der Server als hängend.
    private let receiveTimeout: TimeInterval = 90

    private init(connection: NWConnection, queue: DispatchQueue) {
        self.connection = connection
        self.queue = queue
    }

    static func isLoopback(_ host: String) -> Bool {
        ["127.0.0.1", "::1", "localhost"].contains(host.lowercased())
    }

    static func connect(host: String, port: Int, allowInsecureLoopbackCertificate: Bool) async throws -> NetworkTransport {
        let queue = DispatchQueue(label: "de.lunaos.lumail.transport.\(host)")
        let tls = NWProtocolTLS.Options()
        if allowInsecureLoopbackCertificate && isLoopback(host) {
            // Proton Mail Bridge nutzt ein selbstsigniertes Zertifikat – akzeptiert nur für 127.0.0.1.
            sec_protocol_options_set_verify_block(tls.securityProtocolOptions, { _, _, complete in
                complete(true)
            }, queue)
        }
        let tcp = NWProtocolTCP.Options()
        tcp.connectionTimeout = 20
        tcp.enableKeepalive = true
        let parameters = NWParameters(tls: tls, tcp: tcp)
        guard let nwPort = NWEndpoint.Port(rawValue: UInt16(clamping: port)) else {
            throw TransportError.connectionFailed("Ungültiger Port \(port)")
        }
        let connection = NWConnection(host: NWEndpoint.Host(host), port: nwPort, using: parameters)
        let transport = NetworkTransport(connection: connection, queue: queue)
        try await transport.waitUntilReady(host: host, port: port)
        return transport
    }

    private func waitUntilReady(host: String, port: Int) async throws {
        let once = ResumeOnce()
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            connection.stateUpdateHandler = { [connection] state in
                switch state {
                case .ready:
                    once.run { continuation.resume() }
                case .failed(let error):
                    once.run { continuation.resume(throwing: TransportError.connectionFailed("\(host):\(port) – \(error.localizedDescription)")) }
                case .waiting(let error):
                    // z. B. „Connection refused“, wenn die Proton Bridge nicht läuft.
                    connection.cancel()
                    once.run { continuation.resume(throwing: TransportError.connectionFailed("\(host):\(port) nicht erreichbar – \(error.localizedDescription)")) }
                case .cancelled:
                    once.run { continuation.resume(throwing: TransportError.closed) }
                default:
                    break
                }
            }
            connection.start(queue: queue)
        }
    }

    func send(_ data: Data) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            connection.send(content: data, completion: .contentProcessed { error in
                if let error {
                    continuation.resume(throwing: TransportError.connectionFailed(error.localizedDescription))
                } else {
                    continuation.resume()
                }
            })
        }
    }

    func receive() async throws -> Data {
        let once = ResumeOnce()
        return try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Data, Error>) in
            let timeout = DispatchWorkItem { [connection] in
                once.run { continuation.resume(throwing: TransportError.timeout) }
                connection.cancel()
            }
            queue.asyncAfter(deadline: .now() + receiveTimeout, execute: timeout)
            connection.receive(minimumIncompleteLength: 1, maximumLength: 256 * 1024) { data, _, isComplete, error in
                timeout.cancel()
                if let data, !data.isEmpty {
                    once.run { continuation.resume(returning: data) }
                } else if let error {
                    once.run { continuation.resume(throwing: TransportError.connectionFailed(error.localizedDescription)) }
                } else if isComplete {
                    once.run { continuation.resume(throwing: TransportError.closed) }
                } else {
                    once.run { continuation.resume(throwing: TransportError.closed) }
                }
            }
        }
    }

    func close() {
        connection.cancel()
    }
}

struct NetworkTransportFactory: MailTransportFactory {
    func connect(to endpoint: ServerEndpoint, allowInsecureLoopbackCertificate: Bool) async throws -> MailTransport {
        try await NetworkTransport.connect(host: endpoint.host, port: endpoint.port,
                                           allowInsecureLoopbackCertificate: allowInsecureLoopbackCertificate)
    }
}

/// Stellt sicher, dass eine Continuation genau einmal fortgesetzt wird.
final class ResumeOnce: @unchecked Sendable {
    private let lock = NSLock()
    private var done = false

    func run(_ action: () -> Void) {
        lock.lock()
        let shouldRun = !done
        done = true
        lock.unlock()
        if shouldRun { action() }
    }
}
