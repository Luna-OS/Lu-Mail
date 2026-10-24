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

/// Nutzer-Regeln (`rules.json`, Version 2 – alte flache Dateien werden weiter gelesen).
public struct RuleStore: Sendable {
    public let paths: AppPaths

    public init(paths: AppPaths) {
        self.paths = paths
    }

    public func load() -> RuleSet {
        guard let data = try? Data(contentsOf: paths.rulesFile) else { return RuleSet() }
        return RuleSet.decode(data)
    }

    public func save(_ rules: RuleSet) throws {
        try paths.ensureExists()
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(rules).write(to: paths.rulesFile, options: .atomic)
    }
}
