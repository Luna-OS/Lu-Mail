import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
#if canImport(CryptoKit)
import CryptoKit
#endif

/// `shared/update.json`
public struct UpdateConfig: Decodable, Sendable {
    public var repository: String
    public var latestReleaseApi: String
    public var checkIntervalHours: Double
    public var initialDelaySeconds: Double
    public var assetSuffixes: [String: String]
    public var checksumsAsset: String

    public static let shared: UpdateConfig = {
        do {
            return try JSONDecoder().decode(UpdateConfig.self, from: Data(SharedResources.updateJSON.utf8))
        } catch {
            fatalError("update.json ist ungültig: \(error)")
        }
    }()
}

/// Version im Format MAJOR.MINOR.PATCH (führendes „v“ erlaubt).
public struct SemanticVersion: Comparable, Hashable, Sendable, CustomStringConvertible {
    public var major: Int
    public var minor: Int
    public var patch: Int

    public init(major: Int, minor: Int, patch: Int) {
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    public init?(_ text: String) {
        var trimmed = text.trimmingCharacters(in: .whitespaces)
        if trimmed.hasPrefix("v") || trimmed.hasPrefix("V") { trimmed.removeFirst() }
        let parts = trimmed.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3, let major = Int(parts[0]), let minor = Int(parts[1]), let patch = Int(parts[2]),
              major >= 0, minor >= 0, patch >= 0 else { return nil }
        self.init(major: major, minor: minor, patch: patch)
    }

    public static func < (lhs: SemanticVersion, rhs: SemanticVersion) -> Bool {
        (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
    }

    public var description: String { "\(major).\(minor).\(patch)" }

    /// Entwickler-Builds (0.0.0) suchen nicht automatisch nach Updates.
    public var isDevelopmentBuild: Bool { self == SemanticVersion(major: 0, minor: 0, patch: 0) }
}

/// Ein verfügbares Update.
public struct UpdateInfo: Sendable, Equatable {
    public var version: SemanticVersion
    public var title: String
    public var notes: String
    public var releasePage: URL?
    public var assetName: String
    public var downloadURL: URL
    public var size: Int
    public var sha256: String
}

public enum UpdateError: Error, LocalizedError, Equatable {
    case invalidResponse(String)
    case checksumMissing(String)
    case checksumMismatch
    case installFailed(String)

    public var errorDescription: String? {
        switch self {
        case .invalidResponse(let reason): return "Update-Prüfung fehlgeschlagen: \(reason)"
        case .checksumMissing(let asset): return "Für \(asset) gibt es keine Prüfsumme – Update wird aus Sicherheitsgründen nicht installiert."
        case .checksumMismatch: return "Das heruntergeladene Update ist beschädigt (Prüfsumme stimmt nicht) und wurde verworfen."
        case .installFailed(let reason): return "Update konnte nicht installiert werden: \(reason)"
        }
    }
}

/// Prüft GitHub-Releases, lädt Pakete und verifiziert sie (`docs/ARCHITEKTUR.md → Downloads & Auto-Updater`).
public struct UpdateService: Sendable {
    public typealias Fetch = @Sendable (URLRequest) async throws -> (Data, Int)

    public let config: UpdateConfig
    public let currentVersion: SemanticVersion
    /// Schlüssel aus `assetSuffixes`, z. B. „macos“.
    public let platform: String
    private let fetch: Fetch

    public init(config: UpdateConfig = .shared, currentVersion: SemanticVersion, platform: String,
                fetch: @escaping Fetch = UpdateService.urlSessionFetch) {
        self.config = config
        self.currentVersion = currentVersion
        self.platform = platform
        self.fetch = fetch
    }

    public static let urlSessionFetch: Fetch = { request in
        let (data, response) = try await URLSession.shared.data(for: request)
        return (data, (response as? HTTPURLResponse)?.statusCode ?? 0)
    }

    private func request(_ url: URL, accept: String) -> URLRequest {
        var request = URLRequest(url: url)
        request.setValue(accept, forHTTPHeaderField: "Accept")
        request.setValue("Lu-Mail/\(currentVersion)", forHTTPHeaderField: "User-Agent")
        request.timeoutInterval = 30
        return request
    }

    /// Liefert ein neueres Update oder `nil`, wenn Lu-Mail aktuell ist.
    public func check() async throws -> UpdateInfo? {
        guard let url = URL(string: config.latestReleaseApi) else { throw UpdateError.invalidResponse("ungültige Adresse") }
        let (data, status) = try await fetch(request(url, accept: "application/vnd.github+json"))
        if status == 404 { return nil }  // noch kein Release veröffentlicht
        guard status == 200 else { throw UpdateError.invalidResponse("HTTP \(status)") }
        guard let release = try? JSONDecoder().decode(GitHubRelease.self, from: data) else {
            throw UpdateError.invalidResponse("unerwartete Antwort")
        }
        guard let version = SemanticVersion(release.tag_name), version > currentVersion else { return nil }
        guard let suffix = config.assetSuffixes[platform],
              let asset = release.assets.first(where: { $0.name.hasSuffix(suffix) }),
              let downloadURL = URL(string: asset.browser_download_url) else {
            return nil  // Release ohne Paket für diese Plattform
        }
        guard let checksums = release.assets.first(where: { $0.name == config.checksumsAsset }),
              let checksumsURL = URL(string: checksums.browser_download_url) else {
            throw UpdateError.checksumMissing(asset.name)
        }
        let (sumsData, sumsStatus) = try await fetch(request(checksumsURL, accept: "application/octet-stream"))
        guard sumsStatus == 200, let hash = Self.parseChecksums(String(decoding: sumsData, as: UTF8.self))[asset.name] else {
            throw UpdateError.checksumMissing(asset.name)
        }
        return UpdateInfo(version: version, title: release.name ?? "Lu-Mail \(version)", notes: release.body ?? "",
                          releasePage: release.html_url.flatMap(URL.init(string:)), assetName: asset.name,
                          downloadURL: downloadURL, size: asset.size ?? 0, sha256: hash)
    }

    /// `sha256sum`-Format: `<hex>  <Dateiname>` (auch `<hex> *<Dateiname>`).
    public static func parseChecksums(_ text: String) -> [String: String] {
        var result: [String: String] = [:]
        for line in text.split(whereSeparator: \.isNewline) {
            let parts = line.split(maxSplits: 1, whereSeparator: \.isWhitespace)
            guard parts.count == 2 else { continue }
            let hash = parts[0].lowercased()
            guard hash.count == 64, hash.allSatisfy(\.isHexDigit) else { continue }
            var name = parts[1].trimmingCharacters(in: .whitespaces)
            if name.hasPrefix("*") { name.removeFirst() }
            result[name] = hash
        }
        return result
    }

    public static func sha256Hex(of data: Data) -> String {
        #if canImport(CryptoKit)
        return CryptoKit.SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
        #else
        return SHA256.hash(data).map { String(format: "%02x", $0) }.joined()
        #endif
    }

    /// Lädt das Paket in `directory` und prüft die Prüfsumme. Bei Abweichung wird die Datei gelöscht.
    public func download(_ update: UpdateInfo, to directory: URL) async throws -> URL {
        let (data, status) = try await fetch(request(update.downloadURL, accept: "application/octet-stream"))
        guard status == 200 else { throw UpdateError.invalidResponse("Download: HTTP \(status)") }
        guard Self.sha256Hex(of: data) == update.sha256.lowercased() else { throw UpdateError.checksumMismatch }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let file = directory.appendingPathComponent(update.assetName)
        try? FileManager.default.removeItem(at: file)
        try data.write(to: file, options: .atomic)
        return file
    }
}

struct GitHubRelease: Decodable {
    struct Asset: Decodable {
        var name: String
        var browser_download_url: String
        var size: Int?
    }

    var tag_name: String
    var name: String?
    var body: String?
    var html_url: String?
    var assets: [Asset]
}
