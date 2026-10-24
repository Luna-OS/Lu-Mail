import AppKit
import LuMailCore
import Network
import SwiftUI
import WebKit

/// Assistent „Konto hinzufügen“ (docs/FUNKTIONEN.md §17):
/// 1. E-Mail-Adresse eingeben oder Anbieter antippen → 2. „So geht's“ → Anmelden auf der Anbieter-Webseite
/// (Web-Konto, §20) oder – unter „Erweitert“ – per IMAP im Browser.
struct AddAccountView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss

    enum Step: Equatable {
        case choose
        case guide
        case webLogin
        case waiting
        case done(String)
    }

    @State private var step: Step = .choose
    @State private var email = ""
    @State private var provider: Provider?
    @State private var servers: CustomServers?
    @State private var isDiscovering = false
    @State private var errorMessage: String?
    @State private var bridgeRunning: Bool?
    @State private var signInTask: Task<Void, Never>?
    /// IMAP statt Web-Konto (nur über „Erweitert“, oder wenn der Anbieter kein Webmail hat).
    @State private var useIMAP = false
    @State private var webLoginId: UUID?
    @State private var webLoginView: WKWebView?
    @FocusState private var emailFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            Group {
                switch step {
                case .choose: chooseStep
                case .guide: guideStep
                case .webLogin: webLoginStep
                case .waiting: waitingStep
                case .done(let address): doneStep(address)
                }
            }
            .padding(step == .webLogin ? 16 : 24)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)

            Divider().overlay(LuTheme.border)
            footer
                .padding(.horizontal, 24)
                .padding(.vertical, 14)
        }
        .frame(width: step == .webLogin ? 980 : 620, height: step == .webLogin ? 760 : 560)
        .background(LuTheme.surface)
        .onDisappear {
            signInTask?.cancel()
            cancelWebLogin()
        }
    }

    // MARK: Schritt 1

    private var chooseStep: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack(spacing: 10) {
                MoonMark(size: 22)
                Text("Konto hinzufügen").font(.title2.weight(.semibold))
            }
            VStack(alignment: .leading, spacing: 6) {
                Text("Wie lautet deine E-Mail-Adresse?").font(.headline)
                TextField("name@beispiel.de", text: $email)
                    .textFieldStyle(.roundedBorder)
                    .controlSize(.large)
                    .focused($emailFocused)
                    .onSubmit(continueWithEmail)
                Text("Lu-Mail erkennt deinen Anbieter automatisch – auch bei eigenen Domains.")
                    .font(.caption)
                    .foregroundStyle(LuTheme.textSecondary)
            }
            Text("Oder Anbieter wählen").font(.subheadline.weight(.medium)).foregroundStyle(LuTheme.textSecondary)
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
                ForEach(model.catalog.providers) { provider in
                    ProviderTile(provider: provider) { open(provider, servers: nil) }
                }
            }
            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle.fill").foregroundStyle(LuTheme.danger).font(.callout)
            }
        }
        .onAppear { emailFocused = true }
    }

    private func continueWithEmail() {
        let address = email.trimmingCharacters(in: .whitespaces)
        guard address.contains("@"), address.split(separator: "@").last?.contains(".") == true else {
            errorMessage = "Bitte eine vollständige E-Mail-Adresse eingeben."
            return
        }
        errorMessage = nil
        isDiscovering = true
        Task {
            let result = await ProviderDiscovery(catalog: model.catalog).discover(email: address)
            isDiscovering = false
            open(result.provider, servers: result.servers)
        }
    }

    private func open(_ provider: Provider, servers: CustomServers?) {
        self.provider = provider
        self.servers = servers
        errorMessage = nil
        bridgeRunning = nil
        useIMAP = provider.web == nil
        step = .guide
        if useIMAP { checkBridge() }
    }

    private var usesWeb: Bool { provider?.web != nil && !useIMAP }

    // MARK: Schritt 2

    private var usesOAuth: Bool { provider.map { model.auth.usesOAuth($0) } ?? false }
    private var oauthOnlyWithoutClient: Bool {
        guard let provider, !usesWeb else { return false }
        return !provider.authMethods.contains(.password) && !usesOAuth
    }

    @ViewBuilder
    private var guideStep: some View {
        if let provider {
            VStack(alignment: .leading, spacing: 16) {
                HStack(spacing: 12) {
                    Text(String(provider.name.prefix(1)))
                        .font(.title2.weight(.bold))
                        .foregroundStyle(.white)
                        .frame(width: 44, height: 44)
                        .background(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).fill(LuTheme.provider(provider.id).gradient))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(provider.name).font(.title2.weight(.semibold))
                        Text(email.isEmpty ? "Neues Konto" : email).font(.callout).foregroundStyle(LuTheme.textSecondary)
                    }
                }

                if usesWeb {
                    webGuide(provider)
                } else {
                    imapGuide(provider)
                }

                if let errorMessage {
                    Label(errorMessage, systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(LuTheme.danger)
                        .font(.callout)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    /// Einfacher Weg: auf der Webseite des Anbieters anmelden (§20).
    @ViewBuilder
    private func webGuide(_ provider: Provider) -> some View {
        Label("Ganz normal mit deinem Passwort – kein App-Passwort, keine Bridge nötig.", systemImage: "checkmark.seal.fill")
            .font(.callout.weight(.medium))
            .foregroundStyle(LuTheme.success)
        Text("So geht's").font(.headline)
        stepList([
            "Auf „Bei \(provider.name) anmelden“ klicken.",
            "Auf der Seite von \(provider.name) wie gewohnt anmelden – auch mit Bestätigungscode oder Zwei-Faktor.",
            "Fertig: Dein Postfach mit allen Ordnern erscheint links in Lu-Mail.",
        ].map { GuideStep(text: $0) })
        Spacer(minLength: 0)
        Button("Erweitert: per IMAP verbinden (App-Passwort/OAuth)") {
            useIMAP = true
            checkBridge()
        }
        .buttonStyle(.link)
        .font(.callout)
    }

    private struct GuideStep {
        var text: String
        var url: String?
        var button: String?
    }

    @ViewBuilder
    private func stepList(_ steps: [GuideStep]) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(Array(steps.enumerated()), id: \.offset) { index, item in
                HStack(alignment: .top, spacing: 10) {
                    Text("\(index + 1)")
                        .font(.caption.weight(.bold))
                        .frame(width: 22, height: 22)
                        .background(Circle().fill(LuTheme.accent.opacity(0.18)))
                        .foregroundStyle(LuTheme.accent)
                    VStack(alignment: .leading, spacing: 6) {
                        Text(item.text).fixedSize(horizontal: false, vertical: true)
                        if let urlString = item.url, let url = URL(string: urlString) {
                            Button {
                                NSWorkspace.shared.open(url)
                            } label: {
                                Label(item.button ?? "Öffnen", systemImage: "arrow.up.right.square")
                            }
                            .controlSize(.small)
                        }
                    }
                }
            }
        }
    }

    /// Bisheriger Weg: IMAP mit App-Passwort, Bridge oder OAuth.
    @ViewBuilder
    private func imapGuide(_ provider: Provider) -> some View {
        Text("So geht's").font(.headline)
        let steps = (usesOAuth ? provider.setup?.oauthSteps : nil) ?? provider.setup?.passwordSteps ?? []
        stepList(steps.map { GuideStep(text: $0.text, url: $0.url, button: $0.button) })

        if provider.setup?.checkLocalPort != nil {
            HStack(spacing: 8) {
                switch bridgeRunning {
                case .some(true):
                    Label("Bridge läuft", systemImage: "checkmark.circle.fill").foregroundStyle(LuTheme.success)
                case .some(false):
                    Label("Bridge nicht gefunden – bitte starten", systemImage: "xmark.circle.fill").foregroundStyle(LuTheme.danger)
                case .none:
                    ProgressView().controlSize(.small)
                    Text("Prüfe Bridge …")
                }
                Button("Erneut prüfen", action: checkBridge).controlSize(.small)
            }
            .font(.callout)
        }

        if let servers {
            Label("Servereinstellungen gefunden: \(servers.imap.host) / \(servers.smtp.host)", systemImage: "wand.and.stars")
                .font(.callout)
                .foregroundStyle(LuTheme.textSecondary)
        }

        if oauthOnlyWithoutClient {
            Label("\(provider.name) erlaubt nur die Anmeldung über die eigene Anmeldeseite. Dafür muss in Lu-Mail einmalig eine Client-ID eingerichtet werden (docs/OAUTH-EINRICHTEN.md).",
                  systemImage: "exclamationmark.triangle")
                .font(.callout)
                .foregroundStyle(LuTheme.moon)
                .fixedSize(horizontal: false, vertical: true)
        }

        if provider.web != nil {
            Button("Zurück zur einfachen Anmeldung (ohne App-Passwort)") { useIMAP = false }
                .buttonStyle(.link)
                .font(.callout)
        }
    }

    private func checkBridge() {
        guard let port = provider?.setup?.checkLocalPort else { return }
        bridgeRunning = nil
        Task { bridgeRunning = await LocalPortProbe.isOpen(port: port) }
    }

    // MARK: Anmeldung auf der Webseite (§20)

    @ViewBuilder
    private var webLoginStep: some View {
        if let provider, let webLoginView {
            WebLoginView(provider: provider, webView: webLoginView)
        }
    }

    private func startWebLogin() {
        guard let provider, let web = provider.web else { return }
        errorMessage = nil
        let id = UUID()
        webLoginId = id
        model.webMail.onLoggedIn = { loggedInId in
            if loggedInId == id { completeWebLogin() }
        }
        webLoginView = model.webMail.beginLogin(id, config: web)
        step = .webLogin
    }

    private func completeWebLogin() {
        guard let provider, let id = webLoginId else { return }
        webLoginId = nil
        model.webMail.onLoggedIn = nil
        let account = model.addWebAccount(id: id, provider: provider, emailHint: email)
        step = .done(account.label)
        Task {
            try? await Task.sleep(for: .seconds(1.2))
            dismiss()
        }
    }

    /// Abbrechen: den gerade angelegten Cookie-Speicher wieder löschen.
    private func cancelWebLogin() {
        guard let id = webLoginId else { return }
        webLoginId = nil
        webLoginView = nil
        model.webMail.onLoggedIn = nil
        model.webMail.discard(id)
    }

    // MARK: Warten & Fertig

    private var waitingStep: some View {
        VStack(spacing: 16) {
            Spacer()
            ProgressView().controlSize(.large)
            Text("Bitte im Browser fortfahren …").font(.title3.weight(.semibold))
            Text(usesOAuth ? "Melde dich im geöffneten Browserfenster an und erlaube Lu-Mail den Zugriff."
                           : "Gib im geöffneten Browserfenster dein \(provider?.passwordLabel ?? "Passwort") ein.")
                .multilineTextAlignment(.center)
                .foregroundStyle(LuTheme.textSecondary)
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }

    private func doneStep(_ address: String) -> some View {
        VStack(spacing: 14) {
            Spacer()
            Image(systemName: "checkmark.circle.fill").font(.system(size: 52)).foregroundStyle(LuTheme.success)
            Text("\(address) wurde hinzugefügt").font(.title3.weight(.semibold))
            Text("Dein Posteingang wird geladen.").foregroundStyle(LuTheme.textSecondary)
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }

    // MARK: Fußzeile (immer sichtbar)

    private var footer: some View {
        HStack {
            switch step {
            case .choose:
                Button("Abbrechen") { dismiss() }.keyboardShortcut(.cancelAction)
                Spacer()
                Button {
                    continueWithEmail()
                } label: {
                    if isDiscovering {
                        HStack(spacing: 6) { ProgressView().controlSize(.small); Text("Suche Einstellungen …") }
                    } else {
                        Text("Weiter")
                    }
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(email.trimmingCharacters(in: .whitespaces).isEmpty || isDiscovering)
            case .guide:
                Button("Zurück") { step = .choose }
                Spacer()
                Button("Abbrechen") { dismiss() }.keyboardShortcut(.cancelAction)
                if usesWeb {
                    Button(action: startWebLogin) {
                        Label("Bei \(provider?.name ?? "") anmelden", systemImage: "globe")
                    }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
                } else {
                    Button(action: startSignIn) {
                        Label(primaryTitle, systemImage: "safari")
                    }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
                    .disabled(oauthOnlyWithoutClient)
                }
            case .webLogin:
                Button("Zurück") {
                    cancelWebLogin()
                    step = .guide
                }
                Spacer()
                Button("Abbrechen") {
                    cancelWebLogin()
                    dismiss()
                }
                .keyboardShortcut(.cancelAction)
                Button("Fertig", action: completeWebLogin)
                    .buttonStyle(.borderedProminent)
            case .waiting:
                Spacer()
                Button("Abbrechen") { signInTask?.cancel() }.keyboardShortcut(.cancelAction)
            case .done:
                Spacer()
                Button("Fertig") { dismiss() }.keyboardShortcut(.defaultAction)
            }
        }
    }

    private var primaryTitle: String {
        guard let provider, usesOAuth else { return "Im Browser anmelden" }
        switch provider.id {
        case "gmail": return "Mit Google anmelden"
        case "outlook": return "Mit Microsoft anmelden"
        default: return "Mit \(provider.name) anmelden"
        }
    }

    private func startSignIn() {
        guard let provider else { return }
        errorMessage = nil
        step = .waiting
        let hint = email.trimmingCharacters(in: .whitespaces)
        signInTask = Task {
            do {
                try await model.addAccount(provider: provider, emailHint: hint, serversHint: servers)
                NSApplication.shared.activate(ignoringOtherApps: true)
                step = .done(model.accounts.last?.email ?? hint)
                try? await Task.sleep(for: .seconds(1.5))
                dismiss()
            } catch is CancellationError {
                step = .guide
            } catch {
                NSApplication.shared.activate(ignoringOtherApps: true)
                errorMessage = AuthService.friendlyLoginError(error, provider: provider) + " Klicke erneut auf „\(primaryTitle)“, um es noch einmal zu versuchen."
                step = .guide
            }
        }
    }
}

/// Kompakte Anbieter-Kachel (alle 10 passen ohne Scrollen).
struct ProviderTile: View {
    let provider: Provider
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Text(String(provider.name.prefix(1)))
                    .font(.headline)
                    .foregroundStyle(.white)
                    .frame(width: 28, height: 28)
                    .background(RoundedRectangle(cornerRadius: 7).fill(LuTheme.provider(provider.id).gradient))
                Text(provider.name).font(.callout.weight(.medium)).lineLimit(1)
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).fill(LuTheme.background))
            .overlay(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).stroke(LuTheme.border))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// Prüft, ob ein lokaler Dienst (z. B. die Proton Mail Bridge) erreichbar ist.
enum LocalPortProbe {
    static func isOpen(port: Int, timeout: TimeInterval = 1.5) async -> Bool {
        guard let nwPort = NWEndpoint.Port(rawValue: UInt16(clamping: port)) else { return false }
        let connection = NWConnection(host: "127.0.0.1", port: nwPort, using: .tcp)
        let once = ResumeOnce()
        return await withCheckedContinuation { continuation in
            connection.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    once.run { continuation.resume(returning: true) }
                    connection.cancel()
                case .failed, .waiting:
                    once.run { continuation.resume(returning: false) }
                    connection.cancel()
                default:
                    break
                }
            }
            connection.start(queue: .global())
            DispatchQueue.global().asyncAfter(deadline: .now() + timeout) {
                once.run { continuation.resume(returning: false) }
                connection.cancel()
            }
        }
    }
}
