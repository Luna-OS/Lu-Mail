import LuMailCore
import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        TabView {
            GeneralSettings()
                .tabItem { Label("Allgemein", systemImage: "gearshape") }
            AppearanceSettings()
                .tabItem { Label("Darstellung", systemImage: "moon.stars") }
            AccountSettings()
                .tabItem { Label("Konten", systemImage: "person.crop.circle") }
            RuleSettings()
                .tabItem { Label("Regeln", systemImage: "line.3.horizontal.decrease.circle") }
            PrivacySettings()
                .tabItem { Label("Datenschutz", systemImage: "hand.raised") }
            UpdateSettingsView()
                .tabItem { Label("Updates", systemImage: "arrow.down.circle") }
        }
        .frame(width: 640, height: 520)
    }
}

private struct GeneralSettings: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var model = model
        Form {
            Section("Sortierung") {
                Toggle("Posteingang in Kategorien aufteilen (Werbung, Soziale Netzwerke, Updates, Foren)", isOn: $model.separateCategories)
                Toggle("Nachrichten als Konversationen gruppieren", isOn: $model.conversationMode)
            }
            Section("Neue Mails") {
                Toggle("Sofort per Push (IMAP IDLE)", isOn: $model.pushEnabled)
                Picker("Zusätzlich abrufen", selection: $model.refreshMinutes) {
                    Text("Nie").tag(0)
                    Text("Jede Minute").tag(1)
                    Text("Alle 3 Minuten").tag(3)
                    Text("Alle 10 Minuten").tag(10)
                    Text("Alle 30 Minuten").tag(30)
                }
                Toggle("Mitteilungen bei neuen Mails (Posteingang und Updates)", isOn: $model.notificationsEnabled)
            }
            Section("Aufräumen") {
                Text("Ältere Nachrichten dieser Kategorien automatisch archivieren (markierte bleiben liegen).")
                    .font(.caption)
                    .foregroundStyle(LuTheme.textSecondary)
                ForEach([MailCategory.promotions, .social, .updates, .forums], id: \.self) { category in
                    Picker(category.title, selection: Binding(
                        get: { model.cleanupDays[category] ?? 0 },
                        set: { model.cleanupDays[category] = $0; model.saveSettings() })) {
                        Text("Aus").tag(0)
                        Text("nach 7 Tagen").tag(7)
                        Text("nach 30 Tagen").tag(30)
                        Text("nach 90 Tagen").tag(90)
                    }
                }
            }
        }
        .formStyle(.grouped)
        .onChange(of: model.separateCategories) { _, _ in model.saveSettings() }
        .onChange(of: model.conversationMode) { _, _ in model.saveSettings() }
        .onChange(of: model.pushEnabled) { _, _ in model.saveSettings() }
        .onChange(of: model.refreshMinutes) { _, _ in model.saveSettings() }
        .onChange(of: model.notificationsEnabled) { _, _ in model.saveSettings() }
    }
}

private struct AppearanceSettings: View {
    @State private var theme = ThemeSettings.shared
    private let tokens = ThemeTokens.shared

    var body: some View {
        Form {
            Section("Modus") {
                Picker("Erscheinungsbild", selection: Binding(get: { theme.mode }, set: { theme.set(mode: $0) })) {
                    ForEach(ThemeSettings.Mode.allCases) { mode in Text(mode.title).tag(mode) }
                }
                .pickerStyle(.segmented)
            }
            Section("Variante") {
                ForEach(ThemeTokens.variantOrder, id: \.self) { key in
                    if let variant = tokens.variants[key] {
                        Button {
                            theme.set(variant: key)
                        } label: {
                            HStack(spacing: 12) {
                                HStack(spacing: 0) {
                                    Rectangle().fill(Color(hex: variant.colors.dark.background))
                                    Rectangle().fill(Color(hex: variant.colors.light.background))
                                }
                                .frame(width: 44, height: 28)
                                .clipShape(RoundedRectangle(cornerRadius: 6))
                                .overlay(RoundedRectangle(cornerRadius: 6).stroke(LuTheme.border))
                                VStack(alignment: .leading) {
                                    Text(variant.name).font(.headline)
                                    Text(variant.description).font(.caption).foregroundStyle(LuTheme.textSecondary)
                                }
                                Spacer()
                                if theme.variant == key { Image(systemName: "checkmark.circle.fill").foregroundStyle(LuTheme.accent) }
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            Section("Akzentfarbe") {
                HStack(spacing: 14) {
                    ForEach(ThemeTokens.accentOrder, id: \.self) { key in
                        if let accent = tokens.accents[key] {
                            Button {
                                theme.set(accent: key)
                            } label: {
                                VStack(spacing: 4) {
                                    Circle()
                                        .fill(Color(hex: accent.dark))
                                        .frame(width: 26, height: 26)
                                        .overlay(Circle().stroke(theme.accent == key ? LuTheme.textPrimary : .clear, lineWidth: 2).padding(-3))
                                    Text(accent.name).font(.caption2)
                                }
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
        }
        .formStyle(.grouped)
    }
}

private struct AccountSettings: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        Form {
            Section("Konten") {
                if model.accounts.isEmpty {
                    Text("Noch keine Konten.").foregroundStyle(LuTheme.textSecondary)
                }
                ForEach(model.accounts) { account in
                    AccountRow(account: account)
                }
            }
            Section("Direkte Anmeldung (OAuth)") {
                Text("Mit einer eigenen OAuth-Client-ID meldest du dich bei Gmail, Outlook und Mail.ru direkt auf deren Seite an – ganz ohne App-Passwort. Anleitung: docs/ANMELDUNG.md")
                    .font(.callout)
                    .foregroundStyle(LuTheme.textSecondary)
                Button("oauth.json im Finder zeigen") { model.revealOAuthConfig() }
            }
        }
        .formStyle(.grouped)
    }
}

private struct AccountRow: View {
    @Environment(AppModel.self) private var model
    let account: Account
    @State private var signature = ""
    @State private var editing = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                ProviderDot(providerId: account.providerId)
                VStack(alignment: .leading) {
                    Text(account.email)
                    Text("\(model.provider(for: account)?.name ?? account.providerId) · \(account.authMethod == .oauth2 ? "OAuth (Browser)" : "Passwort (Browser)")")
                        .font(.caption)
                        .foregroundStyle(LuTheme.textSecondary)
                }
                Spacer()
                Button(editing ? "Fertig" : "Signatur") {
                    if editing { model.updateSignature(signature, for: account) }
                    editing.toggle()
                }
                Button("Entfernen", role: .destructive) { model.removeAccount(account) }
            }
            if editing {
                TextEditor(text: $signature)
                    .font(.callout)
                    .frame(height: 70)
                    .overlay(RoundedRectangle(cornerRadius: 6).stroke(LuTheme.border))
            }
        }
        .onAppear { signature = account.signature ?? "" }
    }
}

private struct RuleSettings: View {
    @Environment(AppModel.self) private var model
    @State private var newField: ClassificationRule.Field = .subject
    @State private var newText = ""
    @State private var newCategory: MailCategory = .promotions
    @State private var newCategoryName = ""
    @State private var newCategoryColor = "#22C55E"

    private let colors = ["#22C55E", "#0EA5E9", "#F43F5E", "#EAB308", "#8B5CF6", "#14B8A6", "#F97316"]

    var body: some View {
        Form {
            Section("Eigene Kategorien") {
                ForEach(model.rules.customCategories) { category in
                    HStack {
                        Circle().fill(Color(hex: category.color)).frame(width: 10, height: 10)
                        Text(category.name)
                        Spacer()
                        Button("Löschen", role: .destructive) { model.removeCustomCategory(category) }
                    }
                }
                HStack {
                    TextField("Neue Kategorie, z. B. Rechnungen", text: $newCategoryName)
                    ForEach(colors, id: \.self) { color in
                        Circle().fill(Color(hex: color)).frame(width: 14, height: 14)
                            .overlay(Circle().stroke(newCategoryColor == color ? LuTheme.textPrimary : .clear, lineWidth: 2).padding(-2))
                            .onTapGesture { newCategoryColor = color }
                    }
                    Button("Anlegen") {
                        if let created = model.addCustomCategory(name: newCategoryName, color: newCategoryColor) {
                            newCategory = created.category
                            newCategoryName = ""
                        }
                    }
                    .disabled(newCategoryName.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
            Section("Regeln") {
                ForEach(model.rules.rules) { rule in
                    HStack {
                        Text("\(rule.field.germanName) enthält „\(rule.contains)“ → \(rule.category.title(model.rules))")
                        Spacer()
                        Button("Löschen", role: .destructive) { model.removeRule(rule) }
                    }
                }
                HStack {
                    Picker("", selection: $newField) {
                        ForEach(ClassificationRule.Field.allCases, id: \.self) { Text($0.germanName).tag($0) }
                    }
                    .labelsHidden()
                    .frame(width: 120)
                    TextField("enthält …", text: $newText)
                    Picker("", selection: $newCategory) {
                        ForEach(model.allCategories, id: \.self) { Text($0.title(model.rules)).tag($0) }
                    }
                    .labelsHidden()
                    .frame(width: 170)
                    Button("Hinzufügen") {
                        model.addRule(ClassificationRule(field: newField, contains: newText, category: newCategory))
                        newText = ""
                    }
                    .disabled(newText.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
            Section("Absender-Regeln") {
                if model.rules.senders.isEmpty {
                    Text("Per Rechtsklick auf eine Mail → „Absender immer einsortieren als …“").font(.caption).foregroundStyle(LuTheme.textSecondary)
                }
                ForEach(model.rules.senders.keys.sorted(), id: \.self) { key in
                    HStack {
                        Text(key)
                        Spacer()
                        Text(model.rules.senders[key]?.title(model.rules) ?? "").foregroundStyle(LuTheme.textSecondary)
                        Button("Löschen", role: .destructive) { model.removeSenderRule(key) }
                    }
                }
            }
        }
        .formStyle(.grouped)
    }
}

private struct PrivacySettings: View {
    @Environment(AppModel.self) private var model
    @State private var lock = AppLock.shared

    var body: some View {
        @Bindable var model = model
        Form {
            Section("Inhalte") {
                Toggle("Externe Bilder immer laden (nicht empfohlen – ermöglicht Tracking)", isOn: $model.alwaysLoadRemoteContent)
            }
            Section("App-Sperre") {
                Toggle("Lu-Mail mit \(lock.biometryName) sperren", isOn: Binding(get: { lock.isEnabled }, set: { lock.isEnabled = $0; lock.save() }))
                Picker("Sperren nach", selection: Binding(get: { lock.lockAfterMinutes }, set: { lock.lockAfterMinutes = $0; lock.save() })) {
                    Text("sofort").tag(0)
                    Text("5 Minuten im Hintergrund").tag(5)
                    Text("15 Minuten im Hintergrund").tag(15)
                    Text("60 Minuten im Hintergrund").tag(60)
                }
                .disabled(!lock.isEnabled)
            }
            Section("Offline-Cache") {
                Toggle("Nachrichten für die Offline-Nutzung speichern (max. 200 MB)", isOn: $model.offlineCacheEnabled)
                Button("Cache leeren") { model.clearCache() }
            }
        }
        .formStyle(.grouped)
        .onChange(of: model.alwaysLoadRemoteContent) { _, _ in model.saveSettings() }
        .onChange(of: model.offlineCacheEnabled) { _, _ in model.saveSettings() }
    }
}
