import LuMailCore
import SwiftUI

struct MessageListView: View {
    @Environment(AppModel.self) private var model
    @FocusState private var searchFocused: Bool

    private var showsAccountInfo: Bool {
        switch model.selection {
        case .allInboxes, .unread, .flagged, .reminders, .providerInbox: return true
        default: return false
        }
    }

    var body: some View {
        Group {
            if model.selection == .scheduled {
                ScheduledListView()
            } else {
                messageList
            }
        }
        .background {
            ZStack {
                NightSkyLayer()
                LuTheme.listPane
            }
            .ignoresSafeArea()
        }
        .navigationTitle(model.title)
        .modifier(SearchFocusModifier(focused: $searchFocused))
    }

    private var messageList: some View {
        @Bindable var model = model
        let messages = model.list(for: model.selection)
        let conversations = model.conversationMode ? ConversationBuilder.group(messages) : []
        let orderedIDs = model.conversationMode ? conversations.map(\.latest.id) : messages.map(\.id)
        return VStack(spacing: 0) {
            ListHeader(count: model.conversationMode ? conversations.count : messages.count)
            if messages.isEmpty && model.serverResults.isEmpty {
                ContentUnavailableView {
                    Label(model.searchText.isEmpty ? "Keine Nachrichten" : "Keine Treffer", systemImage: "tray")
                } description: {
                    Text(model.searchText.isEmpty ? "Hier ist gerade alles erledigt. 🌙"
                                                  : "Für „\(model.searchText)“ wurde lokal nichts gefunden – Enter sucht auf dem Server.")
                }
                .frame(maxHeight: .infinity)
            } else {
                List(selection: $model.selectedMessageIDs) {
                    if model.conversationMode {
                        ForEach(conversations) { conversation in
                            MessageRow(message: conversation.latest, showsAccountInfo: showsAccountInfo, conversation: conversation)
                                .tag(conversation.latest.id)
                        }
                    } else {
                        ForEach(messages) { message in
                            MessageRow(message: message, showsAccountInfo: showsAccountInfo)
                                .tag(message.id)
                        }
                    }
                    if model.isSearchingServer || !model.serverResults.isEmpty {
                        Section {
                            if model.isSearchingServer {
                                HStack { ProgressView().controlSize(.small); Text("Suche auf dem Server …") }
                            }
                            ForEach(model.serverResults) { message in
                                MessageRow(message: message, showsAccountInfo: true)
                                    .tag(message.id)
                            }
                        } header: {
                            Text("Weitere Treffer auf dem Server")
                        }
                    }
                }
                .listStyle(.inset)
                .scrollContentBackground(.hidden)
                .contextMenu(forSelectionType: MessageSummary.ID.self) { ids in
                    let selected = model.messages(for: ids)
                    if !selected.isEmpty { MessageContextMenu(messages: selected) }
                }
                .onKeyPress(phases: .down) { press in
                    handleKey(press, orderedIDs: orderedIDs) ? .handled : .ignored
                }
            }
        }
    }

    /// Tastenkürzel wie in Gmail (docs/FUNKTIONEN.md §6).
    private func handleKey(_ press: KeyPress, orderedIDs: [MessageSummary.ID]) -> Bool {
        guard press.modifiers.isEmpty || press.modifiers == .shift else { return false }
        let selected = model.selectedMessages
        switch press.characters {
        case "j", "k":
            guard !orderedIDs.isEmpty else { return true }
            let current = orderedIDs.firstIndex { model.selectedMessageIDs.contains($0) }
            let next: Int
            if let current { next = press.characters == "j" ? min(current + 1, orderedIDs.count - 1) : max(current - 1, 0) } else { next = 0 }
            model.selectedMessageIDs = [orderedIDs[next]]
        case "e": Task { await model.move(selected, to: .archive) }
        case "#": Task { await model.move(selected, to: .trash) }
        case "!": Task { await model.move(selected, to: .spam) }
        case "s": Task { await model.toggleFlagged(selected) }
        case "u": Task { await model.toggleRead(selected) }
        case "r": if let message = model.selectedMessage { model.reply(to: message, all: false) }
        case "a": if let message = model.selectedMessage { model.reply(to: message, all: true) }
        case "f": if let message = model.selectedMessage { model.forward(message) }
        case "c": model.newMessage()
        case "/": searchFocused = true
        default: return false
        }
        return true
    }
}

/// Fokussiert das Suchfeld mit „/“ (erst ab macOS 15 möglich).
struct SearchFocusModifier: ViewModifier {
    var focused: FocusState<Bool>.Binding

    func body(content: Content) -> some View {
        if #available(macOS 15.0, *) {
            content.searchFocused(focused)
        } else {
            content
        }
    }
}

/// Kopfzeile der Liste mit Anzahl und Umschalter für Konversationen.
struct ListHeader: View {
    @Environment(AppModel.self) private var model
    let count: Int

    var body: some View {
        @Bindable var model = model
        HStack {
            Text(count == 1 ? "1 Eintrag" : "\(count) Einträge")
                .font(.caption)
                .foregroundStyle(LuTheme.textSecondary)
            Spacer()
            Toggle(isOn: $model.conversationMode) {
                Label("Konversationen", systemImage: "bubble.left.and.bubble.right")
            }
            .toggleStyle(.button)
            .controlSize(.small)
            .help("Nachrichten als Konversationen gruppieren")
            .onChange(of: model.conversationMode) { _, _ in
                model.selectedMessageIDs = []
                model.saveSettings()
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 6)
    }
}

struct MessageRow: View {
    @Environment(AppModel.self) private var model
    let message: MessageSummary
    let showsAccountInfo: Bool
    var conversation: Conversation?

    private var isUnread: Bool { conversation?.hasUnread ?? !message.isSeen }

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            if let account = model.account(id: message.accountId) {
                RoundedRectangle(cornerRadius: 2)
                    .fill(LuTheme.provider(account.providerId))
                    .frame(width: 3)
                    .opacity(showsAccountInfo ? 1 : 0.35)
            }
            Circle()
                .fill(isUnread ? LuTheme.unreadDot : Color.clear)
                .frame(width: 8, height: 8)
                .padding(.top, 5)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline) {
                    Text(senderLine)
                        .font(.body.weight(isUnread ? .semibold : .regular))
                        .foregroundStyle(LuTheme.textPrimary)
                        .lineLimit(1)
                    if let conversation, conversation.count > 1 {
                        Text("\(conversation.count)")
                            .font(.caption2.weight(.semibold))
                            .padding(.horizontal, 5)
                            .background(Capsule().fill(LuTheme.surfaceHover))
                            .foregroundStyle(LuTheme.textSecondary)
                    }
                    Spacer(minLength: 6)
                    if !message.attachmentHint.isEmpty {
                        Image(systemName: "paperclip").font(.caption).foregroundStyle(LuTheme.textMuted)
                    }
                    if message.isFlagged {
                        Image(systemName: "star.fill").font(.caption).foregroundStyle(LuTheme.moon)
                    }
                    Text(Self.format(message.date))
                        .font(.caption)
                        .foregroundStyle(LuTheme.textSecondary)
                        .monospacedDigit()
                }
                Text(message.subject.isEmpty ? "(kein Betreff)" : message.subject)
                    .font(.callout.weight(isUnread ? .medium : .regular))
                    .foregroundStyle(isUnread ? LuTheme.textPrimary : LuTheme.textSecondary)
                    .lineLimit(1)
                if showsAccountInfo || (message.category != nil && message.category != .primary) {
                    HStack(spacing: 6) {
                        if showsAccountInfo, let account = model.account(id: message.accountId) {
                            Text(account.email)
                                .font(.caption2)
                                .foregroundStyle(LuTheme.textMuted)
                                .lineLimit(1)
                        }
                        if let category = message.category, category != .primary {
                            CategoryChip(category: category, rules: model.rules)
                        }
                    }
                }
            }
        }
        .padding(.vertical, 4)
    }

    private var senderLine: String {
        if let conversation, conversation.count > 1 {
            return conversation.participants.suffix(3).joined(separator: ", ")
        }
        return message.from?.displayName ?? "(kein Absender)"
    }

    static func format(_ date: Date) -> String {
        let calendar = Calendar.current
        if calendar.isDateInToday(date) { return date.formatted(date: .omitted, time: .shortened) }
        if calendar.isDateInYesterday(date) { return "Gestern" }
        if let days = calendar.dateComponents([.day], from: date, to: Date()).day, days < 7 {
            return date.formatted(.dateTime.weekday(.wide))
        }
        return date.formatted(date: .numeric, time: .omitted)
    }
}

private extension MessageSummary {
    /// Grober Hinweis auf Anhänge (Größe > 100 KB) – genaue Anhänge zeigt der Lesebereich.
    var attachmentHint: String { size > 100_000 ? "📎" : "" }
}

/// Rechtsklick-Menü für eine oder mehrere Nachrichten (auch im Lesebereich genutzt).
struct MessageContextMenu: View {
    @Environment(AppModel.self) private var model
    let messages: [MessageSummary]

    var body: some View {
        let anyUnread = messages.contains { !$0.isSeen }
        let anyUnflagged = messages.contains { !$0.isFlagged }
        Button(anyUnread ? "Als gelesen markieren" : "Als ungelesen markieren") {
            Task { await model.toggleRead(messages) }
        }
        Button(anyUnflagged ? "Markieren" : "Markierung entfernen") {
            Task { await model.toggleFlagged(messages) }
        }
        Menu("Später erinnern") {
            ForEach(SnoozePreset.allCases, id: \.self) { preset in
                Button(preset.title) { model.snooze(messages, until: preset.date()) }
            }
        }
        Divider()
        Button("Archivieren") { Task { await model.move(messages, to: .archive) } }
        Button("Als Spam markieren") { Task { await model.move(messages, to: .spam) } }
        Button("Löschen", role: .destructive) { Task { await model.move(messages, to: .trash) } }
        if messages.count == 1, let message = messages.first, let from = message.from {
            Divider()
            Menu("Absender immer einsortieren als …") {
                ForEach(model.allCategories, id: \.self) { category in
                    Button(category.title(model.rules)) { model.setRule(for: message, category: category, wholeDomain: false) }
                }
            }
            Menu("Alle von \(from.domain) einsortieren als …") {
                ForEach(model.allCategories, id: \.self) { category in
                    Button(category.title(model.rules)) { model.setRule(for: message, category: category, wholeDomain: true) }
                }
            }
            if model.snoozed.contains(where: { $0.matches(message) }) {
                Button("Erinnerung entfernen") { model.unsnooze(message) }
            }
        }
    }
}

/// Übersicht → Geplant.
struct ScheduledListView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        if model.scheduled.isEmpty {
            ContentUnavailableView("Keine geplanten Nachrichten", systemImage: "paperplane.circle",
                                   description: Text("Im Verfassen-Fenster kannst du „Später senden“ wählen."))
        } else {
            List(model.scheduled) { entry in
                HStack(alignment: .top) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(entry.subject.isEmpty ? "(kein Betreff)" : entry.subject).font(.headline)
                        Text("An: \(entry.to.joined(separator: ", "))").font(.caption).foregroundStyle(LuTheme.textSecondary)
                        Text("Von: \(model.account(id: entry.accountId)?.email ?? "?")").font(.caption).foregroundStyle(LuTheme.textMuted)
                    }
                    Spacer()
                    VStack(alignment: .trailing, spacing: 6) {
                        Text(entry.sendAt.formatted(date: .abbreviated, time: .shortened)).font(.caption).monospacedDigit()
                        HStack {
                            Button("Jetzt senden") { Task { await model.sendScheduled(entry) } }
                            Button("Löschen", role: .destructive) { model.deleteScheduled(entry) }
                        }
                        .controlSize(.small)
                    }
                }
                .padding(.vertical, 4)
            }
            .listStyle(.inset)
            .scrollContentBackground(.hidden)
        }
    }
}
