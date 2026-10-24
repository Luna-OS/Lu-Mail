import AppKit
import Foundation
import LuMailCore
import Network

/// Einmaliger HTTP-Server auf 127.0.0.1/::1 für die Browser-Anmeldung.
/// Die eigentliche Logik (Routen, state-Prüfung, Seiten) steckt in `BrowserLoginRouter` (LuMailCore).
final class LoopbackServer: @unchecked Sendable {
    private let listener: NWListener
    private let queue = DispatchQueue(label: "de.lunaos.lumail.loopback")
    private var router: BrowserLoginRouter?
    private let once = ResumeOnce()
    private var continuation: CheckedContinuation<BrowserLoginOutcome, Error>?
    /// Ergebnis, das ankam, bevor jemand darauf gewartet hat.
    private var pendingResult: Result<BrowserLoginOutcome, Error>?
    private let lock = NSLock()

    private(set) var port: UInt16 = 0

    private init(listener: NWListener) {
        self.listener = listener
    }

    /// Startet den Server auf einem zufälligen freien Port (nur Loopback-Schnittstelle).
    static func start() async throws -> LoopbackServer {
        let parameters = NWParameters.tcp
        parameters.requiredInterfaceType = .loopback
        parameters.allowLocalEndpointReuse = true
        let listener = try NWListener(using: parameters)
        let server = LoopbackServer(listener: listener)
        let readyOnce = ResumeOnce()
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            listener.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    server.port = listener.port?.rawValue ?? 0
                    readyOnce.run { continuation.resume() }
                case .failed(let error):
                    readyOnce.run { continuation.resume(throwing: error) }
                default:
                    break
                }
            }
            listener.newConnectionHandler = { [weak server] connection in
                server?.handle(connection)
            }
            listener.start(queue: server.queue)
        }
        return server
    }

    func attach(_ router: BrowserLoginRouter) {
        lock.lock()
        self.router = router
        lock.unlock()
    }

    /// Wartet, bis der Browser ein Ergebnis liefert (oder 10 Minuten vergehen / abgebrochen wird).
    func waitForOutcome(timeout: TimeInterval = 600) async throws -> BrowserLoginOutcome {
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<BrowserLoginOutcome, Error>) in
                lock.lock()
                if let pending = pendingResult {
                    lock.unlock()
                    continuation.resume(with: pending)
                    return
                }
                self.continuation = continuation
                lock.unlock()
                queue.asyncAfter(deadline: .now() + timeout) { [weak self] in
                    self?.finish(.failure(OAuthError.authorizationDenied("Zeitüberschreitung – bitte erneut versuchen")))
                }
            }
        } onCancel: {
            self.finish(.failure(CancellationError()))
        }
    }

    func stop() {
        listener.cancel()
    }

    private func finish(_ result: Result<BrowserLoginOutcome, Error>) {
        once.run {
            lock.lock()
            let continuation = self.continuation
            if continuation == nil { pendingResult = result }
            lock.unlock()
            continuation?.resume(with: result)
            // Kurz warten, damit die letzte Seite noch ausgeliefert wird.
            queue.asyncAfter(deadline: .now() + 1) { [weak self] in self?.stop() }
        }
    }

    private func handle(_ connection: NWConnection) {
        connection.start(queue: queue)
        receive(on: connection, buffer: Data())
    }

    private func receive(on connection: NWConnection, buffer: Data) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 64 * 1024) { [weak self] data, _, isComplete, error in
            guard let self else { connection.cancel(); return }
            var buffer = buffer
            if let data { buffer.append(data) }
            switch HTTPRequest.parse(buffer) {
            case .incomplete where !isComplete && error == nil:
                self.receive(on: connection, buffer: buffer)
            case .complete(let request):
                Task { await self.respond(to: request, on: connection) }
            default:
                self.send(HTTPResponse(status: 400, body: "Bad Request"), on: connection)
            }
        }
    }

    private func respond(to request: HTTPRequest, on connection: NWConnection) async {
        let router = lock.withLock { self.router }
        guard let router else {
            send(HTTPResponse(status: 503, body: "Nicht bereit"), on: connection)
            return
        }
        let result = await router.handle(request)
        send(result.response, on: connection)
        if let outcome = result.outcome {
            finish(outcome.mapError { $0 as Error })
        }
    }

    private func send(_ response: HTTPResponse, on connection: NWConnection) {
        connection.send(content: response.serialized, contentContext: .finalMessage, isComplete: true,
                        completion: .contentProcessed { _ in connection.cancel() })
    }
}
