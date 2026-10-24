import LuMailCore
import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var model = model
        TabView {
            Form {
                Section("Sortierung") {
                    Toggle("Posteingang in Kategorien aufteilen (Werbung, Soziale Netzwerke, Updates, Foren)",
                           isOn: $model.separateCategories)
                    if !model.senderRules.isEmpty {
                        LabeledContent("Eigene Regeln", value: "\(model.senderRules.count)")
                    }
                }
                Section("Datenschutz") {
                    Toggle("Externe Bilder immer laden (nicht empfohlen – ermöglicht Tracking)", isOn: $model.alwaysLoadRemoteContent)
                }
                Section("Aktualisierung") {
                    Picker("Automatisch abrufen", selection: $model.refreshMinutes) {
                        Text("Nie").tag(0)
                        Text("Jede Minute").tag(1)
                        Text("Alle 3 Minuten").tag(3)
                        Text("Alle 10 Minuten").tag(10)
                        Text("Alle 30 Minuten").tag(30)
                    }
                }
            }
            .formStyle(.grouped)
            .tabItem { Label("Allgemein", systemImage: "gearshape") }

            Form {
                Section("Konten") {
                    if model.accounts.isEmpty {
                        Text("Noch keine Konten.").foregroundStyle(LuTheme.textSecondary)
                    }
                    ForEach(model.accounts) { account in
                        HStack {
                            ProviderDot(providerId: account.providerId)
                            VStack(alignment: .leading) {
                                Text(account.email)
                                Text("\(model.provider(for: account)?.name ?? account.providerId) · \(account.authMethod == .oauth2 ? "OAuth (Browser)" : "Passwort (Browser)")")
                                    .font(.caption)
                                    .foregroundStyle(LuTheme.textSecondary)
                            }
                            Spacer()
                            Button("Entfernen", role: .destructive) { model.removeAccount(account) }
                        }
                    }
                }
                Section("Direkte Anmeldung (OAuth)") {
                    Text("Mit einer eigenen OAuth-Client-ID meldest du dich bei Gmail und Mail.ru direkt auf deren Seite an – ganz ohne App-Passwort. Anleitung: docs/ANMELDUNG.md")
                        .font(.callout)
                        .foregroundStyle(LuTheme.textSecondary)
                    Button("oauth.json im Finder zeigen") { model.revealOAuthConfig() }
                }
            }
            .formStyle(.grouped)
            .tabItem { Label("Konten", systemImage: "person.crop.circle") }
        }
        .frame(width: 560, height: 420)
        .onChange(of: model.separateCategories) { _, _ in model.saveSettings() }
        .onChange(of: model.alwaysLoadRemoteContent) { _, _ in model.saveSettings() }
        .onChange(of: model.refreshMinutes) { _, _ in model.saveSettings() }
    }
}
