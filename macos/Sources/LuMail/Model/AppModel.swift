import AppKit
import Foundation
import LuMailCore
import Observation

/// Ein Eintrag in der Sidebar.
enum SidebarItem: Hashable {
    case allInboxes
    case unread
    case flagged
    case category(UUID, MailCategory)
    case folder(UUID, String)

    var accountId: UUID? {
        switch self {
        case .category(let id, _), .folder(let id, _): return id
        default: return nil
        }
    }
}

/// Ein Fehler mit fertiger Meldung für die Oberfläche.
struct UserFacingError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}

/// Entwurf für das Verfassen-Fenster.
struct ComposeDraft: Identifiable {
    let id = UUID()
    var accountId: UUID?
    var to = ""
    var cc = ""
    var bcc = ""
    var subject = ""
    var body = ""
    var inReplyTo: String?
    var references: String?
}

@MainActor
@Observable
final class AppModel {
    // MARK: Zustand
    var accounts: [Account] = []
    var folders: [UUID: [MailFolder]] = [:]
    /// Nachrichten pro Konto und Ordnerpfad.
    var messages: [UUID: [String: [MessageSummary]]] = [:]
    /// Änderungen meldet die Oberfläche über `selectionChanged()`.
    var selection: SidebarItem? = .allInboxes
    var selectedMessageID: MessageSummary.ID?
    var openedMessage: ParsedMessage?
    var openedMessageError: String?
    var isLoadingMessage = false
    var searchText = ""
    var loadingAccounts: Set<UUID> = []
    var accountErrors: [UUID: String] = [:]
    var banner: String?
    var senderRules: [String: MailCategory] = [:]
    var composeDraft: ComposeDraft?
    var showAddAccount = false
    var allowRemoteContentFor: Set<MessageSummary.ID> = []

    // MARK: Einstellungen (gespeichert über `saveSettings()`)
    var separateCategories: Bool = UserDefaults.standard.object(forKey: "separateCategories") as? Bool ?? true
    var alwaysLoadRemoteContent: Bool = UserDefaults.standard.bool(forKey: "alwaysLoadRemoteContent")
    var refreshMinutes: Int = UserDefaults.standard.object(forKey: "refreshMinutes") as? Int ?? 3

    func saveSettings() {
        let defaults = UserDefaults.standard
        defaults.set(separateCategories, forKey: "separateCategories")
        defaults.set(alwaysLoadRemoteContent, forKey: "alwaysLoadRemoteContent")
        defaults.set(refreshMinutes, forKey: "refreshMinutes")
        startAutoRefresh()
    }

    // MARK: Dienste
    let catalog = ProviderCatalog.shared
    let paths = AppPaths()
    let auth: AuthService
    private let accountStore: AccountStore
    private let ruleStore: SenderRuleStore
    private var sessions: [UUID: MailAccountSession] = [:]
    private var refreshTimer: Timer?
    private var messageLoadTask: Task<Void, Never>?

    init() {
        auth = AuthService(paths: paths, transports: NetworkTransportFactory())
        accountStore = AccountStore(paths: paths)
        ruleStore = SenderRuleStore(paths: paths)
        accounts = accountStore.load()
        senderRules = ruleStore.load()
        startAutoRefresh()
        Task { await refreshAll() }
    }

    // MARK: Konten

    func provider(for account: Account) -> Provider? {
        catalog.provider(id: account.providerId)
    }

    func account(id: UUID) -> Account? {
        accounts.first { $0.id == id }
    }

    /// Anbieter in Katalog-Reihenfolge mit ihren Konten (leere Anbieter fehlen).
    var accountsByProvider: [(provider: Provider, accounts: [Account])] {
        catalog.providers.compactMap { provider in
            let list = accounts.filter { $0.providerId == provider.id }.sorted { $0.email < $1.email }
            return list.isEmpty ? nil : (provider, list)
        }
    }

    private func session(for account: Account) -> MailAccountSession? {
        if let session = sessions[account.id] { return session }
        guard let provider = provider(for: account) else { return nil }
        let auth = auth
        let session = MailAccountSession(account: account, provider: provider, transports: auth.transports) {
            try await auth.credential(for: account, provider: provider)
        }
        sessions[account.id] = session
        return session
    }

    /// Startet die Browser-Anmeldung und fügt das Konto hinzu.
    func addAccount(provider: Provider, emailHint: String?) async throws {
        let hint = emailHint?.trimmingCharacters(in: .whitespaces)
        let account = try await auth.signIn(provider: provider, emailHint: hint?.isEmpty == true ? nil : hint)
        if let existing = accounts.first(where: { $0.email.caseInsensitiveCompare(account.email) == .orderedSame && $0.providerId == account.providerId }) {
            // Erneute Anmeldung: altes Konto ersetzen (z. B. abgelaufenes Token).
            removeAccount(existing)
        }
        accounts.append(account)
        saveAccounts()
        selection = .category(account.id, .primary)
        await refresh(account)
    }

    func removeAccount(_ account: Account) {
        if let session = sessions.removeValue(forKey: account.id) {
            Task { await session.disconnect() }
        }
        auth.forget(account)
        accounts.removeAll { $0.id == account.id }
        folders[account.id] = nil
        messages[account.id] = nil
        accountErrors[account.id] = nil
        if selection?.accountId == account.id { selection = .allInboxes }
        saveAccounts()
    }

    private func saveAccounts() {
        do { try accountStore.save(accounts) } catch { banner = "Konten konnten nicht gespeichert werden: \(error.localizedDescription)" }
    }

    // MARK: Laden

    func startAutoRefresh() {
        refreshTimer?.invalidate()
        guard refreshMinutes > 0 else { return }
        refreshTimer = Timer.scheduledTimer(withTimeInterval: TimeInterval(refreshMinutes * 60), repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.refreshAll() }
        }
    }

    func refreshAll() async {
        await withTaskGroup(of: Void.self) { group in
            for account in accounts {
                group.addTask { await self.refresh(account) }
            }
        }
        // Gerade geöffneten Server-Ordner ebenfalls neu laden.
        if case .folder(let accountId, let path) = selection, let account = account(id: accountId) {
            await loadFolder(path, of: account)
        }
    }

    func refresh(_ account: Account) async {
        guard let session = session(for: account), !loadingAccounts.contains(account.id) else { return }
        loadingAccounts.insert(account.id)
        defer { loadingAccounts.remove(account.id) }
        do {
            let folderList = try await session.folders(refresh: folders[account.id] == nil)
            folders[account.id] = folderList
            let inboxPath = folderList.first { $0.role == .inbox }?.path ?? "INBOX"
            let inbox = try await session.loadInbox(overrides: senderRules)
            messages[account.id, default: [:]][inboxPath] = inbox
            accountErrors[account.id] = nil
        } catch {
            accountErrors[account.id] = AuthService.friendlyLoginError(error, provider: provider(for: account) ?? catalog.providers[0])
        }
    }

    func loadFolder(_ path: String, of account: Account) async {
        guard let session = session(for: account) else { return }
        do {
            messages[account.id, default: [:]][path] = try await session.loadFolder(path)
        } catch {
            banner = "Ordner konnte nicht geladen werden: \((error as? LocalizedError)?.errorDescription ?? error.localizedDescription)"
        }
    }

    func selectionChanged() {
        selectedMessageID = nil
        openedMessage = nil
        if case .folder(let accountId, let path) = selection, let account = account(id: accountId),
           inboxPath(of: accountId) != path {
            Task { await loadFolder(path, of: account) }
        }
    }

    // MARK: Ordner & Listen

    func inboxPath(of accountId: UUID) -> String {
        folders[accountId]?.first { $0.role == .inbox }?.path ?? "INBOX"
    }

    func inboxMessages(of accountId: UUID) -> [MessageSummary] {
        messages[accountId]?[inboxPath(of: accountId)] ?? []
    }

    /// Server-Ordner ohne INBOX: erst die mit Rolle, dann der Rest alphabetisch.
    func sidebarFolders(of accountId: UUID) -> [MailFolder] {
        (folders[accountId] ?? [])
            .filter { $0.isSelectable && $0.role != .inbox }
            .sorted { lhs, rhs in
                switch (lhs.role, rhs.role) {
                case let (l?, r?): return l.sortOrder < r.sortOrder
                case (.some, nil): return true
                case (nil, .some): return false
                case (nil, nil): return lhs.decodedPath.localizedStandardCompare(rhs.decodedPath) == .orderedAscending
                }
            }
    }

    func unreadCount(_ item: SidebarItem) -> Int {
        list(for: item, applySearch: false).filter { !$0.isSeen }.count
    }

    func unreadCount(account: Account) -> Int {
        inboxMessages(of: account.id).filter { !$0.isSeen }.count
    }

    /// Die Nachrichten für einen Sidebar-Eintrag, neueste zuerst.
    func list(for item: SidebarItem?, applySearch: Bool = true) -> [MessageSummary] {
        guard let item else { return [] }
        var result: [MessageSummary]
        switch item {
        case .allInboxes:
            result = accounts.flatMap { inboxMessages(of: $0.id) }
            if separateCategories { result = result.filter { $0.category == .primary || $0.category == nil } }
        case .unread:
            result = accounts.flatMap { inboxMessages(of: $0.id) }.filter { !$0.isSeen }
        case .flagged:
            result = accounts.flatMap { account in (messages[account.id] ?? [:]).values.flatMap { $0 } }.filter(\.isFlagged)
        case .category(let accountId, let category):
            result = inboxMessages(of: accountId)
            if separateCategories { result = result.filter { ($0.category ?? .primary) == category } }
        case .folder(let accountId, let path):
            result = messages[accountId]?[path] ?? []
        }
        if applySearch {
            let query = searchText.trimmingCharacters(in: .whitespaces).lowercased()
            if !query.isEmpty {
                result = result.filter { message in
                    message.subject.lowercased().contains(query)
                        || (message.from?.displayName.lowercased().contains(query) ?? false)
                        || (message.from?.address.lowercased().contains(query) ?? false)
                }
            }
        }
        return result.sorted { $0.date > $1.date }
    }

    var selectedMessage: MessageSummary? {
        guard let selectedMessageID else { return nil }
        for byFolder in messages.values {
            for list in byFolder.values {
                if let message = list.first(where: { $0.id == selectedMessageID }) { return message }
            }
        }
        return nil
    }

    var title: String {
        switch selection {
        case .allInboxes, nil: return "Alle Posteingänge"
        case .unread: return "Ungelesen"
        case .flagged: return "Markiert"
        case .category(let accountId, let category):
            return "\(category.title) – \(account(id: accountId)?.email ?? "")"
        case .folder(let accountId, let path):
            let name = folders[accountId]?.first { $0.path == path }?.displayName ?? path
            return "\(name) – \(account(id: accountId)?.email ?? "")"
        }
    }

    // MARK: Nachricht öffnen

    func openSelectedMessage() {
        messageLoadTask?.cancel()
        openedMessage = nil
        openedMessageError = nil
        guard let message = selectedMessage, let account = account(id: message.accountId), let session = session(for: account) else { return }
        isLoadingMessage = true
        messageLoadTask = Task {
            do {
                let parsed = try await session.fetchMessage(folder: message.folderPath, uid: message.uid)
                guard !Task.isCancelled, selectedMessageID == message.id else { return }
                openedMessage = parsed
                isLoadingMessage = false
                if !message.isSeen { await setFlag("\\Seen", add: true, on: message) }
            } catch {
                guard !Task.isCancelled else { return }
                openedMessageError = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
                isLoadingMessage = false
            }
        }
    }

    // MARK: Aktionen

    private func update(_ message: MessageSummary, _ change: (inout MessageSummary) -> Void) {
        guard var list = messages[message.accountId]?[message.folderPath],
              let index = list.firstIndex(where: { $0.id == message.id }) else { return }
        change(&list[index])
        messages[message.accountId]?[message.folderPath] = list
    }

    private func removeLocally(_ message: MessageSummary) {
        messages[message.accountId]?[message.folderPath]?.removeAll { $0.id == message.id }
        if selectedMessageID == message.id {
            selectedMessageID = nil
            openedMessage = nil
        }
    }

    func setFlag(_ flag: String, add: Bool, on message: MessageSummary) async {
        guard let account = account(id: message.accountId), let session = session(for: account) else { return }
        update(message) { if add { $0.flags.insert(flag) } else { $0.flags.remove(flag) } }
        do {
            try await session.setFlag(flag, add: add, uids: [message.uid], folder: message.folderPath)
        } catch {
            update(message) { if add { $0.flags.remove(flag) } else { $0.flags.insert(flag) } }
            banner = "Aktion fehlgeschlagen: \((error as? LocalizedError)?.errorDescription ?? error.localizedDescription)"
        }
    }

    func toggleRead(_ message: MessageSummary) async {
        await setFlag("\\Seen", add: !message.isSeen, on: message)
    }

    func toggleFlagged(_ message: MessageSummary) async {
        await setFlag("\\Flagged", add: !message.isFlagged, on: message)
    }

    func move(_ message: MessageSummary, to role: FolderRole) async {
        guard let account = account(id: message.accountId), let session = session(for: account) else { return }
        let currentRole = folders[account.id]?.first { $0.path == message.folderPath }?.role
        do {
            if role == .trash && currentRole == .trash {
                try await session.deletePermanently(uids: [message.uid], folder: message.folderPath)
            } else {
                try await session.move(uids: [message.uid], from: message.folderPath, to: role)
            }
            removeLocally(message)
        } catch {
            banner = "Verschieben fehlgeschlagen: \((error as? LocalizedError)?.errorDescription ?? error.localizedDescription)"
        }
    }

    /// „Absender immer als … einsortieren“ – gilt sofort für alle geladenen Nachrichten.
    func setRule(for message: MessageSummary, category: MailCategory, wholeDomain: Bool) {
        guard let from = message.from else { return }
        let key = wholeDomain ? from.domain : from.address.lowercased()
        senderRules[key] = category
        do { try ruleStore.save(senderRules) } catch { banner = "Regel konnte nicht gespeichert werden." }
        for (accountId, byFolder) in messages {
            for (path, list) in byFolder where path == inboxPath(of: accountId) {
                messages[accountId]?[path] = list.map { summary in
                    var summary = summary
                    if summary.from?.address.lowercased() == key || summary.from?.domain == key { summary.category = category }
                    return summary
                }
            }
        }
        banner = "Nachrichten von \(key) landen ab jetzt in „\(category.title)“."
    }

    // MARK: Verfassen

    func newMessage() {
        composeDraft = ComposeDraft(accountId: selection?.accountId ?? accounts.first?.id)
    }

    func reply(to message: MessageSummary, all: Bool) {
        guard let parsed = openedMessage, let account = account(id: message.accountId) else { return }
        let replyTargets = parsed.replyTo.isEmpty ? parsed.from : parsed.replyTo
        var cc: [MailAddress] = []
        if all {
            cc = (parsed.to + parsed.cc).filter { $0.address.caseInsensitiveCompare(account.email) != .orderedSame }
        }
        let intro = "Am \(message.date.formatted(date: .abbreviated, time: .shortened)) schrieb \(parsed.from.first?.displayName ?? "jemand"):"
        composeDraft = ComposeDraft(
            accountId: account.id,
            to: replyTargets.map(\.address).joined(separator: ", "),
            cc: cc.map(\.address).joined(separator: ", "),
            subject: MessageBuilder.prefixedSubject(parsed.subject, prefix: "Re:"),
            body: MessageBuilder.quotedBody(original: parsed, intro: intro),
            inReplyTo: parsed.messageId,
            references: [parsed.references, parsed.messageId].compactMap { $0 }.joined(separator: " "))
    }

    func forward(_ message: MessageSummary) {
        guard let parsed = openedMessage else { return }
        let intro = "---------- Weitergeleitete Nachricht ----------\nVon: \(parsed.from.first?.headerValue ?? "")\nBetreff: \(parsed.subject)"
        composeDraft = ComposeDraft(
            accountId: message.accountId,
            subject: MessageBuilder.prefixedSubject(parsed.subject, prefix: "Fwd:"),
            body: MessageBuilder.quotedBody(original: parsed, intro: intro))
    }

    func send(_ draft: ComposeDraft) async throws {
        guard let accountId = draft.accountId, let account = account(id: accountId), let session = session(for: account) else {
            throw UserFacingError("Bitte ein Absender-Konto wählen.")
        }
        let to = AddressParser.parseList(draft.to)
        guard !to.isEmpty else { throw UserFacingError("Bitte mindestens einen gültigen Empfänger angeben.") }
        let message = OutgoingMessage(
            from: MailAddress(name: account.displayName == account.email ? nil : account.displayName, address: account.email),
            to: to, cc: AddressParser.parseList(draft.cc), bcc: AddressParser.parseList(draft.bcc),
            subject: draft.subject, body: draft.body, inReplyTo: draft.inReplyTo,
            references: draft.references?.isEmpty == true ? nil : draft.references)
        try await session.send(message)
        banner = "Nachricht gesendet."
    }

    // MARK: oauth.json

    /// Legt oauth.json aus der Vorlage an (falls nötig) und zeigt sie im Finder.
    func revealOAuthConfig() {
        do {
            try paths.ensureExists()
            if !FileManager.default.fileExists(atPath: paths.oauthFile.path) {
                try Data(SharedResources.oauthExampleJSON.utf8).write(to: paths.oauthFile)
            }
            NSWorkspace.shared.activateFileViewerSelecting([paths.oauthFile])
        } catch {
            banner = "oauth.json konnte nicht angelegt werden: \(error.localizedDescription)"
        }
    }
}
