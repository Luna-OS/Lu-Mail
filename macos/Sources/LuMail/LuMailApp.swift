import AppKit
import SwiftUI

@main
struct LuMailApp: App {
    @State private var model = AppModel()

    init() {
        // Auch beim Start per `swift run` (ohne .app-Bundle) als normale App mit Dock-Symbol auftreten.
        NSApplication.shared.setActivationPolicy(.regular)
        DispatchQueue.main.async { NSApplication.shared.activate(ignoringOtherApps: true) }
    }

    var body: some Scene {
        WindowGroup("Lu-Mail") {
            ContentView()
                .environment(model)
                .frame(minWidth: 980, minHeight: 600)
                .tint(LuTheme.accent)
        }
        .commands {
            CommandGroup(after: .newItem) {
                Button("Neue E-Mail") { model.newMessage() }
                    .keyboardShortcut("n", modifiers: .command)
                Button("Konto hinzufügen …") { model.showAddAccount = true }
                    .keyboardShortcut("n", modifiers: [.command, .shift])
            }
            CommandMenu("Postfach") {
                Button("Alle Postfächer aktualisieren") { Task { await model.refreshAll() } }
                    .keyboardShortcut("r", modifiers: .command)
            }
        }

        Settings {
            SettingsView()
                .environment(model)
                .tint(LuTheme.accent)
        }
    }
}
