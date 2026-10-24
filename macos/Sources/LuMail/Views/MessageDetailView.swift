import AppKit
import LuMailCore
import SwiftUI
import UniformTypeIdentifiers
import WebKit

struct MessageDetailView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        Group {
            if let message = model.selectedMessage {
                VStack(spacing: 0) {
                    header(message)
                    Divider().overlay(LuTheme.border)
                    content(message)
                }
                .toolbar { toolbar(message) }
            } else {
                ContentUnavailableView {
                    Label("Keine Nachricht ausgewählt", systemImage: "envelope.open")
                } description: {
                    Text("Wähle links eine Nachricht aus.")
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(LuTheme.surface)
    }

    private func header(_ message: MessageSummary) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(model.openedMessage?.subject ?? message.subject)
                .font(.title2.weight(.semibold))
                .foregroundStyle(LuTheme.textPrimary)
                .textSelection(.enabled)
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
                    let recipients = (model.openedMessage?.to ?? message.to).map(\.displayName).joined(separator: ", ")
                    if !recipients.isEmpty {
                        Text("An: \(recipients)").font(.caption).foregroundStyle(LuTheme.textSecondary).lineLimit(2)
                    }
                    if let account = model.account(id: message.accountId) {
                        HStack(spacing: 5) {
                            ProviderDot(providerId: account.providerId, size: 7)
                            Text(account.email).font(.caption).foregroundStyle(LuTheme.textMuted)
                            if let category = message.category, category != .primary { CategoryChip(category: category) }
                        }
                    }
                }
                Spacer()
                Text(message.date.formatted(date: .abbreviated, time: .shortened))
                    .font(.caption)
                    .foregroundStyle(LuTheme.textSecondary)
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    @ViewBuilder
    private func content(_ message: MessageSummary) -> some View {
        if let parsed = model.openedMessage {
            let allowRemote = model.alwaysLoadRemoteContent || model.allowRemoteContentFor.contains(message.id)
            VStack(spacing: 0) {
                if parsed.htmlBody != nil && !allowRemote {
                    HStack {
                        Image(systemName: "shield.lefthalf.filled").foregroundStyle(LuTheme.accent)
                        Text("Externe Bilder und Tracker wurden blockiert.").font(.callout)
                        Spacer()
                        Button("Externe Inhalte laden") { model.allowRemoteContentFor.insert(message.id) }
                    }
                    .padding(.horizontal, 20)
                    .padding(.vertical, 8)
                    .background(LuTheme.accent.opacity(0.08))
                }
                if !parsed.visibleAttachments.isEmpty {
                    AttachmentBar(attachments: parsed.visibleAttachments)
                }
                MailWebView(html: MailHTML.prepare(html: parsed.htmlBody, text: parsed.textBody,
                                                    allowRemoteContent: allowRemote, darkMode: colorScheme == .dark))
            }
        } else if let error = model.openedMessageError {
            ContentUnavailableView("Nachricht konnte nicht geladen werden", systemImage: "exclamationmark.triangle",
                                   description: Text(error))
        } else {
            ProgressView("Lade Nachricht …").frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    @ToolbarContentBuilder
    private func toolbar(_ message: MessageSummary) -> some ToolbarContent {
        ToolbarItemGroup {
            Button { model.reply(to: message, all: false) } label: { Label("Antworten", systemImage: "arrowshape.turn.up.left") }
                .disabled(model.openedMessage == nil)
            Button { model.reply(to: message, all: true) } label: { Label("Allen antworten", systemImage: "arrowshape.turn.up.left.2") }
                .disabled(model.openedMessage == nil)
            Button { model.forward(message) } label: { Label("Weiterleiten", systemImage: "arrowshape.turn.up.right") }
                .disabled(model.openedMessage == nil)
            Button { Task { await model.move(message, to: .archive) } } label: { Label("Archivieren", systemImage: "archivebox") }
                .keyboardShortcut("e", modifiers: .command)
            Button { Task { await model.move(message, to: .spam) } } label: { Label("Spam", systemImage: "xmark.bin") }
            Button { Task { await model.move(message, to: .trash) } } label: { Label("Löschen", systemImage: "trash") }
                .keyboardShortcut(.delete, modifiers: .command)
            Button { Task { await model.toggleFlagged(message) } } label: {
                Label(message.isFlagged ? "Markierung entfernen" : "Markieren", systemImage: message.isFlagged ? "star.fill" : "star")
            }
            Menu {
                MessageContextMenu(message: message)
            } label: {
                Label("Mehr", systemImage: "ellipsis.circle")
            }
        }
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

/// Zeigt HTML ohne JavaScript; Links öffnen im Standardbrowser.
struct MailWebView: NSViewRepresentable {
    let html: String

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
        guard context.coordinator.lastHTML != html else { return }
        context.coordinator.lastHTML = html
        webView.loadHTMLString(html, baseURL: nil)
    }

    final class Coordinator: NSObject, WKNavigationDelegate {
        var lastHTML: String?

        @MainActor
        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction) async -> WKNavigationActionPolicy {
            if navigationAction.navigationType == .linkActivated, let url = navigationAction.request.url {
                NSWorkspace.shared.open(url)
                return .cancel
            }
            // Nur das eigene HTML (about:blank) laden, keine Weiterleitungen.
            let scheme = navigationAction.request.url?.scheme ?? "about"
            return scheme == "about" || scheme == "data" ? .allow : .cancel
        }
    }
}
