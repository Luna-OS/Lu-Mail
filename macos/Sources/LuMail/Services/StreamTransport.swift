import Foundation
import LuMailCore

/// Verbindung über Foundation-Streams – kann nachträglich auf TLS umschalten (STARTTLS, z. B. iCloud-SMTP auf Port 587).
/// Network.framework kann das nicht, darum wird diese Variante nur für `security: "starttls"` verwendet.
final class StreamTransport: MailTransport, @unchecked Sendable {
    private let input: InputStream
    private let output: OutputStream
    private let host: String
    private let allowInsecureLoopbackCertificate: Bool
    private let readQueue = DispatchQueue(label: "de.lunaos.lumail.stream.read")
    private let writeQueue = DispatchQueue(label: "de.lunaos.lumail.stream.write")

    private init(input: InputStream, output: OutputStream, host: String, allowInsecure: Bool) {
        self.input = input
        self.output = output
        self.host = host
        allowInsecureLoopbackCertificate = allowInsecure
    }

    static func connect(host: String, port: Int, allowInsecureLoopbackCertificate: Bool) async throws -> StreamTransport {
        var inputStream: InputStream?
        var outputStream: OutputStream?
        Stream.getStreamsToHost(withName: host, port: port, inputStream: &inputStream, outputStream: &outputStream)
        guard let input = inputStream, let output = outputStream else {
            throw TransportError.connectionFailed("\(host):\(port) nicht erreichbar")
        }
        let transport = StreamTransport(input: input, output: output, host: host, allowInsecure: allowInsecureLoopbackCertificate)
        input.open()
        output.open()
        // Auf den Verbindungsaufbau warten (höchstens 20 Sekunden).
        let deadline = Date().addingTimeInterval(20)
        while input.streamStatus == .opening || output.streamStatus == .opening {
            if Date() > deadline {
                transport.close()
                throw TransportError.connectionFailed("\(host):\(port) – Zeitüberschreitung")
            }
            try await Task.sleep(nanoseconds: 50_000_000)
        }
        if let error = input.streamError ?? output.streamError {
            transport.close()
            throw TransportError.connectionFailed("\(host):\(port) – \(error.localizedDescription)")
        }
        return transport
    }

    func send(_ data: Data) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            writeQueue.async { [output] in
                var offset = 0
                let bytes = [UInt8](data)
                while offset < bytes.count {
                    let written = bytes[offset...].withUnsafeBufferPointer { output.write($0.baseAddress!, maxLength: $0.count) }
                    if written <= 0 {
                        continuation.resume(throwing: TransportError.connectionFailed(output.streamError?.localizedDescription ?? "Schreiben fehlgeschlagen"))
                        return
                    }
                    offset += written
                }
                continuation.resume()
            }
        }
    }

    func receive() async throws -> Data {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Data, Error>) in
            readQueue.async { [input] in
                var buffer = [UInt8](repeating: 0, count: 64 * 1024)
                let count = input.read(&buffer, maxLength: buffer.count)
                if count > 0 {
                    continuation.resume(returning: Data(buffer[0..<count]))
                } else if count == 0 {
                    continuation.resume(throwing: TransportError.closed)
                } else {
                    continuation.resume(throwing: TransportError.connectionFailed(input.streamError?.localizedDescription ?? "Lesen fehlgeschlagen"))
                }
            }
        }
    }

    func startTLS() async throws {
        var settings: [String: Any] = [
            kCFStreamSSLLevel as String: kCFStreamSocketSecurityLevelNegotiatedSSL,
            kCFStreamSSLPeerName as String: host,
        ]
        if allowInsecureLoopbackCertificate && NetworkTransport.isLoopback(host) {
            settings[kCFStreamSSLValidatesCertificateChain as String] = false
        }
        let key = CFStreamPropertyKey(rawValue: kCFStreamPropertySSLSettings)
        guard CFReadStreamSetProperty(input as CFReadStream, key, settings as CFDictionary),
              CFWriteStreamSetProperty(output as CFWriteStream, key, settings as CFDictionary) else {
            throw TransportError.connectionFailed("TLS konnte nicht gestartet werden")
        }
    }

    func close() {
        input.close()
        output.close()
    }
}
