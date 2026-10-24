import AppKit
import LuMailCore
import SwiftUI

/// Das Theme „Luna“ – Farben kommen aus `shared/theme/lu-theme.json` und passen sich Hell/Dunkel an.
enum LuTheme {
    private static let tokens = ThemeTokens.shared

    static let background = adaptive(\.background)
    static let sidebar = adaptive(\.sidebar)
    static let surface = adaptive(\.surface)
    static let surfaceHover = adaptive(\.surfaceHover)
    static let surfaceSelected = adaptive(\.surfaceSelected)
    static let border = adaptive(\.border)
    static let textPrimary = adaptive(\.textPrimary)
    static let textSecondary = adaptive(\.textSecondary)
    static let textMuted = adaptive(\.textMuted)
    static let accent = adaptive(\.accent)
    static let accentText = adaptive(\.accentText)
    static let moon = adaptive(\.moon)
    static let unreadDot = adaptive(\.unreadDot)
    static let danger = adaptive(\.danger)
    static let success = adaptive(\.success)

    static let radiusSmall = CGFloat(tokens.radius.small)
    static let radiusMedium = CGFloat(tokens.radius.medium)
    static let radiusLarge = CGFloat(tokens.radius.large)

    static func provider(_ id: String) -> Color {
        Color(hex: tokens.providers[id] ?? "#8B7CFF")
    }

    static func category(_ category: MailCategory) -> Color {
        Color(hex: tokens.categories[category.rawValue] ?? "#8B7CFF")
    }

    private static func adaptive(_ keyPath: KeyPath<ThemeTokens.Palette, String>) -> Color {
        let light = NSColor(hex: tokens.colors.light[keyPath: keyPath])
        let dark = NSColor(hex: tokens.colors.dark[keyPath: keyPath])
        return Color(nsColor: NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light
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
    var title: String { CategoryClassifier.shared.info(for: self)?.name ?? rawValue }
    var systemImage: String { CategoryClassifier.shared.info(for: self)?.icon ?? "tray" }
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

    var body: some View {
        Text(category.title)
            .font(.caption2.weight(.medium))
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(Capsule().fill(LuTheme.category(category).opacity(0.16)))
            .foregroundStyle(LuTheme.category(category))
    }
}
