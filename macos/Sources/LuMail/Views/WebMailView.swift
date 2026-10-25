import AppKit
import LuMailCore
import SwiftUI
import WebKit

/// Ein Web-Konto: Lu-Ansicht (§21) oder Webseite (§20), je nach Umschalter.
struct WebAccountView: View {
    @Environment(AppModel.self) private var model
    let account: Account
    let folder: String

    var body: some View {
        if let provider = model.provider(for: account), let web = provider.web {
            if model.showsWebsite(account) {
                WebMailView(account: account, folder: folder, provider: provider, web: web)
            } else {
                WebInboxView(account: account, folder: folder, provider: provider, web: web)
            }
        } else {
            Text("Für diesen Anbieter gibt es kein Webmail.")
                .foregroundStyle(LuTheme.textSecondary)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

/// Umschalter „Lu-Ansicht / Webseite“ (nur bei Anbietern mit Leser).
struct WebModePicker: View {
    @Environment(AppModel.self) private var model
    let account: Account

    var body: some View {
        if model.provider(for: account)?.web?.reader != nil {
            Picker("Ansicht", selection: Binding(
                get: { model.showsWebsite(account) },
                set: { model.setShowsWebsite($0, for: account.id) })) {
                Text("Lu-Ansicht").tag(false)
                Text("Webseite").tag(true)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            .help("Mails in Lu-Mails eigener Ansicht oder als Webseite des Anbieters zeigen")
        }
    }
}

/// Das Webmail als Webseite anstelle von Liste und Lesebereich (docs/FUNKTIONEN.md §20).
struct WebMailView: View {
    @Environment(AppModel.self) private var model
    let account: Account
    let folder: String
    let provider: Provider
    let web: WebConfig

    var body: some View {
        let webMail = model.webMail
        VStack(spacing: 0) {
            HStack(spacing: 6) {
                WebModePicker(account: account)
                Button { webMail.goBack(account.id) } label: { Image(systemName: "chevron.left") }
                    .disabled(!webMail.canGoBack.contains(account.id))
                    .help("Zurück")
                Button { webMail.goForward(account.id) } label: { Image(systemName: "chevron.right") }
                    .disabled(!webMail.canGoForward.contains(account.id))
                    .help("Vor")
                Button { webMail.reload(account.id) } label: { Image(systemName: "arrow.clockwise") }
                    .help("Neu laden")
                if webMail.loading.contains(account.id) {
                    ProgressView().controlSize(.small)
                }
                Spacer(minLength: 8)
                AccountBadge(account: account, provider: provider)
                Button {
                    if let url = webMail.currentURL(account.id) { NSWorkspace.shared.open(url) }
                } label: {
                    Image(systemName: "safari")
                }
                .help("Im Standardbrowser öffnen")
            }
            .buttonStyle(.borderless)
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(LuTheme.surface)
            Divider().overlay(LuTheme.border)
            WebViewHost(webView: webMail.webView(for: account.id, config: web))
        }
        .navigationTitle(model.title)
        .task(id: "\(account.id.uuidString)|\(folder)") {
            webMail.open(account.id, config: web, folder: folder)
        }
    }
}

private struct AccountBadge: View {
    let account: Account
    let provider: Provider

    var body: some View {
        HStack(spacing: 6) {
            ProviderDot(providerId: provider.id)
            Text(account.label)
                .font(.callout)
                .foregroundStyle(LuTheme.textSecondary)
                .lineLimit(1)
                .truncationMode(.middle)
        }
    }
}

/// Lu-Ansicht eines Web-Kontos (§21): eigene Liste und eigener Lesebereich, gelesen aus dem Webmail im Hintergrund.
struct WebInboxView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.colorScheme) private var colorScheme
    let account: Account
    let folder: String
    let provider: Provider
    let web: WebConfig

    @State private var selectedId: String?
    @State private var message: WebReader.MessageResult?
    @State private var messageFailed = false
    @State private var allowRemote = false
    @State private var refreshToken = 0

    private var key: String { WebMailService.key(account.id, folder) }
    private var items: [WebReader.Item] { model.webMail.readerItems[key] ?? [] }
    private var selectedItem: WebReader.Item? { items.first { $0.id == selectedId } }

    var body: some View {
        let webMail = model.webMail
        ZStack {
            // Das echte Webmail liegt unsichtbar dahinter, damit die Seite vollständig rendert und gelesen werden kann.
            WebViewHost(webView: webMail.webView(for: account.id, config: web), hidden: true)
            VStack(spacing: 0) {
                toolbar
                Divider().overlay(LuTheme.border)
                HSplitView {
                    list
                        .frame(minWidth: 300, idealWidth: 400, maxWidth: 560)
                    reader
                        .frame(minWidth: 380, maxWidth: .infinity)
                }
            }
        }
        .navigationTitle(model.title)
        .task(id: "\(key)|\(refreshToken)") { await refreshLoop() }
        .onChange(of: selectedId) { _, id in openMessage(id) }
        .onChange(of: key) { _, _ in
            selectedId = nil
            message = nil
        }
    }

    // MARK: Leiste

    private var toolbar: some View {
        HStack(spacing: 8) {
            WebModePicker(account: account)
            Button {
                refreshToken += 1
            } label: {
                Image(systemName: "arrow.clockwise")
            }
            .help("Neu laden")
            if model.webMail.readerLoading.contains(key) {
                ProgressView().controlSize(.small)
            }
            Spacer(minLength: 8)
            AccountBadge(account: account, provider: provider)
            Button {
                model.composeOnWebsite(account)
            } label: {
                Image(systemName: "square.and.pencil")
            }
            .help("Neue E-Mail (auf der Webseite)")
        }
        .buttonStyle(.borderless)
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background(LuTheme.surface)
    }

    // MARK: Liste

    @ViewBuilder
    private var list: some View {
        VStack(spacing: 0) {
            HStack {
                Text(items.count == 1 ? "1 Eintrag" : "\(items.count) Einträge")
                    .font(.caption)
                    .foregroundStyle(LuTheme.textSecondary)
                Spacer()
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 6)
            if items.isEmpty {
                if model.webMail.readerLoading.contains(key) {
                    ProgressView("Lade \(provider.name) …").frame(maxHeight: .infinity)
                } else {
                    ContentUnavailableView {
                        Label("Keine Nachrichten", systemImage: "tray")
                    } description: {
                        Text("Hier ist gerade alles erledigt. 🌙")
                    }
                    .frame(maxHeight: .infinity)
                }
            } else {
                List(selection: $selectedId) {
                    ForEach(items) { item in
                        WebMessageRow(item: item, providerId: provider.id)
                            .tag(item.id)
                    }
                }
                .listStyle(.inset)
                .scrollContentBackground(.hidden)
            }
        }
        .background {
            ZStack {
                NightSkyLayer()
                LuTheme.listPane
            }
        }
    }

    // MARK: Lesebereich

    @ViewBuilder
    private var reader: some View {
        if let item = selectedItem {
            VStack(alignment: .leading, spacing: 0) {
                VStack(alignment: .leading, spacing: 8) {
                    Text(message?.subject.isEmpty == false ? message!.subject : item.displaySubject)
                        .font(.title3.weight(.semibold))
                        .foregroundStyle(LuTheme.textPrimary)
                        .textSelection(.enabled)
                    let part = message?.parts.last
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Text(part?.sender.isEmpty == false ? part!.sender : item.sender).font(.callout.weight(.medium))
                        let email = part?.fromEmail.isEmpty == false ? part!.fromEmail : item.fromEmail
                        if !email.isEmpty {
                            Text("<\(email)>").font(.callout).foregroundStyle(LuTheme.textSecondary)
                        }
                        Spacer()
                        Text(part?.date.isEmpty == false ? part!.date : WebReader.shortDate(item))
                            .font(.caption)
                            .foregroundStyle(LuTheme.textSecondary)
                    }
                    if let to = part?.to, !to.isEmpty {
                        Text("An: \(to)").font(.caption).foregroundStyle(LuTheme.textSecondary)
                    }
                    HStack {
                        Button {
                            Task {
                                await model.webMail.openInPage(account.id, config: web, folder: folder, item: item)
                                model.setShowsWebsite(true, for: account.id)
                            }
                        } label: {
                            Label("In der Webseite öffnen (Antworten, Löschen …)", systemImage: "arrowshape.turn.up.left")
                        }
                        .controlSize(.small)
                        if (message?.parts.count ?? 0) > 1 {
                            Text("\(message!.parts.count) Nachrichten in dieser Unterhaltung")
                                .font(.caption)
                                .foregroundStyle(LuTheme.textSecondary)
                        }
                    }
                }
                .padding(20)
                Divider().overlay(LuTheme.border)
                if let message {
                    if !allowRemote {
                        InfoStrip(icon: "shield.lefthalf.filled", tint: LuTheme.accent, text: "Externe Bilder wurden blockiert.") {
                            Button("Externe Inhalte laden") { allowRemote = true }
                        }
                    }
                    MailWebView(html: MailHTML.prepare(html: Self.combinedHTML(message), text: nil,
                                                       allowRemoteContent: allowRemote, darkMode: colorScheme == .dark)) { url in
                        if url.scheme == "mailto" {
                            model.composeOnWebsite(account)
                        } else {
                            NSWorkspace.shared.open(url)
                        }
                    }
                } else if messageFailed {
                    ContentUnavailableView {
                        Label("Nachricht konnte nicht gelesen werden", systemImage: "exclamationmark.triangle")
                    } description: {
                        Text("Öffne sie in der Webseite.")
                    }
                } else {
                    ProgressView("Lade Nachricht …").frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
            .background(LuTheme.background)
        } else {
            ContentUnavailableView {
                Label("Keine Nachricht ausgewählt", systemImage: "envelope.open")
            } description: {
                Text(provider.name + " · " + account.label)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background {
                ZStack {
                    NightSkyLayer()
                    LuTheme.emptyPane
                }
            }
        }
    }

    /// Alle Teile einer Unterhaltung als ein Dokument (ältere oben).
    static func combinedHTML(_ message: WebReader.MessageResult) -> String {
        guard message.parts.count > 1 else { return message.parts.first?.html ?? "" }
        return message.parts.map { part in
            let header = [part.sender, part.date].filter { !$0.isEmpty }.map(escape).joined(separator: " · ")
            return "<div style=\"margin:0 0 6px;font-size:12px;opacity:.7\">\(header)</div><div>\(part.html)</div>"
        }.joined(separator: "<hr style=\"border:none;border-top:1px solid rgba(128,128,128,.3);margin:18px 0\">")
    }

    static func escape(_ value: String) -> String {
        value.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;")
    }

    // MARK: Laden

    private func refreshLoop() async {
        while !Task.isCancelled {
            // Während eine Nachricht offen ist, bleibt der Browser bei ihr.
            if selectedId == nil {
                let outcome = await model.webMail.loadList(account.id, config: web, folder: folder)
                // Inzwischen anderer Ordner gewählt? Dann zählt das Ergebnis nicht mehr.
                guard !Task.isCancelled else { return }
                switch outcome {
                case .items:
                    break
                case .cancelled:
                    return
                case .needsLogin:
                    model.fallBackToWebsite(account.id, reason: "Bitte bei \(provider.name) anmelden – danach zurück zur Lu-Ansicht.")
                    return
                case .failed:
                    model.fallBackToWebsite(account.id, reason: "Die Lu-Ansicht kann \(provider.name) gerade nicht lesen – die Webseite wird angezeigt.")
                    return
                }
            }
            try? await Task.sleep(for: .seconds(60))
        }
    }

    private func openMessage(_ id: String?) {
        message = nil
        messageFailed = false
        allowRemote = model.alwaysLoadRemoteContent
        guard let id, let item = items.first(where: { $0.id == id }) else { return }
        Task {
            let result = await model.webMail.loadMessage(account.id, config: web, item: item)
            guard selectedId == id else { return }
            message = result
            messageFailed = result == nil
        }
    }
}

/// Zeile der Lu-Ansicht – im Stil der IMAP-Liste.
struct WebMessageRow: View {
    let item: WebReader.Item
    let providerId: String

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            RoundedRectangle(cornerRadius: 2)
                .fill(LuTheme.provider(providerId))
                .frame(width: 3)
                .opacity(0.35)
            Circle()
                .fill(item.unread ? LuTheme.unreadDot : Color.clear)
                .frame(width: 8, height: 8)
                .padding(.top, 5)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline) {
                    Text(item.sender)
                        .font(.body.weight(item.unread ? .semibold : .regular))
                        .foregroundStyle(LuTheme.textPrimary)
                        .lineLimit(1)
                    Spacer(minLength: 6)
                    if item.attachment {
                        Image(systemName: "paperclip").font(.caption).foregroundStyle(LuTheme.textMuted)
                    }
                    Text(WebReader.shortDate(item))
                        .font(.caption)
                        .foregroundStyle(LuTheme.textSecondary)
                        .lineLimit(1)
                }
                Text(item.displaySubject)
                    .font(.callout.weight(item.unread ? .medium : .regular))
                    .foregroundStyle(item.unread ? LuTheme.textPrimary : LuTheme.textSecondary)
                    .lineLimit(1)
                if !item.snippet.isEmpty {
                    Text(item.snippet)
                        .font(.caption)
                        .foregroundStyle(LuTheme.textMuted)
                        .lineLimit(2)
                }
            }
        }
        .padding(.vertical, 4)
        .help(item.dateTitle)
    }
}

/// Anmeldung auf der Webseite des Anbieters im eingebetteten Browser (Schritt im Assistenten).
struct WebLoginView: View {
    let provider: Provider
    let webView: WKWebView

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                Text(String(provider.name.prefix(1)))
                    .font(.headline)
                    .foregroundStyle(.white)
                    .frame(width: 28, height: 28)
                    .background(RoundedRectangle(cornerRadius: 7).fill(LuTheme.provider(provider.id).gradient))
                VStack(alignment: .leading, spacing: 1) {
                    Text("Melde dich bei \(provider.name) an").font(.headline)
                    Text("Ganz normal mit deinem Passwort – Lu-Mail speichert es nicht. Danach geht es automatisch weiter, sonst auf „Fertig“ klicken.")
                        .font(.caption)
                        .foregroundStyle(LuTheme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            WebViewHost(webView: webView)
                .clipShape(RoundedRectangle(cornerRadius: LuTheme.radiusMedium))
                .overlay(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).stroke(LuTheme.border))
        }
    }
}
