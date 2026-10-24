import AppKit
import LuMailCore
import SwiftUI

@main
struct LuMailApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @State private var model = AppModel()

    init() {
        // Auch beim Start per `swift run` (ohne .app-Bundle) als normale App mit Dock-Symbol auftreten.
        NSApplication.shared.setActivationPolicy(.regular)
        DispatchQueue.main.async {
            ThemeSettings.shared.apply()
            NSApplication.shared.activate(ignoringOtherApps: true)
        }
    }

    var body: some Scene {
        WindowGroup("Lu-Mail", id: "main") {
            ContentView()
                .environment(model)
                .frame(minWidth: 1000, minHeight: 620)
                .tint(LuTheme.accent)
        }
        .commands {
            CommandGroup(after: .newItem) {
                Button("Neue E-Mail") { model.newMessage() }
                    .keyboardShortcut("n", modifiers: .command)
                Button("Konto hinzufügen …") { model.showAddAccount = true }
                    .keyboardShortcut("n", modifiers: [.command, .shift])
            }
            CommandGroup(after: .appInfo) {
                Button("Nach Updates suchen …") { Task { await UpdateManager.shared.check(userInitiated: true) } }
            }
            CommandMenu("Postfach") {
                Button("Alle Postfächer aktualisieren") { Task { await model.refreshAll() } }
                    .keyboardShortcut("r", modifiers: .command)
                Button("Auf dem Server suchen") { Task { await model.searchOnServer() } }
                    .keyboardShortcut("f", modifiers: [.command, .shift])
                Divider()
                Toggle("Als Konversationen anzeigen", isOn: Binding(get: { model.conversationMode },
                                                                     set: { model.conversationMode = $0; model.saveSettings() }))
            }
        }

        Settings {
            SettingsView()
                .environment(model)
                .tint(LuTheme.accent)
        }

        MenuBarExtra {
            MenuBarContent().environment(model)
        } label: {
            let count = model.totalUnread
            Image(systemName: count > 0 ? "moon.stars.fill" : "moon")
            if count > 0 { Text("\(count)") }
        }
    }
}

/// Menüleisten-Menü: die neuesten ungelesenen Mails.
struct MenuBarContent: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        let latest = model.latestUnread
        if latest.isEmpty {
            Text("Keine ungelesenen Mails 🌙")
        } else {
            Text("\(model.totalUnread) ungelesen")
            Divider()
            ForEach(latest) { message in
                Button("\(message.from?.displayName ?? "?") – \(message.subject.isEmpty ? "(kein Betreff)" : message.subject)") {
                    show()
                    model.reveal(accountId: message.accountId, folder: message.folderPath, uid: message.uid)
                }
            }
        }
        Divider()
        Button("Neue E-Mail") {
            show()
            model.newMessage()
        }
        Button("Lu-Mail öffnen") { show() }
        Button("Aktualisieren") { Task { await model.refreshAll() } }
        Divider()
        Button("Beenden") { NSApplication.shared.terminate(nil) }
    }

    private func show() {
        NSApplication.shared.activate(ignoringOtherApps: true)
        if !NSApplication.shared.windows.contains(where: { $0.isVisible && $0.canBecomeMain }) {
            openWindow(id: "main")
        }
    }
}
