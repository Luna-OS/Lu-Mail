import Foundation

/// Der Datenordner der App (`~/Library/Application Support/LuMail`).
public struct AppPaths: Sendable {
    public let baseDirectory: URL

    public init(baseDirectory: URL? = nil) {
        if let baseDirectory {
            self.baseDirectory = baseDirectory
        } else {
            let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
                ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent(".lumail")
            self.baseDirectory = support.appendingPathComponent("LuMail", isDirectory: true)
        }
    }

    public var accountsFile: URL { baseDirectory.appendingPathComponent("accounts.json") }
    public var rulesFile: URL { baseDirectory.appendingPathComponent("rules.json") }
    public var oauthFile: URL { baseDirectory.appendingPathComponent("oauth.json") }

    public func ensureExists() throws {
        try FileManager.default.createDirectory(at: baseDirectory, withIntermediateDirectories: true)
    }
}

/// Speichert die Kontenliste (ohne Passwörter/Tokens).
public struct AccountStore: Sendable {
    public let paths: AppPaths

    public init(paths: AppPaths) {
        self.paths = paths
    }

    public func load() -> [Account] {
        guard let data = try? Data(contentsOf: paths.accountsFile) else { return [] }
        return (try? JSONDecoder().decode([Account].self, from: data)) ?? []
    }

    public func save(_ accounts: [Account]) throws {
        try paths.ensureExists()
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(accounts).write(to: paths.accountsFile, options: .atomic)
    }
}

/// Nutzer-Regeln „Absender immer als … einsortieren“ (Adresse oder Domain → Kategorie).
public struct SenderRuleStore: Sendable {
    public let paths: AppPaths

    public init(paths: AppPaths) {
        self.paths = paths
    }

    public func load() -> [String: MailCategory] {
        guard let data = try? Data(contentsOf: paths.rulesFile),
              let raw = try? JSONDecoder().decode([String: String].self, from: data) else { return [:] }
        return raw.compactMapValues(MailCategory.init(rawValue:))
    }

    public func save(_ rules: [String: MailCategory]) throws {
        try paths.ensureExists()
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(rules.mapValues(\.rawValue)).write(to: paths.rulesFile, options: .atomic)
    }
}
