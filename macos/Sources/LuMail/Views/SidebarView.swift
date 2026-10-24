import LuMailCore
import SwiftUI

/// Übersicht → Anbieter → Konto → Kategorien und Ordner.
struct SidebarView: View {
    @Environment(AppModel.self) private var model
    @State private var collapsedAccounts: Set<UUID> = []

    var body: some View {
        @Bindable var model = model
        List(selection: $model.selection) {
            Section {
                row("Alle Posteingänge", systemImage: "tray.2", item: .allInboxes)
                row("Ungelesen", systemImage: "envelope.badge", item: .unread)
                row("Markiert", systemImage: "star", item: .flagged, showsUnread: false)
                if !model.snoozed.isEmpty {
                    row("Erinnerungen", systemImage: "clock", item: .reminders, showsUnread: false, count: model.snoozed.count)
                }
                if !model.scheduled.isEmpty {
                    row("Geplant", systemImage: "paperplane.circle", item: .scheduled, showsUnread: false, count: model.scheduled.count)
                }
            } header: {
                HStack(spacing: 6) {
                    MoonMark(size: 12)
                    Text("Übersicht")
                }
            }

            ForEach(model.accountsByProvider, id: \.provider.id) { group in
                Section {
                    if group.accounts.count > 1 {
                        row("Alle \(group.provider.name)-Posteingänge", systemImage: "tray.2", item: .providerInbox(group.provider.id),
                            tint: LuTheme.provider(group.provider.id))
                    }
                    ForEach(group.accounts) { account in
                        accountSection(account)
                    }
                } header: {
                    HStack(spacing: 6) {
                        ProviderDot(providerId: group.provider.id)
                        Text(group.provider.name)
                    }
                }
            }
        }
        .listStyle(.sidebar)
        .scrollContentBackground(.hidden)
        .background(LuTheme.sidebar)
        .safeAreaInset(edge: .bottom) {
            Button {
                model.showAddAccount = true
            } label: {
                Label("Konto hinzufügen", systemImage: "plus.circle")
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .buttonStyle(.plain)
            .foregroundStyle(LuTheme.accent)
            .padding(12)
            .background(LuTheme.sidebar)
        }
    }

    @ViewBuilder
    private func accountSection(_ account: Account) -> some View {
        let expanded = Binding(
            get: { !collapsedAccounts.contains(account.id) },
            set: { isExpanded in
                if isExpanded { collapsedAccounts.remove(account.id) } else { collapsedAccounts.insert(account.id) }
            })
        DisclosureGroup(isExpanded: expanded) {
            if account.isWeb {
                webFolders(account)
            } else if model.separateCategories {
                ForEach(model.allCategories, id: \.self) { category in
                    row(category.title(model.rules), systemImage: category.systemImage(model.rules), item: .category(account.id, category),
                        tint: category == .primary ? nil : LuTheme.category(category, rules: model.rules))
                }
            } else {
                row("Posteingang", systemImage: "tray", item: .category(account.id, .primary))
            }
            ForEach(model.sidebarFolders(of: account.id)) { folder in
                row(folder.displayName, systemImage: folder.role?.systemImage ?? "folder",
                    item: .folder(account.id, folder.path), showsUnread: model.unreadCounts[account.id]?[folder.path] != nil)
            }
        } label: {
            HStack(spacing: 6) {
                VStack(alignment: .leading, spacing: 1) {
                    Text(account.label)
                        .font(.callout.weight(.medium))
                        .lineLimit(1)
                        .truncationMode(.middle)
                    if let error = model.accountErrors[account.id] {
                        Text(error)
                            .font(.caption2)
                            .foregroundStyle(LuTheme.danger)
                            .lineLimit(2)
                    }
                }
                Spacer(minLength: 4)
                if model.loadingAccounts.contains(account.id) {
                    ProgressView().controlSize(.small)
                } else if model.accountErrors[account.id] != nil {
                    Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(LuTheme.danger)
                } else if collapsedAccounts.contains(account.id) {
                    UnreadBadge(count: model.unreadCount(account: account))
                }
            }
            .contextMenu {
                if account.isWeb {
                    Button("Neu laden") { model.webMail.reload(account.id) }
                    Button("Erneut anmelden …") { model.reloginWeb(account) }
                } else {
                    Button("Aktualisieren") { Task { await model.refresh(account) } }
                    Button("Neu anmelden (Browser) …") { model.showAddAccount = true }
                }
                Divider()
                Button("Konto entfernen", role: .destructive) { model.removeAccount(account) }
            }
        }
    }

    /// Ordner eines Web-Kontos – Links ins Webmail (§20).
    @ViewBuilder
    private func webFolders(_ account: Account) -> some View {
        let folders = model.provider(for: account)?.web?.sidebarFolders(separateCategories: model.separateCategories) ?? []
        ForEach(folders) { folder in
            let category = MailCategory(rawValue: folder.key)
            if folder.isCategory {
                row(folder.title, systemImage: category.systemImage(model.rules), item: .web(account.id, folder.key),
                    tint: category == .primary ? nil : LuTheme.category(category, rules: model.rules))
            } else {
                row(folder.title, systemImage: FolderRole(rawValue: folder.key)?.systemImage ?? "folder",
                    item: .web(account.id, folder.key), showsUnread: false)
            }
        }
    }

    private func row(_ title: String, systemImage: String, item: SidebarItem, tint: Color? = nil,
                     showsUnread: Bool = true, count: Int? = nil) -> some View {
        HStack {
            Label {
                Text(title).lineLimit(1)
            } icon: {
                Image(systemName: systemImage).foregroundStyle(tint ?? LuTheme.accent)
            }
            Spacer(minLength: 4)
            if let count {
                Text("\(count)").font(.caption2).monospacedDigit().foregroundStyle(LuTheme.textSecondary)
            } else if showsUnread {
                UnreadBadge(count: model.unreadCount(item))
            }
        }
        .tag(item)
    }
}
