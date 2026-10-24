import AppKit
import LuMailCore
import SwiftUI

/// Das Lu-Mail-Theme – Farben aus `shared/theme/lu-theme.json`, Variante und Akzent aus den Einstellungen,
/// automatisch hell/dunkel.
@MainActor
enum LuTheme {
    private static let tokens = ThemeTokens.shared

    static var background: Color { adaptive(\.background) }
    static var sidebar: Color { adaptive(\.sidebar) }
    static var surface: Color { adaptive(\.surface) }
    static var surfaceHover: Color { adaptive(\.surfaceHover) }
    static var surfaceSelected: Color { adaptive(\.surfaceSelected) }
    static var border: Color { adaptive(\.border) }
    static var textPrimary: Color { adaptive(\.textPrimary) }
    static var textSecondary: Color { adaptive(\.textSecondary) }
    static var textMuted: Color { adaptive(\.textMuted) }
    static var accent: Color { accentColor() }
    static var accentText: Color { adaptive(\.accentText) }
    static var moon: Color { adaptive(\.moon) }
    static var unreadDot: Color { accentColor() }
    static var danger: Color { adaptive(\.danger) }
    static var success: Color { adaptive(\.success) }

    static let radiusSmall = CGFloat(ThemeTokens.shared.radius.small)
    static let radiusMedium = CGFloat(ThemeTokens.shared.radius.medium)
    static let radiusLarge = CGFloat(ThemeTokens.shared.radius.large)

    static func provider(_ id: String) -> Color {
        Color(hex: tokens.providers[id] ?? "#8B7CFF")
    }

    static func category(_ category: MailCategory, rules: RuleSet? = nil) -> Color {
        if let custom = rules?.customCategory(category) { return Color(hex: custom.color) }
        return Color(hex: tokens.categories[category.rawValue] ?? "#8B7CFF")
    }

    private static var palette: ThemeTokens.Colors {
        tokens.variants[ThemeSettings.shared.variant]?.colors ?? tokens.colors
    }

    private static func adaptive(_ keyPath: KeyPath<ThemeTokens.Palette, String>) -> Color {
        let colors = palette
        return dynamic(light: colors.light[keyPath: keyPath], dark: colors.dark[keyPath: keyPath])
    }

    private static func accentColor() -> Color {
        guard let accent = tokens.accents[ThemeSettings.shared.accent] else { return adaptive(\.accent) }
        return dynamic(light: accent.light, dark: accent.dark)
    }

    private static func dynamic(light: String, dark: String) -> Color {
        let lightColor = NSColor(hex: light)
        let darkColor = NSColor(hex: dark)
        return Color(nsColor: NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? darkColor : lightColor
        })
    }
}

extension NSColor {
    convenience init(hex: String) {
        let rgb = parseHexColor(hex) ?? (0.55, 0.49, 1.0)
        self.init(srgbRed: rgb.red, green: rgb.green, blue: rgb.blue, alpha: 1)
    }
}

extension Color {
    init(hex: String) {
        self.init(nsColor: NSColor(hex: hex))
    }
}

extension MailCategory {
    /// Name; eigene Kategorien brauchen das Regelwerk.
    func title(_ rules: RuleSet?) -> String {
        if let custom = rules?.customCategory(self) { return custom.name }
        return CategoryClassifier.shared.info(for: self)?.name ?? rawValue
    }

    var title: String { title(nil) }

    func systemImage(_ rules: RuleSet?) -> String {
        if let custom = rules?.customCategory(self) { return custom.icon ?? "folder" }
        return CategoryClassifier.shared.info(for: self)?.icon ?? "tray"
    }

    var systemImage: String { systemImage(nil) }
}

extension FolderRole {
    var systemImage: String {
        switch self {
        case .inbox: return "tray"
        case .sent: return "paperplane"
        case .drafts: return "doc"
        case .spam: return "xmark.bin"
        case .trash: return "trash"
        case .archive: return "archivebox"
        }
    }
}

/// Der kleine Mond aus dem Logo.
struct MoonMark: View {
    var size: CGFloat = 18

    var body: some View {
        ZStack {
            Circle().fill(LuTheme.accent)
            Circle()
                .fill(LuTheme.sidebar)
                .frame(width: size * 0.78, height: size * 0.78)
                .offset(x: size * 0.28, y: -size * 0.16)
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
        .accessibilityHidden(true)
    }
}

/// Farbiger Punkt eines Anbieters.
struct ProviderDot: View {
    let providerId: String
    var size: CGFloat = 9

    var body: some View {
        Circle().fill(LuTheme.provider(providerId)).frame(width: size, height: size)
    }
}

/// Zähler für ungelesene Nachrichten.
struct UnreadBadge: View {
    let count: Int

    var body: some View {
        if count > 0 {
            Text(count > 999 ? "999+" : "\(count)")
                .font(.caption2.weight(.semibold))
                .monospacedDigit()
                .padding(.horizontal, 6)
                .padding(.vertical, 1)
                .background(Capsule().fill(LuTheme.accent.opacity(0.18)))
                .foregroundStyle(LuTheme.accent)
        }
    }
}

struct CategoryChip: View {
    let category: MailCategory
    var rules: RuleSet?

    var body: some View {
        Text(category.title(rules))
            .font(.caption2.weight(.medium))
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(Capsule().fill(LuTheme.category(category, rules: rules).opacity(0.16)))
            .foregroundStyle(LuTheme.category(category, rules: rules))
    }
}
