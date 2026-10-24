import Foundation

/// Eine selbst angelegte Kategorie (z. B. „Rechnungen“).
public struct CustomCategory: Codable, Hashable, Identifiable, Sendable {
    public var id: String
    public var name: String
    public var color: String
    public var icon: String?

    public init(id: String, name: String, color: String, icon: String? = nil) {
        self.id = id
        self.name = name
        self.color = color
        self.icon = icon
    }

    public var category: MailCategory { MailCategory(rawValue: id) }

    /// Erzeugt eine eindeutige ID aus dem Namen („Rechnungen“ → „custom-rechnungen“).
    public static func makeID(name: String, existing: [CustomCategory]) -> String {
        let base = "custom-" + name.lowercased()
            .folding(options: .diacriticInsensitive, locale: Locale(identifier: "de"))
            .map { $0.isLetter || $0.isNumber ? String($0) : "-" }
            .joined()
            .split(separator: "-").joined(separator: "-")
        var id = base == "custom-" ? "custom-kategorie" : base
        var counter = 2
        while existing.contains(where: { $0.id == id }) {
            id = "\(base)-\(counter)"
            counter += 1
        }
        return id
    }
}

/// „Wenn Betreff enthält … → Kategorie“.
public struct ClassificationRule: Codable, Hashable, Identifiable, Sendable {
    public enum Field: String, Codable, CaseIterable, Sendable {
        case from, to, subject

        public var germanName: String {
            switch self {
            case .from: return "Absender"
            case .to: return "Empfänger"
            case .subject: return "Betreff"
            }
        }
    }

    public var field: Field
    public var contains: String
    public var category: MailCategory

    public init(field: Field, contains: String, category: MailCategory) {
        self.field = field
        self.contains = contains
        self.category = category
    }

    public var id: String { "\(field.rawValue)|\(contains)|\(category.rawValue)" }

    public func matches(from: MailAddress?, to: [MailAddress], subject: String) -> Bool {
        let needle = contains.trimmingCharacters(in: .whitespaces).lowercased()
        guard !needle.isEmpty else { return false }
        switch field {
        case .subject:
            return subject.lowercased().contains(needle)
        case .from:
            guard let from else { return false }
            return from.address.lowercased().contains(needle) || (from.name?.lowercased().contains(needle) ?? false)
        case .to:
            return to.contains { $0.address.lowercased().contains(needle) || ($0.name?.lowercased().contains(needle) ?? false) }
        }
    }
}

/// Inhalt von `rules.json` (Version 2, siehe docs/FUNKTIONEN.md §10).
public struct RuleSet: Codable, Equatable, Sendable {
    public var version = 2
    /// Absender-Adresse oder Domain (klein geschrieben) → Kategorie.
    public var senders: [String: MailCategory] = [:]
    public var rules: [ClassificationRule] = []
    public var customCategories: [CustomCategory] = []

    public init(senders: [String: MailCategory] = [:], rules: [ClassificationRule] = [], customCategories: [CustomCategory] = []) {
        self.senders = senders
        self.rules = rules
        self.customCategories = customCategories
    }

    enum CodingKeys: String, CodingKey { case version, senders, rules, customCategories }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        version = try container.decodeIfPresent(Int.self, forKey: .version) ?? 2
        senders = try container.decodeIfPresent([String: MailCategory].self, forKey: .senders) ?? [:]
        rules = try container.decodeIfPresent([ClassificationRule].self, forKey: .rules) ?? []
        customCategories = try container.decodeIfPresent([CustomCategory].self, forKey: .customCategories) ?? []
    }

    /// Liest Version 2 oder die alte flache Form `{ "<absender>": "<kategorie>" }`.
    public static func decode(_ data: Data) -> RuleSet {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return RuleSet() }
        if object["senders"] != nil || object["rules"] != nil || object["customCategories"] != nil {
            return (try? JSONDecoder().decode(RuleSet.self, from: data)) ?? RuleSet()
        }
        var senders: [String: MailCategory] = [:]
        for (key, value) in object {
            if let category = value as? String { senders[key.lowercased()] = MailCategory(rawValue: category) }
        }
        return RuleSet(senders: senders)
    }

    /// Kategorie aus Nutzer-Regeln: erst Absender, dann Regeln (erste passende gewinnt).
    public func category(from: MailAddress?, to: [MailAddress], subject: String) -> MailCategory? {
        if let from {
            if let match = senders[from.address.lowercased()] { return match }
            if !from.domain.isEmpty, let match = senders[from.domain] { return match }
        }
        return rules.first { $0.matches(from: from, to: to, subject: subject) }?.category
    }

    public func customCategory(_ category: MailCategory) -> CustomCategory? {
        customCategories.first { $0.id == category.rawValue }
    }
}
