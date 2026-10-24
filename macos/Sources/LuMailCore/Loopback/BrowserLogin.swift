import Foundation

/// Minimale HTTP/1.1-Anfrage für den lokalen Anmelde-Server.
public struct HTTPRequest: Sendable, Equatable {
    public var method: String
    public var path: String
    public var query: [String: String]
    public var headers: [String: String]
    public var body: Data

    public enum ParseResult: Equatable {
        case incomplete
        case invalid
        case complete(HTTPRequest)
    }

    public static let maximumSize = 64 * 1024

    public static func parse(_ data: Data) -> ParseResult {
        guard data.count <= maximumSize else { return .invalid }
        guard let headerEnd = data.range(of: Data("\r\n\r\n".utf8)) else { return .incomplete }
        let head = String(decoding: data[data.startIndex..<headerEnd.lowerBound], as: UTF8.self)
        var lines = head.components(separatedBy: "\r\n")
        let requestLine = lines.removeFirst().split(separator: " ")
        guard requestLine.count == 3, requestLine[2].hasPrefix("HTTP/1.") else { return .invalid }

        var headers: [String: String] = [:]
        for line in lines {
            guard let colon = line.firstIndex(of: ":") else { continue }
            headers[line[..<colon].trimmingCharacters(in: .whitespaces).lowercased()] =
                line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
        }
        let contentLength = Int(headers["content-length"] ?? "0") ?? 0
        guard contentLength >= 0, contentLength <= maximumSize else { return .invalid }
        let body = data[headerEnd.upperBound...]
        if body.count < contentLength { return .incomplete }

        let target = String(requestLine[1])
        let pathPart = target.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false)
        let query = pathPart.count > 1 ? FormEncoding.decode(String(pathPart[1])) : [:]
        return .complete(HTTPRequest(method: String(requestLine[0]).uppercased(),
                                     path: String(pathPart[0]).removingPercentEncoding ?? String(pathPart[0]),
                                     query: query, headers: headers, body: Data(body.prefix(contentLength))))
    }
}

public struct HTTPResponse: Sendable, Equatable {
    public var status: Int
    public var body: String

    public init(status: Int, body: String) {
        self.status = status
        self.body = body
    }

    public var serialized: Data {
        let reason: String
        switch status {
        case 200: reason = "OK"
        case 400: reason = "Bad Request"
        case 403: reason = "Forbidden"
        case 404: reason = "Not Found"
        case 405: reason = "Method Not Allowed"
        default: reason = "Status"
        }
        let bodyData = Data(body.utf8)
        let head = [
            "HTTP/1.1 \(status) \(reason)",
            "Content-Type: text/html; charset=utf-8",
            "Content-Length: \(bodyData.count)",
            "Cache-Control: no-store",
            "X-Frame-Options: DENY",
            "Referrer-Policy: no-referrer",
            "Connection: close",
            "", "",
        ].joined(separator: "\r\n")
        return Data(head.utf8) + bodyData
    }
}

/// Füllt die Vorlagen aus `shared/login/`.
public enum LoginPages {
    public static func login(provider: Provider, state: String, email: String?, error: String?,
                             servers: CustomServers? = nil) -> String {
        var extra = ""
        if provider.needsCustomServers {
            let imap = servers?.imap ?? ServerEndpoint(host: "", port: 993, security: "tls")
            let smtp = servers?.smtp ?? ServerEndpoint(host: "", port: 465, security: "tls")
            extra = render(SharedResources.customServersHTML, [
                "IMAP_HOST": imap.host, "IMAP_PORT": String(imap.port),
                "IMAP_TLS_SELECTED": imap.usesSTARTTLS ? "" : "selected", "IMAP_STARTTLS_SELECTED": imap.usesSTARTTLS ? "selected" : "",
                "SMTP_HOST": smtp.host, "SMTP_PORT": String(smtp.port),
                "SMTP_TLS_SELECTED": smtp.usesSTARTTLS ? "" : "selected", "SMTP_STARTTLS_SELECTED": smtp.usesSTARTTLS ? "selected" : "",
            ])
        }
        // EXTRA_FIELDS ist bereits fertiges (escaptes) HTML und wird roh eingesetzt.
        return render(SharedResources.loginHTML, [
            "PROVIDER_NAME": provider.name,
            "PROVIDER_COLOR": provider.color,
            "PASSWORD_LABEL": provider.passwordLabel,
            "PASSWORD_HELP": provider.passwordHelp,
            "HELP_URL": provider.helpUrl,
            "STATE": state,
            "EMAIL": email ?? "",
            "ERROR": error ?? "",
        ]).replacingOccurrences(of: "{{EXTRA_FIELDS}}", with: extra)
    }

    /// Liest und prüft die Server-Felder des Formulars („Andere (IMAP)“).
    public static func parseServers(_ form: [String: String]) -> Result<CustomServers, LoginFormError> {
        func endpoint(_ prefix: String, defaultPort: Int) -> ServerEndpoint? {
            let host = (form["\(prefix)Host"] ?? "").trimmingCharacters(in: .whitespaces).lowercased()
            let port = Int(form["\(prefix)Port"] ?? "") ?? defaultPort
            let security = form["\(prefix)Security"] == "starttls" ? "starttls" : "tls"
            let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyz0123456789.-:[]")
            guard !host.isEmpty, host.count <= 253, host.unicodeScalars.allSatisfy(allowed.contains), (1...65535).contains(port) else { return nil }
            return ServerEndpoint(host: host, port: port, security: security)
        }
        guard let imap = endpoint("imap", defaultPort: 993) else { return .failure(.invalidServer("IMAP")) }
        guard let smtp = endpoint("smtp", defaultPort: 465) else { return .failure(.invalidServer("SMTP")) }
        return .success(CustomServers(imap: imap, smtp: smtp))
    }

    public static func done(title: String, message: String) -> String {
        render(SharedResources.doneHTML, ["TITLE": title, "MESSAGE": message])
    }

    /// Ersetzt `{{NAME}}` – alle Werte werden HTML-escaped.
    public static func render(_ template: String, _ values: [String: String]) -> String {
        var result = template
        for (key, value) in values {
            result = result.replacingOccurrences(of: "{{\(key)}}", with: HTMLText.escape(value))
        }
        return result
    }
}

public enum LoginFormError: Error, Equatable {
    case invalidServer(String)

    public var message: String {
        switch self {
        case .invalidServer(let kind): return "Bitte einen gültigen \(kind)-Server und Port angeben."
        }
    }
}

/// Zugangsdaten aus der Browser-Anmeldeseite.
public struct BrowserCredentials: Sendable, Equatable {
    public var email: String
    public var password: String
    /// Nur bei „Andere (IMAP)“.
    public var servers: CustomServers?

    public init(email: String, password: String, servers: CustomServers? = nil) {
        self.email = email
        self.password = password
        self.servers = servers
    }
}

/// Was der Browser zurückgeliefert hat.
public enum BrowserLoginOutcome: Sendable, Equatable {
    case authorizationCode(String)
    case credentials(BrowserCredentials)
}

/// Die Logik des lokalen Anmelde-Servers – unabhängig vom eigentlichen Socket (der lebt in der App).
public actor BrowserLoginRouter {
    public enum Mode: Sendable {
        /// OAuth: wartet auf `GET /callback?code&state`.
        case oauth
        /// Passwort: zeigt `GET /login/<state>` und nimmt `POST /login` an.
        case password(Provider, emailHint: String?)
    }

    /// Prüft Zugangsdaten (IMAP-LOGIN) und liefert bei Fehler eine Meldung für die Seite.
    public typealias Verifier = @Sendable (BrowserCredentials) async -> String?

    public let state: String
    public let port: UInt16
    private let mode: Mode
    private let verify: Verifier
    public private(set) var isFinished = false

    public init(mode: Mode, port: UInt16, state: String = Data.secureRandom(count: 32).base64URLEncodedString,
                verify: @escaping Verifier = { _ in nil }) {
        self.mode = mode
        self.port = port
        self.state = state
        self.verify = verify
    }

    /// Die Adresse, die im Browser geöffnet wird (nur im Passwort-Modus).
    public nonisolated var loginPageURL: URL {
        URL(string: "http://127.0.0.1:\(port)/login/\(state)")!
    }

    public struct Result: Sendable {
        public var response: HTTPResponse
        public var outcome: Swift.Result<BrowserLoginOutcome, OAuthError>?
    }

    public func handle(_ request: HTTPRequest) async -> Result {
        // DNS-Rebinding-Schutz: nur Loopback-Hostnamen mit unserem Port.
        let allowedHosts = ["127.0.0.1:\(port)", "localhost:\(port)", "[::1]:\(port)"]
        guard let host = request.headers["host"]?.lowercased(), allowedHosts.contains(host) else {
            return Result(response: HTTPResponse(status: 403, body: "Forbidden"), outcome: nil)
        }
        guard !isFinished else {
            return Result(response: HTTPResponse(status: 200, body: LoginPages.done(
                title: "Schon erledigt", message: "Diese Anmeldung ist abgeschlossen. Du kannst das Fenster schließen.")), outcome: nil)
        }

        switch mode {
        case .oauth:
            guard request.method == "GET", request.path == "/callback" else { return notFound }
            guard request.query["state"] == state else {
                return Result(response: HTTPResponse(status: 400, body: LoginPages.done(
                    title: "Ungültige Anfrage", message: "Diese Antwort gehört nicht zu deiner Anmeldung.")), outcome: nil)
            }
            isFinished = true
            if let error = request.query["error"] {
                let reason = request.query["error_description"] ?? error
                return Result(response: HTTPResponse(status: 200, body: LoginPages.done(
                    title: "Anmeldung abgebrochen", message: "Der Anbieter meldet: \(reason)")),
                              outcome: .failure(.authorizationDenied(reason)))
            }
            guard let code = request.query["code"], !code.isEmpty else {
                return Result(response: HTTPResponse(status: 400, body: LoginPages.done(
                    title: "Ungültige Anfrage", message: "Es wurde kein Anmeldecode übermittelt.")),
                              outcome: .failure(.authorizationDenied("kein Code")))
            }
            return Result(response: HTTPResponse(status: 200, body: LoginPages.done(
                title: "Angemeldet!", message: "Du kannst dieses Fenster schließen und zu Lu-Mail zurückkehren.")),
                          outcome: .success(.authorizationCode(code)))

        case .password(let provider, let emailHint):
            if request.method == "GET", request.path == "/login/\(state)" {
                return Result(response: HTTPResponse(status: 200, body: LoginPages.login(
                    provider: provider, state: state, email: emailHint, error: nil)), outcome: nil)
            }
            guard request.path == "/login" else { return notFound }
            guard request.method == "POST" else {
                return Result(response: HTTPResponse(status: 405, body: "Method Not Allowed"), outcome: nil)
            }
            let form = FormEncoding.decode(String(decoding: request.body, as: UTF8.self))
            guard form["state"] == state else {
                return Result(response: HTTPResponse(status: 403, body: LoginPages.done(
                    title: "Ungültige Anfrage", message: "Diese Anmeldeseite ist abgelaufen. Bitte starte die Anmeldung in Lu-Mail neu.")), outcome: nil)
            }
            let email = (form["email"] ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            let password = form["password"] ?? ""
            var servers: CustomServers?
            var formError: String?
            if provider.needsCustomServers {
                switch LoginPages.parseServers(form) {
                case .success(let parsed): servers = parsed
                case .failure(let error): formError = error.message
                }
            }
            func page(_ error: String) -> Result {
                Result(response: HTTPResponse(status: 200, body: LoginPages.login(
                    provider: provider, state: state, email: email, error: error, servers: servers)), outcome: nil)
            }
            guard email.contains("@"), !password.isEmpty else { return page("Bitte E-Mail-Adresse und Passwort eingeben.") }
            if let formError { return page(formError) }
            let credentials = BrowserCredentials(email: email, password: password, servers: servers)
            if let error = await verify(credentials) { return page(error) }
            isFinished = true
            return Result(response: HTTPResponse(status: 200, body: LoginPages.done(
                title: "Angemeldet!", message: "\(email) wurde zu Lu-Mail hinzugefügt. Du kannst dieses Fenster schließen.")),
                          outcome: .success(.credentials(credentials)))
        }
    }

    private var notFound: Result {
        Result(response: HTTPResponse(status: 404, body: "Not Found"), outcome: nil)
    }
}
