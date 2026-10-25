import AppKit
import LocalAuthentication
import LuMailCore
import Observation
import UserNotifications

/// Systemmitteilungen für neue Mails (docs/FUNKTIONEN.md §2).
@MainActor
final class NotificationService: NSObject, UNUserNotificationCenterDelegate {
    static let shared = NotificationService()

    /// Wird aufgerufen, wenn eine Mitteilung angeklickt wird (Konto-ID, Ordner, UID).
    var onOpen: ((UUID, String, UInt32) -> Void)?
    /// Mitteilung eines Web-Kontos angeklickt (Konto-ID).
    var onOpenWeb: ((UUID) -> Void)?

    /// UNUserNotificationCenter stürzt ohne Bundle-ID ab (z. B. bei `swift run`).
    private var isAvailable: Bool { Bundle.main.bundleIdentifier != nil && Bundle.main.bundleURL.pathExtension == "app" }

    func requestAuthorization() {
        guard isAvailable else { return }
        let center = UNUserNotificationCenter.current()
        center.delegate = self
        center.requestAuthorization(options: [.alert, .sound, .badge]) { _, _ in }
    }

    func notify(_ messages: [MessageSummary], accountEmail: (UUID) -> String) {
        guard isAvailable, !messages.isEmpty else { return }
        let center = UNUserNotificationCenter.current()
        for message in messages.prefix(5) {
            let content = UNMutableNotificationContent()
            content.title = message.from?.displayName ?? "Neue E-Mail"
            content.subtitle = accountEmail(message.accountId)
            content.body = message.subject.isEmpty ? "(kein Betreff)" : message.subject
            content.sound = .default
            content.userInfo = ["account": message.accountId.uuidString, "folder": message.folderPath, "uid": Int(message.uid)]
            center.add(UNNotificationRequest(identifier: message.id, content: content, trigger: nil))
        }
        if messages.count > 5 {
            let content = UNMutableNotificationContent()
            content.title = "Lu-Mail"
            content.body = "\(messages.count - 5) weitere neue Nachrichten"
            center.add(UNNotificationRequest(identifier: "lumail-more-\(UUID().uuidString)", content: content, trigger: nil))
        }
    }

    /// Neue Mails in einem Web-Konto (§20) – der Inhalt ist nur im Webmail sichtbar.
    func notifyWeb(accountId: UUID, title: String, subtitle: String, body: String) {
        guard isAvailable else { return }
        let content = UNMutableNotificationContent()
        content.title = title
        content.subtitle = subtitle
        content.body = body
        content.sound = .default
        content.userInfo = ["account": accountId.uuidString, "web": true]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "lumail-web-\(UUID().uuidString)", content: content, trigger: nil))
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                            withCompletionHandler completionHandler: @escaping () -> Void) {
        let info = response.notification.request.content.userInfo
        let account = (info["account"] as? String).flatMap(UUID.init(uuidString:))
        let folder = info["folder"] as? String
        let uid = (info["uid"] as? Int).map(UInt32.init)
        let isWeb = info["web"] as? Bool ?? false
        Task { @MainActor in
            NSApplication.shared.activate(ignoringOtherApps: true)
            if isWeb, let account { self.onOpenWeb?(account) }
            if let account, let folder, let uid { self.onOpen?(account, folder, uid) }
            completionHandler()
        }
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                            withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }

    /// Zahl auf dem Dock-Symbol.
    func setBadge(_ count: Int) {
        NSApplication.shared.dockTile.badgeLabel = count > 0 ? (count > 999 ? "999+" : "\(count)") : nil
    }
}

/// Sperre mit Touch ID bzw. Mac-Passwort (§14).
@MainActor
@Observable
final class AppLock {
    static let shared = AppLock()

    var isEnabled: Bool = UserDefaults.standard.bool(forKey: "appLockEnabled")
    /// Minuten im Hintergrund bis zur Sperre.
    var lockAfterMinutes: Int = UserDefaults.standard.object(forKey: "appLockMinutes") as? Int ?? 5
    private(set) var isLocked = false
    private(set) var lastError: String?
    private var backgroundSince: Date?
    private var observers: [NSObjectProtocol] = []

    private init() {
        isLocked = isEnabled
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: NSApplication.didResignActiveNotification, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { AppLock.shared.backgroundSince = Date() }
        })
        observers.append(center.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { AppLock.shared.didBecomeActive() }
        })
    }

    var biometryName: String {
        let context = LAContext()
        _ = context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: nil)
        return context.biometryType == .touchID ? "Touch ID" : "Mac-Passwort"
    }

    func save() {
        UserDefaults.standard.set(isEnabled, forKey: "appLockEnabled")
        UserDefaults.standard.set(lockAfterMinutes, forKey: "appLockMinutes")
        if !isEnabled { isLocked = false }
    }

    private func didBecomeActive() {
        guard isEnabled, let since = backgroundSince else { return }
        if Date().timeIntervalSince(since) >= Double(lockAfterMinutes * 60) { isLocked = true }
        backgroundSince = nil
    }

    func unlock() {
        let context = LAContext()
        context.localizedCancelTitle = "Abbrechen"
        context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: "Lu-Mail entsperren") { success, error in
            Task { @MainActor in
                if success {
                    self.isLocked = false
                    self.lastError = nil
                } else {
                    self.lastError = error?.localizedDescription
                }
            }
        }
    }
}

/// Darstellung: Modus, Variante (Luna/Neumond/Vollmond) und Akzentfarbe (§16).
@MainActor
@Observable
final class ThemeSettings {
    static let shared = ThemeSettings()

    enum Mode: String, CaseIterable, Identifiable {
        case system, light, dark
        var id: String { rawValue }
        var title: String {
            switch self {
            case .system: return "Wie System"
            case .light: return "Hell"
            case .dark: return "Dunkel"
            }
        }
    }

    private(set) var mode: Mode = Mode(rawValue: UserDefaults.standard.string(forKey: "themeMode") ?? "") ?? .system
    private(set) var variant: String = UserDefaults.standard.string(forKey: "themeVariant") ?? "luna"
    private(set) var accent: String = UserDefaults.standard.string(forKey: "themeAccent") ?? "lila"
    /// Animierter Nachthimmel als Hintergrund (§22).
    private(set) var nightSky: Bool = UserDefaults.standard.object(forKey: "nightSky") as? Bool ?? true
    /// Zählt jede Änderung hoch – die Oberfläche baut sich damit neu auf.
    private(set) var revision = 0

    func set(mode: Mode? = nil, variant: String? = nil, accent: String? = nil, nightSky: Bool? = nil) {
        if let mode { self.mode = mode }
        if let variant { self.variant = variant }
        if let accent { self.accent = accent }
        if let nightSky { self.nightSky = nightSky }
        UserDefaults.standard.set(self.nightSky, forKey: "nightSky")
        UserDefaults.standard.set(self.mode.rawValue, forKey: "themeMode")
        UserDefaults.standard.set(self.variant, forKey: "themeVariant")
        UserDefaults.standard.set(self.accent, forKey: "themeAccent")
        revision += 1
        switch self.mode {
        case .system: NSApplication.shared.appearance = nil
        case .light: NSApplication.shared.appearance = NSAppearance(named: .aqua)
        case .dark: NSApplication.shared.appearance = NSAppearance(named: .darkAqua)
        }
    }

    /// Beim Start die gespeicherte Darstellung anwenden.
    func apply() {
        set()
    }
}
