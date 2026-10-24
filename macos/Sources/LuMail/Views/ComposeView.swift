import LuMailCore
import SwiftUI

struct ComposeView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State var draft: ComposeDraft
    @State private var showCcBcc = false
    @State private var isSending = false
    @State private var errorMessage: String?

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
                TextField("An", text: $draft.to, prompt: Text("name@beispiel.de, …"))
                if showCcBcc || !draft.cc.isEmpty {
                    TextField("Cc", text: $draft.cc)
                    TextField("Bcc", text: $draft.bcc)
                }
                TextField("Betreff", text: $draft.subject)
            }
            .formStyle(.grouped)
            .scrollDisabled(true)
            .frame(height: showCcBcc || !draft.cc.isEmpty ? 250 : 180)

            TextEditor(text: $draft.body)
                .font(.body)
                .scrollContentBackground(.hidden)
                .padding(12)
                .background(LuTheme.background)
                .overlay(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).stroke(LuTheme.border))
                .padding(.horizontal, 20)

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
                Spacer()
                Button("Verwerfen") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button {
                    send()
                } label: {
                    if isSending {
                        ProgressView().controlSize(.small)
                    } else {
                        Label("Senden", systemImage: "paperplane.fill")
                    }
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(isSending || draft.to.trimmingCharacters(in: .whitespaces).isEmpty || draft.accountId == nil)
            }
            .padding(20)
        }
        .frame(width: 640, height: 600)
        .background(LuTheme.surface)
    }

    private func send() {
        isSending = true
        errorMessage = nil
        Task {
            do {
                try await model.send(draft)
                dismiss()
            } catch {
                errorMessage = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
            }
            isSending = false
        }
    }
}
