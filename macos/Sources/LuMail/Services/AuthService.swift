import AppKit
import Foundation
import LuMailCore

/// Anmeldung über den Standardbrowser und Verwaltung der Zugangsdaten.
final class AuthService: @unchecked Sendable {
    let paths: AppPaths
    let transports: MailTransportFactory
    private let oauthService = OAuthService()
    private let refreshLock = NSLock()
    private var refreshTasks: [UUID: Task<OAuthTokens, Error>] = [:]

    init(paths: AppPaths, transports: MailTransportFactory) {
        self.paths = paths
        self.transports = transports
    }

    /// Eigene OAuth-Client-Daten aus `oauth.json` (bei jedem Aufruf frisch gelesen).
    func oauthCredentials(for provider: Provider) -> OAuthClientCredentials? {
        guard provider.authMethods.contains(.oauth2), provider.oauth != nil else { return nil }
        return OAuthClientCredentials.resolve(userFile: paths.oauthFile)[provider.id]
    }

    func usesOAuth(_ provider: Provider) -> Bool {
        oauthCredentials(for: provider) != nil
    }

    // MARK: Anmeldung

    /// Öffnet den Browser und liefert das neue Konto, sobald die Anmeldung dort abgeschlossen ist.
    func signIn(provider: Provider, emailHint: String?, serversHint: CustomServers? = nil) async throws -> Account {
        if let credentials = oauthCredentials(for: provider), let endpoints = provider.oauth {
            return try await signInWithOAuth(provider: provider, endpoints: endpoints, credentials: credentials, emailHint: emailHint)
        }
        guard provider.authMethods.contains(.password) else {
            // z. B. Outlook: nur OAuth möglich, aber keine Client-ID eingetragen.
            throw OAuthError.notConfigured(provider.name)
        }
        return try await signInWithPassword(provider: provider, emailHint: emailHint, serversHint: serversHint)
    }

    private func signInWithOAuth(provider: Provider, endpoints: OAuthEndpoints, credentials: OAuthClientCredentials,
                                 emailHint: String?) async throws -> Account {
        let server = try await LoopbackServer.start()
        defer { server.stop() }
        let flow = OAuthFlow(endpoints: endpoints, credentials: credentials, port: server.port)
        server.attach(BrowserLoginRouter(mode: .oauth, port: server.port, state: flow.state))
        await openInBrowser(flow.authorizationURL(loginHint: emailHint))

        guard case .authorizationCode(let code) = try await server.waitForOutcome() else {
            throw OAuthError.authorizationDenied("unerwartete Antwort")
        }
        let tokens = try await oauthService.exchange(flow: flow, code: code)
        guard let email = await oauthService.email(endpoints: endpoints, tokens: tokens, fallback: emailHint) else {
            throw OAuthError.missingEmail
        }
        // Prüfen, ob IMAP mit dem Token wirklich funktioniert.
        try await MailAccountSession.verify(provider: provider, credential: .oauth(user: email, accessToken: tokens.accessToken),
                                            transports: transports)
        let account = Account(providerId: provider.id, email: email, displayName: email, authMethod: .oauth2)
        try Keychain.save(AccountSecret(password: nil, tokens: tokens), for: account.id)
        return account
    }

    private func signInWithPassword(provider: Provider, emailHint: String?, serversHint: CustomServers?) async throws -> Account {
        let server = try await LoopbackServer.start()
        defer { server.stop() }
        let transports = transports
        let router = BrowserLoginRouter(mode: .password(provider, emailHint: emailHint, serversHint: serversHint), port: server.port) { credentials in
            do {
                try await MailAccountSession.verify(provider: provider, endpoint: credentials.servers?.imap,
                                                    credential: .password(user: credentials.email, password: credentials.password),
                                                    transports: transports)
                return nil
            } catch {
                return Self.friendlyLoginError(error, provider: provider)
            }
        }
        server.attach(router)
        await openInBrowser(router.loginPageURL)

        guard case .credentials(let credentials) = try await server.waitForOutcome() else {
            throw OAuthError.authorizationDenied("unerwartete Antwort")
        }
        let account = Account(providerId: provider.id, email: credentials.email, displayName: credentials.email,
                              authMethod: .password, customServers: credentials.servers)
        try Keychain.save(AccountSecret(password: credentials.password, tokens: nil), for: account.id)
        return account
    }

    static func friendlyLoginError(_ error: Error, provider: Provider) -> String {
        switch error {
        case IMAPError.authenticationFailed:
            return "\(provider.name) hat die Anmeldung abgelehnt. Bitte prüfe E-Mail-Adresse und \(provider.passwordLabel)."
        case TransportError.connectionFailed(let reason) where provider.id == "proton":
            return "Die Proton Mail Bridge ist nicht erreichbar (\(reason)). Läuft die Bridge und ist der Verbindungsmodus auf „SSL“ gestellt?"
        case IMAPError.commandFailed(_, let response) where provider.id == "gmx" || provider.id == "webde":
            return "\(provider.name) verweigert den Zugriff (\(response)). Ist der IMAP-Abruf in den Postfach-Einstellungen aktiviert?"
        default:
            return (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        }
    }

    @MainActor
    private func openInBrowser(_ url: URL) {
        NSWorkspace.shared.open(url)
    }

    // MARK: Zugangsdaten für laufende Sitzungen

    func credential(for account: Account, provider: Provider) async throws -> MailCredential {
        guard let secret = try Keychain.load(for: account.id) else { throw OAuthError.missingRefreshToken }
        switch account.authMethod {
        case .password:
            return .password(user: account.email, password: secret.password ?? "")
        case .oauth2:
            guard var tokens = secret.tokens else { throw OAuthError.missingRefreshToken }
            if tokens.needsRefresh() {
                tokens = try await refreshTokens(for: account, provider: provider, current: tokens)
            }
            return .oauth(user: account.email, accessToken: tokens.accessToken)
        }
    }

    /// Erneuert Tokens – gleichzeitige Anfragen für dasselbe Konto teilen sich eine Erneuerung.
    private func refreshTokens(for account: Account, provider: Provider, current: OAuthTokens) async throws -> OAuthTokens {
        let task: Task<OAuthTokens, Error> = refreshLock.withLock {
            if let running = refreshTasks[account.id] { return running }
            let task = Task { [oauthService, paths] () throws -> OAuthTokens in
                guard let endpoints = provider.oauth,
                      let credentials = OAuthClientCredentials.resolve(userFile: paths.oauthFile)[provider.id] else {
                    throw OAuthError.notConfigured(provider.name)
                }
                let fresh = try await oauthService.refresh(endpoints: endpoints, credentials: credentials, tokens: current)
                try Keychain.save(AccountSecret(password: nil, tokens: fresh), for: account.id)
                return fresh
            }
            refreshTasks[account.id] = task
            return task
        }
        defer { refreshLock.withLock { refreshTasks[account.id] = nil } }
        return try await task.value
    }

    func forget(_ account: Account) {
        Keychain.delete(for: account.id)
    }
}
