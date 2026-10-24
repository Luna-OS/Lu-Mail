import LuMailCore
import SwiftUI

struct MessageListView: View {
    @Environment(AppModel.self) private var model

    private var showsAccountInfo: Bool {
        switch model.selection {
        case .allInboxes, .unread, .flagged: return true
        default: return false
        }
    }

    var body: some View {
        @Bindable var model = model
        let messages = model.list(for: model.selection)
        Group {
            if messages.isEmpty {
                ContentUnavailableView {
                    Label(model.searchText.isEmpty ? "Keine Nachrichten" : "Keine Treffer", systemImage: "tray")
                } description: {
                    Text(model.searchText.isEmpty ? "Hier ist gerade alles erledigt. 🌙" : "Für „\(model.searchText)“ wurde nichts gefunden.")
                }
            } else {
                List(messages, selection: $model.selectedMessageID) { message in
                    MessageRow(message: message, showsAccountInfo: showsAccountInfo)
                        .tag(message.id)
                        .contextMenu { MessageContextMenu(message: message) }
                }
                .listStyle(.inset)
                .scrollContentBackground(.hidden)
            }
        }
        .background(LuTheme.background)
        .navigationTitle(model.title)
    }
}

struct MessageRow: View {
    @Environment(AppModel.self) private var model
    let message: MessageSummary
    let showsAccountInfo: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            // Farbleiste des Anbieters
            if let account = model.account(id: message.accountId) {
                RoundedRectangle(cornerRadius: 2)
                    .fill(LuTheme.provider(account.providerId))
                    .frame(width: 3)
                    .opacity(showsAccountInfo ? 1 : 0.35)
            }
            Circle()
                .fill(message.isSeen ? Color.clear : LuTheme.unreadDot)
                .frame(width: 8, height: 8)
                .padding(.top, 5)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline) {
                    Text(message.from?.displayName ?? "(kein Absender)")
                        .font(.body.weight(message.isSeen ? .regular : .semibold))
                        .foregroundStyle(LuTheme.textPrimary)
                        .lineLimit(1)
                    Spacer(minLength: 6)
                    if message.isFlagged {
                        Image(systemName: "star.fill").font(.caption).foregroundStyle(LuTheme.moon)
                    }
                    Text(Self.format(message.date))
                        .font(.caption)
                        .foregroundStyle(LuTheme.textSecondary)
                        .monospacedDigit()
                }
                Text(message.subject.isEmpty ? "(kein Betreff)" : message.subject)
                    .font(.callout.weight(message.isSeen ? .regular : .medium))
                    .foregroundStyle(message.isSeen ? LuTheme.textSecondary : LuTheme.textPrimary)
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
                            CategoryChip(category: category)
                        }
                    }
                }
            }
        }
        .padding(.vertical, 4)
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

/// Rechtsklick-Menü einer Nachricht (auch im Lesebereich genutzt).
struct MessageContextMenu: View {
    @Environment(AppModel.self) private var model
    let message: MessageSummary

    var body: some View {
        Button(message.isSeen ? "Als ungelesen markieren" : "Als gelesen markieren") {
            Task { await model.toggleRead(message) }
        }
        Button(message.isFlagged ? "Markierung entfernen" : "Markieren") {
            Task { await model.toggleFlagged(message) }
        }
        Divider()
        Button("Archivieren") { Task { await model.move(message, to: .archive) } }
        Button("Als Spam markieren") { Task { await model.move(message, to: .spam) } }
        Button("Löschen", role: .destructive) { Task { await model.move(message, to: .trash) } }
        if let from = message.from {
            Divider()
            Menu("Absender immer einsortieren als …") {
                ForEach(MailCategory.allCases, id: \.self) { category in
                    Button(category.title) { model.setRule(for: message, category: category, wholeDomain: false) }
                }
            }
            Menu("Alle von \(from.domain) einsortieren als …") {
                ForEach(MailCategory.allCases, id: \.self) { category in
                    Button(category.title) { model.setRule(for: message, category: category, wholeDomain: true) }
                }
            }
        }
    }
}
