import LuMailCore
import SwiftUI
import UniformTypeIdentifiers

struct ComposeView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State var draft: ComposeDraft
    @State private var showCcBcc = false
    @State private var isSending = false
    @State private var errorMessage: String?
    @State private var showFileImporter = false
    @State private var showSchedule = false
    @State private var scheduleDate = Calendar.current.date(byAdding: .hour, value: 1, to: Date()) ?? Date()
    @State private var isDropTarget = false
    @FocusState private var focusedField: Field?

    enum Field: Hashable { case to, cc, bcc, subject, body }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Picker("Von", selection: $draft.accountId) {
                    ForEach(model.accountsByProvider, id: \.provider.id) { group in
                        Section(group.provider.name) {
                            ForEach(group.accounts) { account in
                                Text(account.email).tag(Optional(account.id))
                            }
                        }
                    }
                }
                AddressField(title: "An", text: $draft.to, focus: $focusedField, field: .to)
                if showCcBcc || !draft.cc.isEmpty || !draft.bcc.isEmpty {
                    AddressField(title: "Cc", text: $draft.cc, focus: $focusedField, field: .cc)
                    AddressField(title: "Bcc", text: $draft.bcc, focus: $focusedField, field: .bcc)
                }
                TextField("Betreff", text: $draft.subject)
                    .focused($focusedField, equals: .subject)
            }
            .formStyle(.grouped)
            .scrollDisabled(true)
            .frame(height: showCcBcc || !draft.cc.isEmpty || !draft.bcc.isEmpty ? 250 : 180)

            TextEditor(text: $draft.body)
                .font(.body)
                .focused($focusedField, equals: .body)
                .scrollContentBackground(.hidden)
                .padding(12)
                .background(LuTheme.background)
                .overlay(RoundedRectangle(cornerRadius: LuTheme.radiusMedium)
                    .stroke(isDropTarget ? LuTheme.accent : LuTheme.border, lineWidth: isDropTarget ? 2 : 1))
                .padding(.horizontal, 20)
                .onDrop(of: [.fileURL], isTargeted: $isDropTarget) { providers in
                    addDropped(providers)
                    return true
                }

            if !draft.attachments.isEmpty {
                attachmentList
            }

            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(LuTheme.danger)
                    .font(.callout)
                    .padding(.horizontal, 20)
                    .padding(.top, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            HStack {
                Button(showCcBcc ? "Cc/Bcc ausblenden" : "Cc/Bcc") { showCcBcc.toggle() }
                    .buttonStyle(.link)
                Button {
                    showFileImporter = true
                } label: {
                    Label("Anhang", systemImage: "paperclip")
                }
                .help("Datei anhängen (oder per Drag & Drop in den Text ziehen)")
                Spacer()
                Button("Verwerfen") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button {
                    showSchedule = true
                } label: {
                    Label("Später senden", systemImage: "clock")
                }
                .disabled(!canSend)
                .popover(isPresented: $showSchedule) { schedulePopover }
                Button {
                    send(at: nil)
                } label: {
                    if isSending {
                        ProgressView().controlSize(.small)
                    } else {
                        Label("Senden", systemImage: "paperplane.fill")
                    }
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(!canSend)
            }
            .padding(20)
        }
        .frame(width: 680, height: 640)
        .background(LuTheme.surface)
        .fileImporter(isPresented: $showFileImporter, allowedContentTypes: [.item], allowsMultipleSelection: true) { result in
            if case .success(let urls) = result { urls.forEach(addFile) }
        }
        .onAppear { focusedField = draft.to.isEmpty ? .to : .body }
    }

    private var canSend: Bool {
        !isSending && !draft.to.trimmingCharacters(in: .whitespaces).isEmpty && draft.accountId != nil
    }

    private var attachmentList: some View {
        let total = draft.attachments.reduce(0) { $0 + $1.data.count }
        return VStack(alignment: .leading, spacing: 6) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(draft.attachments) { attachment in
                        HStack(spacing: 6) {
                            Image(systemName: "doc")
                            Text(attachment.filename).lineLimit(1)
                            Text(ByteCountFormatter.string(fromByteCount: Int64(attachment.data.count), countStyle: .file))
                                .foregroundStyle(LuTheme.textSecondary)
                            Button {
                                draft.attachments.removeAll { $0.id == attachment.id }
                            } label: {
                                Image(systemName: "xmark.circle.fill")
                            }
                            .buttonStyle(.plain)
                            .foregroundStyle(LuTheme.textMuted)
                        }
                        .font(.caption)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 6)
                        .background(RoundedRectangle(cornerRadius: LuTheme.radiusSmall).fill(LuTheme.surfaceHover))
                    }
                }
            }
            if total > OutgoingAttachment.warningBytes {
                Label("Die Anhänge sind zusammen \(ByteCountFormatter.string(fromByteCount: Int64(total), countStyle: .file)) groß – viele Anbieter lehnen über 25 MB ab.",
                      systemImage: "exclamationmark.triangle")
                    .font(.caption)
                    .foregroundStyle(LuTheme.moon)
            }
        }
        .padding(.horizontal, 20)
        .padding(.top, 8)
    }

    private var schedulePopover: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Später senden").font(.headline)
            DatePicker("Zeitpunkt", selection: $scheduleDate, in: Date()..., displayedComponents: [.date, .hourAndMinute])
                .datePickerStyle(.graphical)
            Text("Lu-Mail muss zu diesem Zeitpunkt laufen – sonst wird die Nachricht beim nächsten Start gesendet.")
                .font(.caption)
                .foregroundStyle(LuTheme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button("Planen") {
                    showSchedule = false
                    send(at: scheduleDate)
                }
                .buttonStyle(.borderedProminent)
            }
        }
        .padding(16)
        .frame(width: 320)
    }

    private func addFile(_ url: URL) {
        let accessing = url.startAccessingSecurityScopedResource()
        defer { if accessing { url.stopAccessingSecurityScopedResource() } }
        guard let data = try? Data(contentsOf: url) else {
            errorMessage = "„\(url.lastPathComponent)“ konnte nicht gelesen werden."
            return
        }
        let type = UTType(filenameExtension: url.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
        draft.attachments.append(OutgoingAttachment(filename: url.lastPathComponent, mimeType: type, data: data))
    }

    private func addDropped(_ providers: [NSItemProvider]) {
        for provider in providers {
            _ = provider.loadObject(ofClass: URL.self) { url, _ in
                guard let url else { return }
                Task { @MainActor in addFile(url) }
            }
        }
    }

    private func send(at date: Date?) {
        isSending = true
        errorMessage = nil
        Task {
            do {
                try await model.send(draft, at: date)
                dismiss()
            } catch {
                errorMessage = AppModel.describe(error)
            }
            isSending = false
        }
    }
}

/// Adressfeld mit Vorschlägen aus den Kontakten.
struct AddressField: View {
    @Environment(AppModel.self) private var model
    let title: String
    @Binding var text: String
    var focus: FocusState<ComposeView.Field?>.Binding
    let field: ComposeView.Field

    var body: some View {
        let suggestions = focus.wrappedValue == field ? model.contactSuggestions(for: text) : []
        VStack(alignment: .leading, spacing: 4) {
            TextField(title, text: $text, prompt: Text("name@beispiel.de, …"))
                .focused(focus, equals: field)
            if !suggestions.isEmpty && !text.hasSuffix(", ") && !text.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 6) {
                        ForEach(suggestions, id: \.address) { contact in
                            Button {
                                accept(contact)
                            } label: {
                                Text(contact.name.map { "\($0) <\(contact.address)>" } ?? contact.address)
                                    .font(.caption)
                                    .padding(.horizontal, 8)
                                    .padding(.vertical, 4)
                                    .background(Capsule().fill(LuTheme.accent.opacity(0.12)))
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
        }
    }

    private func accept(_ contact: MailAddress) {
        var parts = text.split(separator: ",", omittingEmptySubsequences: false).map { $0.trimmingCharacters(in: .whitespaces) }
        if !parts.isEmpty { parts.removeLast() }
        parts.append(contact.address)
        text = parts.filter { !$0.isEmpty }.joined(separator: ", ") + ", "
    }
}
