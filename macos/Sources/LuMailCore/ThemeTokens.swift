import Foundation

/// Die Design-Tokens aus `shared/theme/lu-theme.json`.
public struct ThemeTokens: Decodable, Sendable {
    public struct Palette: Decodable, Sendable {
        public var background: String
        public var sidebar: String
        public var surface: String
        public var surfaceHover: String
        public var surfaceSelected: String
        public var border: String
        public var textPrimary: String
        public var textSecondary: String
        public var textMuted: String
        public var accent: String
        public var accentHover: String
        public var accentText: String
        public var moon: String
        public var unreadDot: String
        public var danger: String
        public var success: String
    }

    public struct Colors: Decodable, Sendable {
        public var dark: Palette
        public var light: Palette
    }

    public struct Radius: Decodable, Sendable {
        public var small: Double
        public var medium: Double
        public var large: Double
    }

    public struct Variant: Decodable, Sendable {
        public var name: String
        public var description: String
        public var colors: Colors
    }

    public struct Accent: Decodable, Sendable {
        public var name: String
        public var dark: String
        public var light: String
    }

    public var name: String
    public var colors: Colors
    public var variants: [String: Variant]
    public var accents: [String: Accent]
    public var providers: [String: String]
    public var categories: [String: String]
    public var radius: Radius

    /// Reihenfolge für die Einstellungen.
    public static let variantOrder = ["luna", "neumond", "vollmond"]
    public static let accentOrder = ["lila", "blau", "tuerkis", "gruen", "rosa", "gold"]

    public static let shared: ThemeTokens = {
        do {
            return try JSONDecoder().decode(ThemeTokens.self, from: Data(SharedResources.themeJSON.utf8))
        } catch {
            fatalError("lu-theme.json ist ungültig: \(error)")
        }
    }()
}

/// Wandelt "#RRGGBB" in RGB-Anteile (0…1) um.
public func parseHexColor(_ hex: String) -> (red: Double, green: Double, blue: Double)? {
    var text = hex.trimmingCharacters(in: .whitespaces)
    if text.hasPrefix("#") { text.removeFirst() }
    guard text.count == 6, let value = UInt32(text, radix: 16) else { return nil }
    return (Double((value >> 16) & 0xFF) / 255, Double((value >> 8) & 0xFF) / 255, Double(value & 0xFF) / 255)
}
