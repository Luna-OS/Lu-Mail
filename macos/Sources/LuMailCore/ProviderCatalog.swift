import Foundation

public struct ServerEndpoint: Codable, Hashable, Sendable {
    public var host: String
    public var port: Int
    /// "tls" = TLS ab dem ersten Byte (IMAPS 993 / SMTPS 465), "starttls" = Klartext-Start, dann STARTTLS.
    public var security: String

    public init(host: String, port: Int, security: String) {
        self.host = host
        self.port = port
        self.security = security
    }

    public var usesSTARTTLS: Bool { security.lowercased() == "starttls" }
}

/// Eigene Server für „Andere (IMAP)“, pro Konto gespeichert.
public struct CustomServers: Codable, Hashable, Sendable {
    public var imap: ServerEndpoint
    public var smtp: ServerEndpoint

    public init(imap: ServerEndpoint, smtp: ServerEndpoint) {
        self.imap = imap
        self.smtp = smtp
    }
}

public struct OAuthEndpoints: Codable, Hashable, Sendable {
    public var authorizationEndpoint: String
    public var tokenEndpoint: String
    public var userInfoEndpoint: String?
    public var scopes: [String]
    public var redirectUriTemplate: String
    public var extraParams: [String: String]
    /// "idToken" oder "userInfo".
    public var emailSource: String
    public var appRegistrationUrl: String?
}

public struct Provider: Codable, Hashable, Identifiable, Sendable {
    public var id: String
    public var name: String
    public var color: String
    public var domains: [String]
    public var imap: ServerEndpoint
    public var smtp: ServerEndpoint
    public var authMethods: [AuthMethod]
    public var oauth: OAuthEndpoints?
    /// "gmail" = Kategorien kommen vom Server, "none" = lokal sortieren.
    public var serverCategories: String
    public var passwordLabel: String
    public var passwordHelp: String
    public var helpUrl: String
    public var allowInsecureLoopbackCertificate: Bool
    /// Manche Server legen per SMTP gesendete Mails nicht selbst in „Gesendet“ ab.
    public var saveSentViaImap: Bool?
    /// Server werden bei der Anmeldung eingegeben („Andere (IMAP)“).
    public var customServers: Bool?
    /// Endungen von MX-Hosts, an denen der Anbieter auch bei eigenen Domains erkannt wird.
    public var mxSuffixes: [String]?
    /// Anleitung für den Assistenten „Konto hinzufügen“.
    public var setup: SetupGuide?
    /// Webmail für Web-Konten (docs/FUNKTIONEN.md §20).
    public var web: WebConfig?

    public var usesGmailCategories: Bool { serverCategories == "gmail" }
    /// IMAP mit dem normalen Passwort ist der Standardweg (z. B. Hostinger) – Web-Anmeldung nur als Alternative.
    public var prefersIMAP: Bool { authMethods.first == .password }
    public var needsCustomServers: Bool { customServers == true }
}

/// „So geht's“-Schritte für den Assistenten (docs/FUNKTIONEN.md §17).
public struct SetupGuide: Codable, Hashable, Sendable {
    public struct Step: Codable, Hashable, Sendable {
        public var text: String
        public var url: String?
        public var button: String?
    }

    public var passwordSteps: [Step]
    public var oauthSteps: [Step]?
    /// Lokaler Port, der erreichbar sein muss (Proton Mail Bridge).
    public var checkLocalPort: Int?
}

/// Einstellungen für die automatische Anbieter-Erkennung.
public struct DiscoveryConfig: Codable, Hashable, Sendable {
    public var mxLookup: String
    public var autoconfig: [String]
    public var timeoutSeconds: Double
}

public struct FolderRoleHints: Codable, Hashable, Sendable {
    public var specialUse: [String]
    public var names: [String]
}

/// Lädt `shared/providers.json`.
public struct ProviderCatalog: Sendable {
    public let providers: [Provider]
    public let folderRoles: [FolderRole: FolderRoleHints]
    public let discovery: DiscoveryConfig?
    /// User-Agent je Plattform für eingebettete Browser (Web-Konten).
    public let webUserAgent: [String: String]

    private struct File: Decodable {
        var providers: [Provider]
        var folderRoles: [String: FolderRoleHints]
        var discovery: DiscoveryConfig?
        var webUserAgent: [String: String]?
    }

    public init(json: String = SharedResources.providersJSON) throws {
        let file = try JSONDecoder().decode(File.self, from: Data(json.utf8))
        providers = file.providers
        discovery = file.discovery
        webUserAgent = file.webUserAgent ?? [:]
        var roles: [FolderRole: FolderRoleHints] = [:]
        for (key, hints) in file.folderRoles {
            if let role = FolderRole(rawValue: key) { roles[role] = hints }
        }
        folderRoles = roles
    }

    /// Der eingebaute Katalog. Ein Fehler hier ist ein Programmierfehler (kaputtes providers.json).
    public static let shared: ProviderCatalog = {
        do { return try ProviderCatalog() } catch { fatalError("providers.json ist ungültig: \(error)") }
    }()

    public func provider(id: String) -> Provider? {
        providers.first { $0.id == id }
    }

    /// Findet den Anbieter zu einer Adresse anhand der Domain (Hostinger/Andere haben eigene Domains → nil).
    public func provider(forEmail email: String) -> Provider? {
        let domain = MailAddress(name: nil, address: email).domain
        return providers.first { $0.domains.contains(domain) }
    }

    /// Anbieter, dessen `mxSuffixes` zum MX-Host passt (z. B. „aspmx.l.google.com“ → Gmail).
    public func provider(forMXHost host: String) -> Provider? {
        var normalized = host.lowercased()
        if normalized.hasSuffix(".") { normalized.removeLast() }
        return providers.first { provider in
            (provider.mxSuffixes ?? []).contains { normalized == $0 || normalized.hasSuffix("." + $0) }
        }
    }
}
