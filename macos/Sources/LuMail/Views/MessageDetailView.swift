import AppKit
import LuMailCore
import SwiftUI
import UniformTypeIdentifiers
import WebKit

struct MessageDetailView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        Group {
            if model.selectedMessageIDs.count > 1 {
                BatchActionsView(messages: model.selectedMessages)
            } else if let message = model.selectedMessage {
                let thread = model.selectedThread
                VStack(spacing: 0) {
                    if thread.count > 1 {
                        ScrollView {
                            VStack(spacing: 12) {
                                Text(ConversationBuilder.normalizedSubject(message.subject).isEmpty ? "(kein Betreff)" : message.subject)
                                    .font(.title2.weight(.semibold))
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(.horizontal, 20)
                                    .padding(.top, 16)
                                ForEach(thread) { item in
                                    ThreadMessageCard(message: item, startsExpanded: item.id == thread.last?.id || !item.isSeen)
                                }
                            }
                            .padding(.bottom, 20)
                        }
                    } else {
                        SingleMessageView(message: message)
                    }
                }
                .toolbar { MessageToolbar(model: model, message: thread.last ?? message) }
            } else {
                ContentUnavailableView {
                    Label("Keine Nachricht ausgewählt", systemImage: "envelope.open")
                } description: {
                    Text("Wähle links eine Nachricht aus. Tipp: j/k blättern, e archiviert, r antwortet.")
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(LuTheme.surface)
    }
}

/// Eine einzelne Nachricht mit Kopf, Hinweisen und Inhalt.
struct SingleMessageView: View {
    @Environment(AppModel.self) private var model
    let message: MessageSummary

    var body: some View {
        VStack(spacing: 0) {
            MessageHeader(message: message, parsed: model.parsed(message), showsSubject: true)
            Divider().overlay(LuTheme.border)
            MessageBody(message: message, fillsSpace: true)
        }
    }
}

/// Eine Nachricht in der Konversationsansicht (einklappbar).
struct ThreadMessageCard: View {
    @Environment(AppModel.self) private var model
    let message: MessageSummary
    @State private var expanded: Bool

    init(message: MessageSummary, startsExpanded: Bool) {
        self.message = message
        _expanded = State(initialValue: startsExpanded)
    }

    var body: some View {
        VStack(spacing: 0) {
            Button {
                expanded.toggle()
            } label: {
                MessageHeader(message: message, parsed: model.parsed(message), showsSubject: false, compact: !expanded)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            if expanded {
                Divider().overlay(LuTheme.border)
                MessageBody(message: message, fillsSpace: false)
                    .frame(height: 420)
            }
        }
        .background(RoundedRectangle(cornerRadius: LuTheme.radiusLarge).fill(LuTheme.background))
        .overlay(RoundedRectangle(cornerRadius: LuTheme.radiusLarge).stroke(LuTheme.border))
        .padding(.horizontal, 16)
    }
}

struct MessageHeader: View {
    @Environment(AppModel.self) private var model
    let message: MessageSummary
    let parsed: ParsedMessage?
    let showsSubject: Bool
    var compact = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if showsSubject {
                Text(parsed?.subject ?? message.subject)
                    .font(.title2.weight(.semibold))
                    .foregroundStyle(LuTheme.textPrimary)
                    .textSelection(.enabled)
            }
            HStack(alignment: .top, spacing: 12) {
                AvatarView(name: message.from?.displayName ?? "?", providerId: model.account(id: message.accountId)?.providerId ?? "")
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 6) {
                        Text(message.from?.displayName ?? "(kein Absender)").font(.headline)
                        if let address = message.from?.address, message.from?.name != nil {
                            Text("<\(address)>").font(.callout).foregroundStyle(LuTheme.textSecondary)
                        }
                    }
                    .textSelection(.enabled)
                    if compact {
                        Text(parsed?.preview ?? message.subject).font(.caption).foregroundStyle(LuTheme.textSecondary).lineLimit(1)
                    } else {
                        let recipients = (parsed?.to ?? message.to).map(\.displayName).joined(separator: ", ")
                        if !recipients.isEmpty {
                            Text("An: \(recipients)").font(.caption).foregroundStyle(LuTheme.textSecondary).lineLimit(2)
                        }
                        if let account = model.account(id: message.accountId) {
                            HStack(spacing: 5) {
                                ProviderDot(providerId: account.providerId, size: 7)
                                Text(account.email).font(.caption).foregroundStyle(LuTheme.textMuted)
                                if let category = message.category, category != .primary {
                                    CategoryChip(category: category, rules: model.rules)
                                }
                            }
                        }
                    }
                }
                Spacer()
                Text(message.date.formatted(date: .abbreviated, time: .shortened))
                    .font(.caption)
                    .foregroundStyle(LuTheme.textSecondary)
            }
        }
        .padding(compact ? 14 : 20)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Inhalt einer Nachricht mit Warnungen, Abbestellen, Tracker-Bericht und Anhängen.
struct MessageBody: View {
    @Environment(AppModel.self) private var model
    @Environment(\.colorScheme) private var colorScheme
    let message: MessageSummary
    let fillsSpace: Bool
    @State private var confirmUnsubscribe = false

    var body: some View {
        if let parsed = model.parsed(message) {
            let allowRemote = model.alwaysLoadRemoteContent || model.allowRemoteContentFor.contains(message.id)
            let trackers = parsed.htmlBody.map { TrackerDetector.shared.trackers(in: $0) } ?? []
            let warnings = PhishingDetector.shared.warnings(from: parsed.from.first ?? message.from, html: parsed.htmlBody)
            VStack(spacing: 0) {
                ForEach(warnings, id: \.self) { warning in
                    InfoStrip(icon: "exclamationmark.shield.fill", tint: LuTheme.danger, text: "Vorsicht, möglicher Betrug: \(warning)")
                }
                if let method = message.unsubscribe {
                    InfoStrip(icon: "hand.raised", tint: LuTheme.accent, text: "Newsletter oder Werbung von \(message.from?.domain ?? "diesem Absender").") {
                        Button("Abbestellen") { confirmUnsubscribe = true }
                            .confirmationDialog("Diesen Newsletter abbestellen?", isPresented: $confirmUnsubscribe) {
                                Button("Abbestellen") { Task { await model.unsubscribe(message) } }
                                Button("Abbestellen und künftig als Werbung einsortieren") {
                                    Task { await model.unsubscribe(message) }
                                    model.setRule(for: message, category: .promotions, wholeDomain: false)
                                }
                            } message: {
                                switch method {
                                case .oneClick: Text("Lu-Mail meldet dich direkt beim Absender ab.")
                                case .mail(let address, _): Text("Lu-Mail sendet eine Abmelde-Mail an \(address).")
                                case .website: Text("Die Abmeldeseite des Absenders wird im Browser geöffnet.")
                                }
                            }
                    }
                }
                if parsed.htmlBody != nil && !allowRemote {
                    let trackerText = trackers.isEmpty ? "Externe Bilder wurden blockiert."
                        : "\(trackers.count) Tracker blockiert (\(trackers.prefix(3).joined(separator: ", "))\(trackers.count > 3 ? ", …" : ""))."
                    InfoStrip(icon: "shield.lefthalf.filled", tint: LuTheme.accent, text: trackerText) {
                        Button("Externe Inhalte laden") { model.allowRemoteContentFor.insert(message.id) }
                    }
                }
                if !parsed.visibleAttachments.isEmpty {
                    AttachmentBar(attachments: parsed.visibleAttachments)
                }
                MailWebView(html: MailHTML.prepare(html: parsed.htmlBody, text: parsed.textBody,
                                                    allowRemoteContent: allowRemote, darkMode: colorScheme == .dark)) { url in
                    if url.scheme == "mailto" {
                        model.newMessage(to: url.absoluteString.dropFirst("mailto:".count).split(separator: "?").first.map(String.init) ?? "")
                    } else {
                        NSWorkspace.shared.open(url)
                    }
                }
            }
        } else if let error = model.messageErrors[message.id] {
            ContentUnavailableView("Nachricht konnte nicht geladen werden", systemImage: "exclamationmark.triangle",
                                   description: Text(error))
        } else {
            ProgressView("Lade Nachricht …").frame(maxWidth: .infinity, maxHeight: fillsSpace ? .infinity : 200)
        }
    }
}

struct InfoStrip<Actions: View>: View {
    let icon: String
    let tint: Color
    let text: String
    let actions: () -> Actions

    init(icon: String, tint: Color, text: String, @ViewBuilder actions: @escaping () -> Actions) {
        self.icon = icon
        self.tint = tint
        self.text = text
        self.actions = actions
    }

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: icon).foregroundStyle(tint)
            Text(text).font(.callout).fixedSize(horizontal: false, vertical: true)
            Spacer()
            actions()
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 8)
        .background(tint.opacity(0.08))
    }
}

extension InfoStrip where Actions == EmptyView {
    init(icon: String, tint: Color, text: String) {
        self.init(icon: icon, tint: tint, text: text) { EmptyView() }
    }
}

struct MessageToolbar: ToolbarContent {
    let model: AppModel
    let message: MessageSummary

    var body: some ToolbarContent {
        ToolbarItemGroup {
            Button { model.reply(to: message, all: false) } label: { Label("Antworten", systemImage: "arrowshape.turn.up.left") }
                .disabled(model.parsed(message) == nil)
            Button { model.reply(to: message, all: true) } label: { Label("Allen antworten", systemImage: "arrowshape.turn.up.left.2") }
                .disabled(model.parsed(message) == nil)
            Button { model.forward(message) } label: { Label("Weiterleiten", systemImage: "arrowshape.turn.up.right") }
                .disabled(model.parsed(message) == nil)
            Button { Task { await model.move([message], to: .archive) } } label: { Label("Archivieren", systemImage: "archivebox") }
                .keyboardShortcut("e", modifiers: .command)
            Button { Task { await model.move([message], to: .trash) } } label: { Label("Löschen", systemImage: "trash") }
                .keyboardShortcut(.delete, modifiers: .command)
            Menu {
                ForEach(SnoozePreset.allCases, id: \.self) { preset in
                    Button(preset.title) { model.snooze([message], until: preset.date()) }
                }
            } label: {
                Label("Später erinnern", systemImage: "clock")
            }
            Menu {
                MessageContextMenu(messages: [message])
            } label: {
                Label("Mehr", systemImage: "ellipsis.circle")
            }
        }
    }
}

/// Mehrere Nachrichten ausgewählt.
struct BatchActionsView: View {
    @Environment(AppModel.self) private var model
    let messages: [MessageSummary]

    var body: some View {
        VStack(spacing: 16) {
            Image(systemName: "square.stack.3d.up").font(.system(size: 44)).foregroundStyle(LuTheme.accent)
            Text("\(messages.count) Nachrichten ausgewählt").font(.title2.weight(.semibold))
            HStack(spacing: 10) {
                Button { Task { await model.setFlag("\\Seen", add: true, on: messages) } } label: { Label("Gelesen", systemImage: "envelope.open") }
                Button { Task { await model.setFlag("\\Seen", add: false, on: messages) } } label: { Label("Ungelesen", systemImage: "envelope.badge") }
                Button { Task { await model.toggleFlagged(messages) } } label: { Label("Markieren", systemImage: "star") }
            }
            HStack(spacing: 10) {
                Button { Task { await model.move(messages, to: .archive) } } label: { Label("Archivieren", systemImage: "archivebox") }
                Button { Task { await model.move(messages, to: .spam) } } label: { Label("Spam", systemImage: "xmark.bin") }
                Button(role: .destructive) { Task { await model.move(messages, to: .trash) } } label: { Label("Löschen", systemImage: "trash") }
            }
            Menu("Später erinnern") {
                ForEach(SnoozePreset.allCases, id: \.self) { preset in
                    Button(preset.title) { model.snooze(messages, until: preset.date()) }
                }
            }
            .fixedSize()
        }
        .controlSize(.large)
    }
}

struct AvatarView: View {
    let name: String
    let providerId: String

    var body: some View {
        Text(String(name.trimmingCharacters(in: .punctuationCharacters).prefix(1)).uppercased())
            .font(.headline)
            .foregroundStyle(.white)
            .frame(width: 38, height: 38)
            .background(Circle().fill(LuTheme.provider(providerId).gradient))
    }
}

struct AttachmentBar: View {
    let attachments: [MailAttachment]

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(attachments) { attachment in
                    Button { save(attachment) } label: {
                        HStack(spacing: 6) {
                            Image(systemName: "paperclip")
                            Text(attachment.filename).lineLimit(1)
                            Text(ByteCountFormatter.string(fromByteCount: Int64(attachment.data.count), countStyle: .file))
                                .foregroundStyle(LuTheme.textSecondary)
                        }
                        .font(.caption)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 6)
                        .background(RoundedRectangle(cornerRadius: LuTheme.radiusSmall).fill(LuTheme.surfaceHover))
                    }
                    .buttonStyle(.plain)
                    .help("Anhang speichern")
                }
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 8)
        }
    }

    private func save(_ attachment: MailAttachment) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = attachment.filename
        if let type = UTType(mimeType: attachment.mimeType) { panel.allowedContentTypes = [type] }
        guard panel.runModal() == .OK, let url = panel.url else { return }
        try? attachment.data.write(to: url)
    }
}

/// Zeigt HTML ohne JavaScript; Links gehen an `onLink` (Browser bzw. Verfassen bei mailto:).
struct MailWebView: NSViewRepresentable {
    let html: String
    var onLink: (URL) -> Void = { NSWorkspace.shared.open($0) }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.defaultWebpagePreferences.allowsContentJavaScript = false
        configuration.websiteDataStore = .nonPersistent()
        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = context.coordinator
        webView.setValue(false, forKey: "drawsBackground")
        return webView
    }

    func updateNSView(_ webView: WKWebView, context: Context) {
        context.coordinator.onLink = onLink
        guard context.coordinator.lastHTML != html else { return }
        context.coordinator.lastHTML = html
        webView.loadHTMLString(html, baseURL: nil)
    }

    final class Coordinator: NSObject, WKNavigationDelegate {
        var lastHTML: String?
        var onLink: (URL) -> Void = { _ in }

        @MainActor
        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction) async -> WKNavigationActionPolicy {
            if navigationAction.navigationType == .linkActivated, let url = navigationAction.request.url {
                onLink(url)
                return .cancel
            }
            // Nur das eigene HTML (about:blank) laden, keine Weiterleitungen.
            let scheme = navigationAction.request.url?.scheme ?? "about"
            return scheme == "about" || scheme == "data" ? .allow : .cancel
        }
    }
}
