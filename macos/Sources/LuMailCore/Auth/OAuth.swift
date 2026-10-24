import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public extension Data {
    /// Base64url ohne Auffüllzeichen (RFC 4648, Abschnitt 5).
    var base64URLEncodedString: String {
        base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    init?(base64URLEncoded text: String) {
        var base64 = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while base64.count % 4 != 0 { base64 += "=" }
        self.init(base64Encoded: base64)
    }

    static func secureRandom(count: Int) -> Data {
        var generator = SystemRandomNumberGenerator()
        return Data((0..<count).map { _ in UInt8.random(in: .min ... .max, using: &generator) })
    }
}

/// Proof Key for Code Exchange (RFC 7636).
public struct PKCE: Sendable {
    public let verifier: String
    public let challenge: String
    public let method = "S256"

    public init(verifier: String = Data.secureRandom(count: 48).base64URLEncodedString) {
        self.verifier = verifier
        challenge = SHA256.hash(Data(verifier.utf8)).base64URLEncodedString
    }
}

public enum XOAuth2 {
    /// `base64("user=" + user + "^Aauth=Bearer " + token + "^A^A")`
    public static func initialResponse(user: String, accessToken: String) -> String {
        Data("user=\(user)\u{01}auth=Bearer \(accessToken)\u{01}\u{01}".utf8).base64EncodedString()
    }
}

/// Eigene OAuth-Client-Daten aus `oauth.json` (nicht im Repository!).
public struct OAuthClientCredentials: Codable, Sendable, Equatable {
    public var clientId: String
    public var clientSecret: String?

    public init(clientId: String, clientSecret: String? = nil) {
        self.clientId = clientId
        self.clientSecret = clientSecret
    }

    /// Nutzer-`oauth.json`, ergänzt um die eingebauten Client-IDs (docs/FUNKTIONEN.md §18).
    public static func resolve(userFile url: URL, builtIn: String = BuiltInOAuth.json) -> [String: OAuthClientCredentials] {
        var result = parse(Data(builtIn.utf8))
        for (key, value) in load(from: url) { result[key] = value }
        return result
    }

    /// Liest `oauth.json`; Einträge ohne Client-ID werden ignoriert.
    public static func load(from url: URL) -> [String: OAuthClientCredentials] {
        guard let data = try? Data(contentsOf: url) else { return [:] }
        return parse(data)
    }

    static func parse(_ data: Data) -> [String: OAuthClientCredentials] {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [:] }
        var result: [String: OAuthClientCredentials] = [:]
        for (key, value) in object {
            guard let entry = value as? [String: Any], let clientId = entry["clientId"] as? String,
                  !clientId.trimmingCharacters(in: .whitespaces).isEmpty else { continue }
            let secret = (entry["clientSecret"] as? String).flatMap { $0.isEmpty ? nil : $0 }
            result[key] = OAuthClientCredentials(clientId: clientId, clientSecret: secret)
        }
        return result
    }
}

/// Gespeicherte Tokens eines OAuth-Kontos (liegen im Schlüsselbund).
public struct OAuthTokens: Codable, Sendable, Equatable {
    public var accessToken: String
    public var refreshToken: String?
    public var expiresAt: Date
    public var idToken: String?

    public init(accessToken: String, refreshToken: String?, expiresAt: Date, idToken: String? = nil) {
        self.accessToken = accessToken
        self.refreshToken = refreshToken
        self.expiresAt = expiresAt
        self.idToken = idToken
    }

    /// Muss erneuert werden, wenn es in weniger als 60 Sekunden abläuft.
    public func needsRefresh(now: Date = Date()) -> Bool {
        expiresAt.timeIntervalSince(now) < 60
    }
}

public enum OAuthError: Error, LocalizedError, Equatable {
    case notConfigured(String)
    case stateMismatch
    case authorizationDenied(String)
    case tokenRequestFailed(String)
    case missingEmail
    case missingRefreshToken

    public var errorDescription: String? {
        switch self {
        case .notConfigured(let provider): return "Für \(provider) ist keine OAuth-Client-ID in oauth.json hinterlegt."
        case .stateMismatch: return "Die Antwort aus dem Browser passt nicht zu dieser Anmeldung."
        case .authorizationDenied(let reason): return "Die Anmeldung wurde im Browser abgebrochen: \(reason)"
        case .tokenRequestFailed(let reason): return "Token-Abruf fehlgeschlagen: \(reason)"
        case .missingEmail: return "Die E-Mail-Adresse konnte nicht ermittelt werden. Bitte gib sie vor der Anmeldung ein."
        case .missingRefreshToken: return "Die Anmeldung ist abgelaufen. Bitte melde dich erneut im Browser an."
        }
    }
}

/// Baut OAuth-URLs und spricht mit dem Token-Endpunkt.
public struct OAuthFlow: Sendable {
    public let endpoints: OAuthEndpoints
    public let credentials: OAuthClientCredentials
    public let redirectURI: String
    public let state: String
    public let pkce: PKCE

    public init(endpoints: OAuthEndpoints, credentials: OAuthClientCredentials, port: UInt16,
                state: String = Data.secureRandom(count: 32).base64URLEncodedString, pkce: PKCE = PKCE()) {
        self.endpoints = endpoints
        self.credentials = credentials
        self.redirectURI = endpoints.redirectUriTemplate.replacingOccurrences(of: "{port}", with: String(port))
        self.state = state
        self.pkce = pkce
    }

    public func authorizationURL(loginHint: String?) -> URL {
        var components = URLComponents(string: endpoints.authorizationEndpoint)!
        var items = [
            URLQueryItem(name: "response_type", value: "code"),
            URLQueryItem(name: "client_id", value: credentials.clientId),
            URLQueryItem(name: "redirect_uri", value: redirectURI),
            URLQueryItem(name: "scope", value: endpoints.scopes.joined(separator: " ")),
            URLQueryItem(name: "state", value: state),
            URLQueryItem(name: "code_challenge", value: pkce.challenge),
            URLQueryItem(name: "code_challenge_method", value: pkce.method),
        ]
        if let loginHint, !loginHint.isEmpty { items.append(URLQueryItem(name: "login_hint", value: loginHint)) }
        for (key, value) in endpoints.extraParams.sorted(by: { $0.key < $1.key }) {
            items.append(URLQueryItem(name: key, value: value))
        }
        components.queryItems = (components.queryItems ?? []) + items
        // „+“ in Scopes etc. sicher kodieren.
        components.percentEncodedQuery = components.percentEncodedQuery?.replacingOccurrences(of: "+", with: "%2B")
        return components.url!
    }

    public func tokenRequest(code: String) -> URLRequest {
        var form = [
            "grant_type": "authorization_code",
            "code": code,
            "redirect_uri": redirectURI,
            "client_id": credentials.clientId,
            "code_verifier": pkce.verifier,
        ]
        if let secret = credentials.clientSecret { form["client_secret"] = secret }
        return Self.formRequest(url: endpoints.tokenEndpoint, form: form)
    }

    public static func refreshRequest(endpoints: OAuthEndpoints, credentials: OAuthClientCredentials, refreshToken: String) -> URLRequest {
        var form = [
            "grant_type": "refresh_token",
            "refresh_token": refreshToken,
            "client_id": credentials.clientId,
        ]
        if let secret = credentials.clientSecret { form["client_secret"] = secret }
        return formRequest(url: endpoints.tokenEndpoint, form: form)
    }

    static func formRequest(url: String, form: [String: String]) -> URLRequest {
        var request = URLRequest(url: URL(string: url)!)
        request.httpMethod = "POST"
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.httpBody = Data(FormEncoding.encode(form).utf8)
        return request
    }

    /// Wertet die JSON-Antwort des Token-Endpunkts aus.
    public static func parseTokenResponse(_ data: Data, previousRefreshToken: String? = nil, now: Date = Date()) throws -> OAuthTokens {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw OAuthError.tokenRequestFailed(String(decoding: data.prefix(300), as: UTF8.self))
        }
        guard let accessToken = object["access_token"] as? String else {
            let reason = (object["error_description"] as? String) ?? (object["error"] as? String) ?? "unbekannter Fehler"
            throw OAuthError.tokenRequestFailed(reason)
        }
        let expiresIn = (object["expires_in"] as? NSNumber)?.doubleValue ?? Double(object["expires_in"] as? String ?? "") ?? 3600
        return OAuthTokens(
            accessToken: accessToken,
            refreshToken: (object["refresh_token"] as? String) ?? previousRefreshToken,
            expiresAt: now.addingTimeInterval(expiresIn),
            idToken: object["id_token"] as? String
        )
    }

    /// Liest den Claim `email` aus einem JWT (ohne Signaturprüfung – das Token kommt direkt per TLS vom Anbieter).
    public static func email(fromIDToken token: String) -> String? {
        let parts = token.split(separator: ".")
        guard parts.count >= 2, let payload = Data(base64URLEncoded: String(parts[1])),
              let object = try? JSONSerialization.jsonObject(with: payload) as? [String: Any] else { return nil }
        if let email = object["email"] as? String { return email }
        // Microsoft-Konten liefern die Adresse teils nur als „preferred_username“.
        if let username = object["preferred_username"] as? String, username.contains("@") { return username }
        return nil
    }

    public static func email(fromUserInfo data: Data) -> String? {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return object["email"] as? String
    }
}

public enum FormEncoding {
    private static let allowed: CharacterSet = {
        var set = CharacterSet.alphanumerics
        set.insert(charactersIn: "-._~")
        return set
    }()

    public static func encode(_ form: [String: String]) -> String {
        form.sorted { $0.key < $1.key }
            .map { "\(escape($0.key))=\(escape($0.value))" }
            .joined(separator: "&")
    }

    public static func escape(_ text: String) -> String {
        text.addingPercentEncoding(withAllowedCharacters: allowed) ?? text
    }

    /// Dekodiert `a=1&b=x+y%21`.
    public static func decode(_ text: String) -> [String: String] {
        var result: [String: String] = [:]
        for pair in text.split(separator: "&") {
            let pieces = pair.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
            let key = unescape(String(pieces[0]))
            let value = pieces.count > 1 ? unescape(String(pieces[1])) : ""
            result[key] = value
        }
        return result
    }

    static func unescape(_ text: String) -> String {
        text.replacingOccurrences(of: "+", with: " ").removingPercentEncoding ?? text
    }
}

/// Erledigt Token-Tausch, Erneuerung und E-Mail-Ermittlung über HTTPS.
public struct OAuthService: Sendable {
    public init() {}

    public func exchange(flow: OAuthFlow, code: String) async throws -> OAuthTokens {
        let (data, _) = try await URLSession.shared.data(for: flow.tokenRequest(code: code))
        return try OAuthFlow.parseTokenResponse(data)
    }

    public func refresh(endpoints: OAuthEndpoints, credentials: OAuthClientCredentials, tokens: OAuthTokens) async throws -> OAuthTokens {
        guard let refreshToken = tokens.refreshToken else { throw OAuthError.missingRefreshToken }
        let request = OAuthFlow.refreshRequest(endpoints: endpoints, credentials: credentials, refreshToken: refreshToken)
        let (data, _) = try await URLSession.shared.data(for: request)
        return try OAuthFlow.parseTokenResponse(data, previousRefreshToken: refreshToken)
    }

    /// Ermittelt die E-Mail-Adresse nach `emailSource` aus providers.json.
    public func email(endpoints: OAuthEndpoints, tokens: OAuthTokens, fallback: String?) async -> String? {
        if endpoints.emailSource == "idToken", let idToken = tokens.idToken, let email = OAuthFlow.email(fromIDToken: idToken) {
            return email
        }
        if endpoints.emailSource == "userInfo", let base = endpoints.userInfoEndpoint,
           var components = URLComponents(string: base) {
            components.queryItems = [URLQueryItem(name: "access_token", value: tokens.accessToken)]
            if let url = components.url {
                var request = URLRequest(url: url)
                request.setValue("Bearer \(tokens.accessToken)", forHTTPHeaderField: "Authorization")
                if let (data, _) = try? await URLSession.shared.data(for: request), let email = OAuthFlow.email(fromUserInfo: data) {
                    return email
                }
            }
        }
        return fallback.flatMap { $0.isEmpty ? nil : $0 }
    }
}
