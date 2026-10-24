import AppKit
import LuMailCore
import SwiftUI

/// Anbieter wählen → „Im Browser anmelden“ → warten, bis der Browser fertig ist.
struct AddAccountView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var selectedProvider: Provider?
    @State private var emailHint = ""
    @State private var signInTask: Task<Void, Never>?
    @State private var isWaiting = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack(spacing: 10) {
                MoonMark(size: 22)
                Text("Konto hinzufügen").font(.title2.weight(.semibold))
            }

            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 10), count: 3), spacing: 10) {
                ForEach(model.catalog.providers) { provider in
                    ProviderTile(provider: provider, isSelected: selectedProvider?.id == provider.id) {
                        selectedProvider = provider
                        errorMessage = nil
                    }
                    .disabled(isWaiting)
                }
            }

            if let provider = selectedProvider {
                details(provider)
            } else {
                Text("Wähle deinen Anbieter. Die Anmeldung läuft anschließend in deinem Browser.")
                    .foregroundStyle(LuTheme.textSecondary)
            }

            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(LuTheme.danger)
                    .font(.callout)
                    .fixedSize(horizontal: false, vertical: true)
            }

            HStack {
                Spacer()
                Button(isWaiting ? "Abbrechen" : "Schließen") {
                    if isWaiting {
                        signInTask?.cancel()
                    } else {
                        dismiss()
                    }
                }
                .keyboardShortcut(.cancelAction)
                Button {
                    startSignIn()
                } label: {
                    if isWaiting {
                        HStack(spacing: 8) {
                            ProgressView().controlSize(.small)
                            Text("Bitte im Browser fortfahren …")
                        }
                    } else {
                        Label("Im Browser anmelden", systemImage: "safari")
                    }
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(selectedProvider == nil || isWaiting || !canSignIn)
            }
        }
        .padding(24)
        .frame(width: 680)
        .background(LuTheme.surface)
        .onDisappear { signInTask?.cancel() }
    }

    @ViewBuilder
    private func details(_ provider: Provider) -> some View {
        let usesOAuth = model.auth.usesOAuth(provider)
        VStack(alignment: .leading, spacing: 10) {
            TextField("E-Mail-Adresse (optional)", text: $emailHint)
                .textFieldStyle(.roundedBorder)
                .disabled(isWaiting)

            if !provider.authMethods.contains(.password) && !usesOAuth {
                Label("\(provider.name) erlaubt die Anmeldung nur über die eigene Anmeldeseite (OAuth). Trage dafür einmalig eine Client-ID in oauth.json ein.",
                      systemImage: "exclamationmark.triangle")
                    .foregroundStyle(LuTheme.moon)
                Button("oauth.json öffnen und Anleitung lesen …") { model.revealOAuthConfig() }
                    .buttonStyle(.link)
            } else if usesOAuth {
                Label("Du wirst zur Anmeldeseite von \(provider.name) weitergeleitet. Lu-Mail sieht dein Passwort nie.",
                      systemImage: "lock.shield")
            } else {
                Label("Im Browser öffnet sich eine Lu-Mail-Anmeldeseite (läuft lokal auf deinem Mac). Dort gibst du dein \(provider.passwordLabel) ein.",
                      systemImage: "lock.shield")
                if provider.needsCustomServers {
                    Text("Auf der Anmeldeseite gibst du zusätzlich IMAP- und SMTP-Server ein.")
                        .font(.callout)
                        .foregroundStyle(LuTheme.textSecondary)
                }
                Text(provider.passwordHelp)
                    .font(.callout)
                    .foregroundStyle(LuTheme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
                HStack(spacing: 14) {
                    if let url = URL(string: provider.helpUrl) {
                        Link("Anleitung öffnen ↗", destination: url)
                    }
                    if provider.authMethods.contains(.oauth2) {
                        Button("Direkte \(provider.name)-Anmeldung einrichten …") { model.revealOAuthConfig() }
                            .buttonStyle(.link)
                            .help("Trage in oauth.json eine eigene OAuth-Client-ID ein – siehe docs/ANMELDUNG.md")
                    }
                }
                .font(.callout)
            }
        }
        .font(.callout)
        .foregroundStyle(LuTheme.textPrimary)
    }

    /// OAuth-only-Anbieter (Outlook) brauchen eine Client-ID in oauth.json.
    private var canSignIn: Bool {
        guard let provider = selectedProvider else { return false }
        return provider.authMethods.contains(.password) || model.auth.usesOAuth(provider)
    }

    private func startSignIn() {
        guard let provider = selectedProvider else { return }
        errorMessage = nil
        isWaiting = true
        signInTask = Task {
            do {
                try await model.addAccount(provider: provider, emailHint: emailHint)
                isWaiting = false
                dismiss()
            } catch is CancellationError {
                isWaiting = false
            } catch {
                isWaiting = false
                errorMessage = AuthService.friendlyLoginError(error, provider: provider)
            }
            // Lu-Mail wieder nach vorne holen, nachdem der Browser fertig ist.
            NSApplication.shared.activate(ignoringOtherApps: true)
        }
    }
}

struct ProviderTile: View {
    let provider: Provider
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 12) {
                Text(String(provider.name.prefix(1)))
                    .font(.title3.weight(.bold))
                    .foregroundStyle(.white)
                    .frame(width: 32, height: 32)
                    .background(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).fill(LuTheme.provider(provider.id).gradient))
                VStack(alignment: .leading, spacing: 2) {
                    Text(provider.name).font(.headline).foregroundStyle(LuTheme.textPrimary)
                    Text(provider.domains.isEmpty ? "eigene Domain" : provider.domains.prefix(2).joined(separator: ", "))
                        .font(.caption)
                        .foregroundStyle(LuTheme.textSecondary)
                        .lineLimit(1)
                }
                Spacer()
                if isSelected {
                    Image(systemName: "checkmark.circle.fill").foregroundStyle(LuTheme.accent)
                }
            }
            .padding(12)
            .background(RoundedRectangle(cornerRadius: LuTheme.radiusLarge).fill(isSelected ? LuTheme.surfaceSelected : LuTheme.background))
            .overlay(RoundedRectangle(cornerRadius: LuTheme.radiusLarge)
                .stroke(isSelected ? LuTheme.accent : LuTheme.border, lineWidth: isSelected ? 2 : 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
