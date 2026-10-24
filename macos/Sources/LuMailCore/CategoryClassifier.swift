import Foundation

/// Sortiert Nachrichten in Posteingang / Werbung / Soziale Netzwerke / Updates / Foren.
/// Der Algorithmus ist in `shared/categories.json → algorithm` beschrieben und in der Windows-App identisch.
public struct CategoryClassifier: Sendable {
    public struct CategoryInfo: Decodable, Sendable, Hashable {
        public var id: String
        public var name: String
        public var icon: String
        public var gmailQuery: String

        public var category: MailCategory? { MailCategory(rawValue: id) }
    }

    struct Rules: Decodable, Sendable {
        struct Social: Decodable, Sendable { var senderDomains: [String] }
        struct Forums: Decodable, Sendable { var senderDomains: [String]; var headers: [String] }
        struct Updates: Decodable, Sendable { var subjectKeywords: [String]; var senderLocalParts: [String] }
        struct Promotions: Decodable, Sendable {
            var headers: [String]
            var precedence: [String]
            var senderLocalParts: [String]
            var subjectKeywords: [String]
        }
        var social: Social
        var forums: Forums
        var updates: Updates
        var promotions: Promotions
    }

    private struct File: Decodable {
        var categories: [CategoryInfo]
        var rules: Rules
    }

    public let categories: [CategoryInfo]
    let rules: Rules

    public init(json: String = SharedResources.categoriesJSON) throws {
        let file = try JSONDecoder().decode(File.self, from: Data(json.utf8))
        categories = file.categories
        rules = file.rules
    }

    public static let shared: CategoryClassifier = {
        do { return try CategoryClassifier() } catch { fatalError("categories.json ist ungültig: \(error)") }
    }()

    public func info(for category: MailCategory) -> CategoryInfo? {
        categories.first { $0.id == category.rawValue }
    }

    /// - Parameters:
    ///   - from: Absender
    ///   - subject: Betreff
    ///   - headers: Header-Namen → Werte; Namen werden ohne Groß-/Kleinschreibung verglichen.
    ///   - serverCategory: Gmail-Kategorie vom Server, falls vorhanden.
    ///   - overrides: Nutzer-Regeln (Absender-Adresse oder Domain, klein geschrieben → Kategorie).
    public func classify(from: MailAddress?, subject: String, headers: [String: String],
                         serverCategory: MailCategory? = nil, overrides: [String: MailCategory] = [:]) -> MailCategory {
        let address = from?.address.lowercased() ?? ""
        let domain = from?.domain ?? ""
        let localPart = from?.localPart ?? ""
        let lowerSubject = subject.lowercased()
        var lowerHeaders: [String: String] = [:]
        for (key, value) in headers { lowerHeaders[key.lowercased()] = value }

        // 1. Nutzer-Regel
        if let rule = overrides[address] ?? (domain.isEmpty ? nil : overrides[domain]) {
            return rule
        }
        // 2. Gmail-Server-Kategorie
        if let serverCategory {
            return serverCategory
        }
        // 3. Soziale Netzwerke
        if Self.domain(domain, matchesAnyOf: rules.social.senderDomains) {
            return .social
        }
        // 4. Foren
        if Self.domain(domain, matchesAnyOf: rules.forums.senderDomains)
            || rules.forums.headers.contains(where: { lowerHeaders[$0.lowercased()] != nil }) {
            return .forums
        }
        // 5. Updates über Betreff
        if Self.text(lowerSubject, containsAnyOf: rules.updates.subjectKeywords) {
            return .updates
        }
        // 6. Werbung
        let precedence = lowerHeaders["precedence"]?.trimmingCharacters(in: .whitespaces).lowercased()
        if rules.promotions.headers.contains(where: { lowerHeaders[$0.lowercased()] != nil })
            || (precedence.map { rules.promotions.precedence.contains($0) } ?? false)
            || rules.promotions.senderLocalParts.contains(localPart)
            || Self.text(lowerSubject, containsAnyOf: rules.promotions.subjectKeywords) {
            return .promotions
        }
        // 7. Updates über Absender
        if rules.updates.senderLocalParts.contains(localPart) {
            return .updates
        }
        // 8. Rest
        return .primary
    }

    static func domain(_ domain: String, matchesAnyOf list: [String]) -> Bool {
        guard !domain.isEmpty else { return false }
        return list.contains { domain == $0 || domain.hasSuffix("." + $0) }
    }

    static func text(_ text: String, containsAnyOf keywords: [String]) -> Bool {
        keywords.contains { text.contains($0.lowercased()) }
    }
}
