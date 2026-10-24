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
            } header: {
                HStack(spacing: 6) {
                    MoonMark(size: 12)
                    Text("Übersicht")
                }
            }

            ForEach(model.accountsByProvider, id: \.provider.id) { group in
                Section {
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
            if model.separateCategories {
                ForEach(MailCategory.allCases, id: \.self) { category in
                    row(category.title, systemImage: category.systemImage, item: .category(account.id, category),
                        tint: category == .primary ? nil : LuTheme.category(category))
                }
            } else {
                row("Posteingang", systemImage: "tray", item: .category(account.id, .primary))
            }
            ForEach(model.sidebarFolders(of: account.id)) { folder in
                row(folder.displayName, systemImage: folder.role?.systemImage ?? "folder",
                    item: .folder(account.id, folder.path), showsUnread: false)
            }
        } label: {
            HStack(spacing: 6) {
                VStack(alignment: .leading, spacing: 1) {
                    Text(account.email)
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
                Button("Aktualisieren") { Task { await model.refresh(account) } }
                Button("Neu anmelden (Browser) …") { model.showAddAccount = true }
                Divider()
                Button("Konto entfernen", role: .destructive) { model.removeAccount(account) }
            }
        }
    }

    private func row(_ title: String, systemImage: String, item: SidebarItem, tint: Color? = nil, showsUnread: Bool = true) -> some View {
        HStack {
            Label {
                Text(title).lineLimit(1)
            } icon: {
                Image(systemName: systemImage).foregroundStyle(tint ?? LuTheme.accent)
            }
            Spacer(minLength: 4)
            if showsUnread {
                UnreadBadge(count: model.unreadCount(item))
            }
        }
        .tag(item)
    }
}
