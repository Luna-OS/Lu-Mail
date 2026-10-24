import LuMailCore
import SwiftUI

/// Hinweisleiste oben im Fenster, sobald ein Update verfügbar ist oder bereitliegt.
struct UpdateBar: View {
    @State private var updates = UpdateManager.shared

    var body: some View {
        switch updates.state {
        case .available(let update):
            bar(icon: "arrow.down.circle.fill", text: "Lu-Mail \(update.version) ist verfügbar.", update: update) {
                Button("Herunterladen und neu starten") {
                    Task {
                        await updates.download(update)
                        updates.installAndRelaunch()
                    }
                }
                .buttonStyle(.borderedProminent)
            }
        case .downloading(let update):
            bar(icon: "arrow.down.circle", text: "Lu-Mail \(update.version) wird geladen …", update: update) {
                ProgressView().controlSize(.small)
            }
        case .ready(let update):
            bar(icon: "sparkles", text: "Lu-Mail \(update.version) ist bereit – wird beim Beenden installiert.", update: update) {
                Button("Jetzt neu starten") { updates.installAndRelaunch() }
                    .buttonStyle(.borderedProminent)
            }
        default:
            EmptyView()
        }
    }

    private func bar<Actions: View>(icon: String, text: String, update: UpdateInfo, @ViewBuilder actions: () -> Actions) -> some View {
        HStack(spacing: 10) {
            Image(systemName: icon).foregroundStyle(LuTheme.moon)
            Text(text).font(.callout.weight(.medium))
            if let page = update.releasePage {
                Link("Was ist neu?", destination: page).font(.callout)
            }
            Spacer()
            actions()
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .background(LuTheme.accent.opacity(0.12))
        .overlay(alignment: .bottom) { Divider().overlay(LuTheme.border) }
    }
}

/// Einstellungen → Updates.
struct UpdateSettingsView: View {
    @State private var updates = UpdateManager.shared

    var body: some View {
        Form {
            Section("Version") {
                LabeledContent("Installiert", value: updates.isAppBundle ? updates.currentVersion.description : "Entwickler-Build")
                if let lastCheck = updates.lastCheck {
                    LabeledContent("Zuletzt geprüft", value: lastCheck.formatted(date: .abbreviated, time: .shortened))
                }
            }
            Section("Updates") {
                Picker("Neue Versionen", selection: Binding(get: { updates.mode }, set: { updates.setMode($0) })) {
                    ForEach(UpdateManager.Mode.allCases) { mode in
                        Text(mode.title).tag(mode)
                    }
                }
                HStack {
                    Button("Jetzt nach Updates suchen") {
                        Task { await updates.check(userInitiated: true) }
                    }
                    .disabled(updates.state == .checking)
                    Spacer()
                    statusText
                }
                Text("Updates kommen direkt aus den offiziellen GitHub-Releases von Lu-Mail und werden vor der Installation per SHA-256-Prüfsumme kontrolliert.")
                    .font(.caption)
                    .foregroundStyle(LuTheme.textSecondary)
            }
        }
        .formStyle(.grouped)
    }

    @ViewBuilder
    private var statusText: some View {
        switch updates.state {
        case .checking: ProgressView().controlSize(.small)
        case .upToDate: Text("Lu-Mail ist aktuell.").foregroundStyle(LuTheme.success)
        case .available(let update): Text("Version \(update.version) verfügbar").foregroundStyle(LuTheme.accent)
        case .downloading: Text("Wird geladen …").foregroundStyle(LuTheme.textSecondary)
        case .ready(let update): Text("Version \(update.version) bereit").foregroundStyle(LuTheme.accent)
        case .failed(let message): Text(message).foregroundStyle(LuTheme.danger).lineLimit(3)
        case .idle: EmptyView()
        }
    }
}
