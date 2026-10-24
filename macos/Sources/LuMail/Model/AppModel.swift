import AppKit
import Foundation
import LuMailCore
import Observation

/// Ein Eintrag in der Sidebar.
enum SidebarItem: Hashable {
    case allInboxes
    case unread
    case flagged
    case reminders
    case scheduled
    case providerInbox(String)
    case category(UUID, MailCategory)
    case folder(UUID, String)
    /// Ordner eines Web-Kontos (Konto, Schlüssel aus `web.folders`, z. B. „promotions“).
    case web(UUID, String)

    var accountId: UUID? {
        switch self {
        case .category(let id, _), .folder(let id, _), .web(let id, _): return id
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
    var attachments: [OutgoingAttachment] = []
}

@MainActor
@Observable
final class AppModel {
    // MARK: Zustand
    var accounts: [Account] = []
    var folders: [UUID: [MailFolder]] = [:]
    /// Nachrichten pro Konto und Ordnerpfad.
    var messages: [UUID: [String: [MessageSummary]]] = [:]
    /// Ungelesene je Konto und Ordnerpfad (IMAP STATUS).
    var unreadCounts: [UUID: [String: Int]] = [:]
    /// Änderungen meldet die Oberfläche über `selectionChanged()`.
    var selection: SidebarItem? = .allInboxes
    /// Ausgewählte Nachrichten (Mehrfachauswahl). Bei Konversationen: die neueste Nachricht je Konversation.
    var selectedMessageIDs: Set<MessageSummary.ID> = []
    /// Geladene Nachrichten (für Lesebereich und Konversationen).
    var parsedMessages: [MessageSummary.ID: ParsedMessage] = [:]
    var loadingMessageIDs: Set<MessageSummary.ID> = []
    var messageErrors: [MessageSummary.ID: String] = [:]
    var searchText = ""
    var serverResults: [MessageSummary] = []
    var isSearchingServer = false
    var loadingAccounts: Set<UUID> = []
    var accountErrors: [UUID: String] = [:]
    var isOffline = false
    var banner: String?
    var rules = RuleSet()
    var contacts = ContactBook()
    var snoozed: [SnoozedMessage] = []
    var scheduled: [ScheduledMessage] = []
    var composeDraft: ComposeDraft?
    var showAddAccount = false
    var allowRemoteContentFor: Set<MessageSummary.ID> = []

    // MARK: Einstellungen (gespeichert über `saveSettings()`)
    private static let defaults = UserDefaults.standard
    var separateCategories: Bool = AppModel.defaults.object(forKey: "separateCategories") as? Bool ?? true
    var alwaysLoadRemoteContent: Bool = AppModel.defaults.bool(forKey: "alwaysLoadRemoteContent")
    var refreshMinutes: Int = AppModel.defaults.object(forKey: "refreshMinutes") as? Int ?? 3
    var notificationsEnabled: Bool = AppModel.defaults.object(forKey: "notificationsEnabled") as? Bool ?? true
    var pushEnabled: Bool = AppModel.defaults.object(forKey: "pushEnabled") as? Bool ?? true
    var offlineCacheEnabled: Bool = AppModel.defaults.object(forKey: "offlineCacheEnabled") as? Bool ?? true
    var conversationMode: Bool = AppModel.defaults.bool(forKey: "conversationMode")
    /// Aufräumen nach Tagen je Kategorie (0 = aus).
    var cleanupDays: [MailCategory: Int] = {
        let stored = AppModel.defaults.dictionary(forKey: "cleanupDays") as? [String: Int] ?? [:]
        var result: [MailCategory: Int] = [:]
        for (key, value) in stored { result[MailCategory(rawValue: key)] = value }
        return result
    }()

    func saveSettings() {
        let defaults = Self.defaults
        defaults.set(separateCategories, forKey: "separateCategories")
        defaults.set(alwaysLoadRemoteContent, forKey: "alwaysLoadRemoteContent")
        defaults.set(refreshMinutes, forKey: "refreshMinutes")
        defaults.set(notificationsEnabled, forKey: "notificationsEnabled")
        defaults.set(pushEnabled, forKey: "pushEnabled")
        defaults.set(offlineCacheEnabled, forKey: "offlineCacheEnabled")
        defaults.set(conversationMode, forKey: "conversationMode")
        defaults.set(Dictionary(uniqueKeysWithValues: cleanupDays.map { ($0.key.rawValue, $0.value) }), forKey: "cleanupDays")
        startAutoRefresh()
        updatePush()
        if !offlineCacheEnabled { cache.clear() }
    }

    // MARK: Dienste
    let catalog = ProviderCatalog.shared
    let paths = AppPaths()
    let auth: AuthService
    private let accountStore: AccountStore
    private let ruleStore: RuleStore
    private let contactStore: ContactStore
    private let snoozeStore: SnoozeStore
    private let outboxStore: OutboxStore
    let cache: MessageCache
    private var sessions: [UUID: MailAccountSession] = [:]
    /// Eingebettete Webmail-Postfächer der Web-Konten (§20).
    let webMail = WebMailService()
    /// Im Seitentitel gefundene Adressen von Konten, deren Anmeldung noch läuft.
    private var pendingWebEmails: [UUID: String] = [:]
    private var refreshTimer: Timer?
    private var tickTimer: Timer?
    private var pushTasks: [UUID: Task<Void, Never>] = [:]
    private var pendingPushRefresh: [UUID: Task<Void, Never>] = [:]
    /// UIDs des Posteingangs beim letzten Laden – für Mitteilungen über neue Mails.
    private var knownInboxUIDs: [UUID: Set<UInt32>] = [:]

    init() {
        auth = AuthService(paths: paths, transports: NetworkTransportFactory())
        accountStore = AccountStore(paths: paths)
        ruleStore = RuleStore(paths: paths)
        contactStore = ContactStore(paths: paths)
        snoozeStore = SnoozeStore(paths: paths)
        outboxStore = OutboxStore(paths: paths)
        cache = MessageCache(paths: paths)
        accounts = accountStore.load()
        rules = ruleStore.load()
        contacts = contactStore.load()
        snoozed = snoozeStore.load()
        scheduled = outboxStore.list()
        restoreFromCache()
        NotificationService.shared.requestAuthorization()
        NotificationService.shared.onOpen = { [weak self] accountId, folder, uid in
            self?.reveal(accountId: accountId, folder: folder, uid: uid)
        }
        NotificationService.shared.onOpenWeb = { [weak self] accountId in
            self?.selection = .web(accountId, "primary")
        }
        setUpWebMail()
        startAutoRefresh()
        startTicker()
        updatePush()
        Task {
            await refreshAll()
            await processDue()
        }
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
            let list = accounts.filter { $0.providerId == provider.id }.sorted { $0.label < $1.label }
            return list.isEmpty ? nil : (provider, list)
        }
    }

    /// Nur IMAP-Konten (zum Verfassen, Suchen, …).
    var mailAccounts: [Account] { accounts.filter { !$0.isWeb } }

    var mailAccountsByProvider: [(provider: Provider, accounts: [Account])] {
        accountsByProvider.compactMap { group in
            let list = group.accounts.filter { !$0.isWeb }
            return list.isEmpty ? nil : (group.provider, list)
        }
    }

    // MARK: Web-Konten (§20)

    private func setUpWebMail() {
        webMail.onUnreadChanged = { [weak self] in self?.updateBadge() }
        webMail.onEmailFound = { [weak self] accountId, email in
            guard let self else { return }
            guard let index = accounts.firstIndex(where: { $0.id == accountId }) else {
                pendingWebEmails[accountId] = email
                return
            }
            guard accounts[index].email.isEmpty else { return }
            accounts[index].email = email
            accounts[index].displayName = email
            saveAccounts()
        }
        webMail.onNewMail = { [weak self] accountId, count in
            guard let self, notificationsEnabled, let account = account(id: accountId) else { return }
            let name = provider(for: account)?.name ?? "Lu-Mail"
            NotificationService.shared.notifyWeb(accountId: accountId, title: name, subtitle: account.label,
                                                 body: count == 1 ? "1 neue E-Mail" : "\(count) neue E-Mails")
        }
        for account in accounts where account.isWeb {
            if let web = provider(for: account)?.web { webMail.preload(account.id, config: web) }
        }
    }

    /// Legt ein Web-Konto an, nachdem die Anmeldung im eingebetteten Browser geklappt hat.
    @discardableResult
    func addWebAccount(id: UUID, provider: Provider, emailHint: String?) -> Account {
        webMail.finishLogin(id)
        let hint = emailHint?.trimmingCharacters(in: .whitespaces) ?? ""
        let email = hint.isEmpty ? (pendingWebEmails.removeValue(forKey: id) ?? "") : hint
        let existingCount = accounts.filter { $0.providerId == provider.id && $0.isWeb }.count
        let fallbackName = existingCount == 0 ? provider.name : "\(provider.name) \(existingCount + 1)"
        let account = Account(id: id, providerId: provider.id, email: email, displayName: email.isEmpty ? fallbackName : email,
                              authMethod: .web)
        accounts.append(account)
        saveAccounts()
        selection = .web(id, "primary")
        return account
    }

    /// Erneut anmelden (Web-Konto): Anmeldeseite im Postfach des Kontos zeigen.
    func reloginWeb(_ account: Account) {
        guard let web = provider(for: account)?.web else { return }
        selection = .web(account.id, "primary")
        webMail.showLogin(account.id, config: web)
    }

    /// Alle Kategorien für die Sidebar: fest eingebaute plus eigene.
    var allCategories: [MailCategory] {
        MailCategory.builtIn + rules.customCategories.map(\.category)
    }

    private func session(for account: Account) -> MailAccountSession? {
        // Web-Konten haben keine IMAP-Verbindung – alles Laden, Push, Suche usw. überspringt sie hier.
        guard !account.isWeb else { return nil }
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
    func addAccount(provider: Provider, emailHint: String?, serversHint: CustomServers? = nil) async throws {
        let hint = emailHint?.trimmingCharacters(in: .whitespaces)
        var account = try await auth.signIn(provider: provider, emailHint: hint?.isEmpty == true ? nil : hint, serversHint: serversHint)
        if let existing = accounts.first(where: { $0.email.caseInsensitiveCompare(account.email) == .orderedSame && $0.providerId == account.providerId }) {
            // Erneute Anmeldung: altes Konto ersetzen (z. B. abgelaufenes Token), Signatur behalten.
            account.signature = existing.signature
            removeAccount(existing, forgetSecrets: false)
        }
        accounts.append(account)
        saveAccounts()
        selection = .category(account.id, .primary)
        updatePush()
        await refresh(account)
    }

    func removeAccount(_ account: Account, forgetSecrets: Bool = true) {
        if account.isWeb, forgetSecrets { webMail.discard(account.id) }
        if let session = sessions.removeValue(forKey: account.id) {
            Task { await session.disconnect() }
        }
        pushTasks.removeValue(forKey: account.id)?.cancel()
        if forgetSecrets { auth.forget(account) }
        cache.removeAccount(account.id)
        accounts.removeAll { $0.id == account.id }
        folders[account.id] = nil
        messages[account.id] = nil
        unreadCounts[account.id] = nil
        accountErrors[account.id] = nil
        knownInboxUIDs[account.id] = nil
        if selection?.accountId == account.id { selection = .allInboxes }
        saveAccounts()
        updateBadge()
    }

    func updateSignature(_ signature: String, for account: Account) {
        guard let index = accounts.firstIndex(where: { $0.id == account.id }) else { return }
        accounts[index].signature = signature.isEmpty ? nil : signature
        saveAccounts()
    }

    private func saveAccounts() {
        do { try accountStore.save(accounts) } catch { banner = "Konten konnten nicht gespeichert werden: \(error.localizedDescription)" }
    }

    // MARK: Offline-Cache

    private func restoreFromCache() {
        guard offlineCacheEnabled else { return }
        for account in accounts {
            guard let cached = cache.loadAccount(id: account.id) else { continue }
            let restored = cached.restore(accountId: account.id)
            folders[account.id] = restored.folders
            var byFolder: [String: [MessageSummary]] = [:]
            for message in restored.messages { byFolder[message.folderPath, default: []].append(message) }
            messages[account.id] = byFolder
        }
        updateBadge()
    }

    private func saveCache(for account: Account) {
        guard offlineCacheEnabled else { return }
        let all = (messages[account.id] ?? [:]).values.flatMap { $0 }
        try? cache.saveAccount(CachedAccount(folders: folders[account.id] ?? [], messages: all), id: account.id)
    }

    // MARK: Laden

    func startAutoRefresh() {
        refreshTimer?.invalidate()
        guard refreshMinutes > 0 else { return }
        refreshTimer = Timer.scheduledTimer(withTimeInterval: TimeInterval(refreshMinutes * 60), repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.refreshAll() }
        }
    }

    /// Prüft alle 30 Sekunden Erinnerungen und geplante Mails.
    private func startTicker() {
        tickTimer?.invalidate()
        tickTimer = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.processDue() }
        }
    }

    /// Push per IMAP IDLE (eine eigene Verbindung pro Konto).
    func updatePush() {
        guard pushEnabled else {
            pushTasks.values.forEach { $0.cancel() }
            pushTasks = [:]
            return
        }
        for account in accounts where pushTasks[account.id] == nil {
            guard let session = session(for: account) else { continue }
            let accountId = account.id
            pushTasks[accountId] = Task.detached { [weak self] in
                _ = await session.watchInbox {
                    Task { @MainActor in self?.schedulePushRefresh(accountId) }
                }
            }
        }
    }

    private func schedulePushRefresh(_ accountId: UUID) {
        pendingPushRefresh[accountId]?.cancel()
        pendingPushRefresh[accountId] = Task {
            try? await Task.sleep(for: .seconds(2))
            guard !Task.isCancelled, let account = account(id: accountId) else { return }
            await refresh(account)
        }
    }

    func refreshAll() async {
        await withTaskGroup(of: Void.self) { group in
            for account in accounts {
                group.addTask { await self.refresh(account) }
            }
        }
        if case .folder(let accountId, let path) = selection, let account = account(id: accountId) {
            await loadFolder(path, of: account)
        }
        let mailAccounts = mailAccounts
        isOffline = !mailAccounts.isEmpty && mailAccounts.allSatisfy { accountErrors[$0.id]?.contains("nicht erreichbar") == true || accountErrors[$0.id]?.contains("Verbindung") == true }
    }

    func refresh(_ account: Account) async {
        guard let session = session(for: account), !loadingAccounts.contains(account.id) else { return }
        loadingAccounts.insert(account.id)
        defer { loadingAccounts.remove(account.id) }
        do {
            let folderList = try await session.folders(refresh: true)
            folders[account.id] = folderList
            let inboxPath = folderList.first { $0.role == .inbox }?.path ?? "INBOX"
            let inbox = try await session.loadInbox(rules: rules)
            messages[account.id, default: [:]][inboxPath] = inbox
            accountErrors[account.id] = nil

            let fresh = NewMailDetector.newMessages(previous: knownInboxUIDs[account.id], current: inbox)
            knownInboxUIDs[account.id] = Set(inbox.map(\.uid))
            if notificationsEnabled, !fresh.isEmpty {
                NotificationService.shared.notify(fresh) { [weak self] id in self?.account(id: id)?.email ?? "" }
            }
            contacts.record(inbox.compactMap(\.from))
            try? contactStore.save(contacts)

            if let counts = try? await session.unreadCounts() { unreadCounts[account.id] = counts }
            await cleanup(account, inbox: inbox)
            saveCache(for: account)
            updateBadge()
        } catch {
            accountErrors[account.id] = AuthService.friendlyLoginError(error, provider: provider(for: account) ?? catalog.providers[0])
        }
    }

    func loadFolder(_ path: String, of account: Account) async {
        guard let session = session(for: account) else { return }
        do {
            messages[account.id, default: [:]][path] = try await session.loadFolder(path)
            saveCache(for: account)
        } catch {
            banner = "Ordner konnte nicht geladen werden: \(Self.describe(error))"
        }
    }

    func selectionChanged() {
        selectedMessageIDs = []
        serverResults = []
        if case .folder(let accountId, let path) = selection, let account = account(id: accountId),
           inboxPath(of: accountId) != path {
            Task { await loadFolder(path, of: account) }
        }
    }

    /// Öffnet eine Nachricht (z. B. aus einer Mitteilung).
    func reveal(accountId: UUID, folder: String, uid: UInt32) {
        selection = .category(accountId, .primary)
        selectionChanged()
        if let message = messages[accountId]?[folder]?.first(where: { $0.uid == uid }) {
            if let category = message.category { selection = .category(accountId, category) }
            selectedMessageIDs = [message.id]
            openSelectedMessage()
        }
    }

    // MARK: Aufräumen (§12)

    private func cleanup(_ account: Account, inbox: [MessageSummary]) async {
        let candidates = CleanupPlanner.messagesToClean(inbox, days: cleanupDays)
        guard !candidates.isEmpty, let session = session(for: account) else { return }
        let hasArchive = folders[account.id]?.contains { $0.role == .archive } ?? false
        let uids = candidates.map(\.uid)
        do {
            try await session.move(uids: uids, from: candidates[0].folderPath, to: hasArchive ? .archive : .trash)
            let removed = Set(candidates.map(\.id))
            messages[account.id]?[candidates[0].folderPath]?.removeAll { removed.contains($0.id) }
            banner = "\(candidates.count) alte Nachrichten in \(account.email) aufgeräumt."
        } catch {
            // Aufräumen ist optional – Fehler nur still ignorieren.
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
        if case .folder(let accountId, let path) = item, let count = unreadCounts[accountId]?[path] {
            return count
        }
        if case .scheduled = item { return 0 }
        if case .web(let accountId, let key) = item { return key == "primary" ? webMail.unread[accountId] ?? 0 : 0 }
        return list(for: item, applySearch: false).filter { !$0.isSeen }.count
    }

    func unreadCount(account: Account) -> Int {
        if account.isWeb { return webMail.unread[account.id] ?? 0 }
        return inboxMessages(of: account.id).filter { !$0.isSeen }.count
    }

    /// Ungelesene im Posteingang (Kategorie Posteingang) aller Konten – für Dock und Menüleiste.
    var totalUnread: Int {
        list(for: .allInboxes, applySearch: false).filter { !$0.isSeen }.count + webMail.unread.values.reduce(0, +)
    }

    var latestUnread: [MessageSummary] {
        Array(list(for: .allInboxes, applySearch: false).filter { !$0.isSeen }.prefix(5))
    }

    private func updateBadge() {
        NotificationService.shared.setBadge(totalUnread)
    }

    private func isSnoozed(_ message: MessageSummary, now: Date = Date()) -> Bool {
        snoozed.contains { $0.until > now && $0.matches(message) }
    }

    private func primaryInbox(_ accountIds: [UUID]) -> [MessageSummary] {
        var result = accountIds.flatMap { inboxMessages(of: $0) }
        if separateCategories { result = result.filter { $0.category == .primary || $0.category == nil } }
        return result
    }

    /// Die Nachrichten für einen Sidebar-Eintrag, neueste zuerst.
    func list(for item: SidebarItem?, applySearch: Bool = true) -> [MessageSummary] {
        guard let item else { return [] }
        var result: [MessageSummary]
        switch item {
        case .allInboxes:
            result = primaryInbox(accounts.map(\.id))
        case .providerInbox(let providerId):
            result = primaryInbox(accounts.filter { $0.providerId == providerId }.map(\.id))
        case .unread:
            result = accounts.flatMap { inboxMessages(of: $0.id) }.filter { !$0.isSeen }
        case .flagged:
            result = accounts.flatMap { account in (messages[account.id] ?? [:]).values.flatMap { $0 } }.filter(\.isFlagged)
        case .reminders:
            let all = accounts.flatMap { account in (messages[account.id] ?? [:]).values.flatMap { $0 } }
            return all.filter { message in snoozed.contains { $0.matches(message) } }.sorted { $0.date > $1.date }
        case .scheduled, .web:
            return []
        case .category(let accountId, let category):
            result = inboxMessages(of: accountId)
            if separateCategories { result = result.filter { ($0.category ?? .primary) == category } }
        case .folder(let accountId, let path):
            result = messages[accountId]?[path] ?? []
        }
        result = result.filter { !isSnoozed($0) }
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

    func conversations(for item: SidebarItem?) -> [Conversation] {
        ConversationBuilder.group(list(for: item))
    }

    /// Alle bekannten Nachrichten (inkl. Serversuche) für Nachschlagen per ID.
    private func findMessage(_ id: MessageSummary.ID) -> MessageSummary? {
        for byFolder in messages.values {
            for list in byFolder.values {
                if let message = list.first(where: { $0.id == id }) { return message }
            }
        }
        return serverResults.first { $0.id == id }
    }

    func messages(for ids: Set<MessageSummary.ID>) -> [MessageSummary] {
        ids.compactMap(findMessage).sorted { $0.date > $1.date }
    }

    var selectedMessages: [MessageSummary] {
        selectedMessageIDs.compactMap(findMessage).sorted { $0.date > $1.date }
    }

    var selectedMessage: MessageSummary? {
        selectedMessageIDs.count == 1 ? selectedMessageIDs.first.flatMap(findMessage) : nil
    }

    /// Die Nachrichten, die der Lesebereich zeigt: bei Konversationen alle, sonst nur die eine.
    var selectedThread: [MessageSummary] {
        guard let message = selectedMessage else { return [] }
        guard conversationMode else { return [message] }
        let pool = list(for: selection, applySearch: false)
        return ConversationBuilder.group(pool).first { $0.messages.contains { $0.id == message.id } }?.messages.reversed() ?? [message]
    }

    var title: String {
        switch selection {
        case .allInboxes, nil: return "Alle Posteingänge"
        case .unread: return "Ungelesen"
        case .flagged: return "Markiert"
        case .reminders: return "Erinnerungen"
        case .scheduled: return "Geplant"
        case .providerInbox(let id): return "Alle \(catalog.provider(id: id)?.name ?? id)-Posteingänge"
        case .category(let accountId, let category):
            return "\(category.title(rules)) – \(account(id: accountId)?.email ?? "")"
        case .folder(let accountId, let path):
            let name = folders[accountId]?.first { $0.path == path }?.displayName ?? path
            return "\(name) – \(account(id: accountId)?.email ?? "")"
        case .web(let accountId, let key):
            let name = WebConfig.folderOrder.first { $0.key == key }?.title ?? key
            return "\(name) – \(account(id: accountId)?.label ?? "")"
        }
    }

    // MARK: Suche auf dem Server (§4)

    func searchOnServer() async {
        let query = searchText.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { serverResults = []; return }
        isSearchingServer = true
        defer { isSearchingServer = false }
        var targets: [(Account, String)] = []
        switch selection {
        case .folder(let accountId, let path):
            if let account = account(id: accountId) { targets = [(account, path)] }
        case .category(let accountId, _):
            if let account = account(id: accountId) { targets = [(account, inboxPath(of: accountId))] }
        case .providerInbox(let providerId):
            targets = accounts.filter { $0.providerId == providerId }.map { ($0, inboxPath(of: $0.id)) }
        default:
            targets = accounts.map { ($0, inboxPath(of: $0.id)) }
        }
        let localIDs = Set(list(for: selection).map(\.id))
        var results: [MessageSummary] = []
        for (account, path) in targets {
            guard let session = session(for: account) else { continue }
            do {
                results += try await session.search(query, in: path)
            } catch {
                banner = "Suche in \(account.email) fehlgeschlagen: \(Self.describe(error))"
            }
        }
        serverResults = results.filter { !localIDs.contains($0.id) }.sorted { $0.date > $1.date }
        if results.isEmpty { banner = "Keine weiteren Treffer auf dem Server." }
    }

    // MARK: Nachricht öffnen

    func openSelectedMessage() {
        for message in selectedThread where parsedMessages[message.id] == nil {
            load(message)
        }
    }

    func parsed(_ message: MessageSummary) -> ParsedMessage? {
        parsedMessages[message.id]
    }

    private func load(_ message: MessageSummary) {
        guard !loadingMessageIDs.contains(message.id), let account = account(id: message.accountId), let session = session(for: account) else { return }
        if offlineCacheEnabled, let data = cache.loadBody(accountId: message.accountId, folder: message.folderPath, uid: message.uid) {
            parsedMessages[message.id] = MIMEParser.parse(data)
            if !message.isSeen { Task { await setFlag("\\Seen", add: true, on: [message]) } }
            return
        }
        loadingMessageIDs.insert(message.id)
        messageErrors[message.id] = nil
        Task {
            defer { loadingMessageIDs.remove(message.id) }
            do {
                let raw = try await session.fetchRawMessage(folder: message.folderPath, uid: message.uid)
                if offlineCacheEnabled { cache.saveBody(raw, accountId: message.accountId, folder: message.folderPath, uid: message.uid) }
                parsedMessages[message.id] = MIMEParser.parse(raw)
                if !message.isSeen { await setFlag("\\Seen", add: true, on: [message]) }
            } catch {
                messageErrors[message.id] = Self.describe(error)
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
        serverResults.removeAll { $0.id == message.id }
        selectedMessageIDs.remove(message.id)
        parsedMessages[message.id] = nil
    }

    /// Gruppiert nach Konto und Ordner, damit pro Gruppe ein IMAP-Befehl reicht.
    private func grouped(_ messages: [MessageSummary]) -> [(Account, String, [MessageSummary])] {
        var groups: [String: (Account, String, [MessageSummary])] = [:]
        for message in messages {
            guard let account = account(id: message.accountId) else { continue }
            let key = "\(message.accountId.uuidString)|\(message.folderPath)"
            var entry = groups[key] ?? (account, message.folderPath, [])
            entry.2.append(message)
            groups[key] = entry
        }
        return Array(groups.values)
    }

    func setFlag(_ flag: String, add: Bool, on messages: [MessageSummary]) async {
        for message in messages { update(message) { if add { $0.flags.insert(flag) } else { $0.flags.remove(flag) } } }
        for (account, folder, group) in grouped(messages) {
            guard let session = session(for: account) else { continue }
            do {
                try await session.setFlag(flag, add: add, uids: group.map(\.uid), folder: folder)
            } catch {
                for message in group { update(message) { if add { $0.flags.remove(flag) } else { $0.flags.insert(flag) } } }
                banner = "Aktion fehlgeschlagen: \(Self.describe(error))"
            }
        }
        updateBadge()
    }

    func toggleRead(_ messages: [MessageSummary]) async {
        let markRead = messages.contains { !$0.isSeen }
        await setFlag("\\Seen", add: markRead, on: messages)
    }

    func toggleFlagged(_ messages: [MessageSummary]) async {
        let flag = messages.contains { !$0.isFlagged }
        await setFlag("\\Flagged", add: flag, on: messages)
    }

    func move(_ messages: [MessageSummary], to role: FolderRole) async {
        for (account, folder, group) in grouped(messages) {
            guard let session = session(for: account) else { continue }
            let currentRole = folders[account.id]?.first { $0.path == folder }?.role
            do {
                if role == .trash && currentRole == .trash {
                    try await session.deletePermanently(uids: group.map(\.uid), folder: folder)
                } else {
                    try await session.move(uids: group.map(\.uid), from: folder, to: role)
                }
                group.forEach(removeLocally)
            } catch {
                banner = "Verschieben fehlgeschlagen: \(Self.describe(error))"
            }
        }
        updateBadge()
    }

    /// „Absender immer als … einsortieren“ – gilt sofort für alle geladenen Nachrichten.
    func setRule(for message: MessageSummary, category: MailCategory, wholeDomain: Bool) {
        guard let from = message.from else { return }
        let key = wholeDomain ? from.domain : from.address.lowercased()
        rules.senders[key] = category
        saveRules()
        applyUserRulesLocally()
        banner = "Nachrichten von \(key) landen ab jetzt in „\(category.title(rules))“."
    }

    func addRule(_ rule: ClassificationRule) {
        rules.rules.append(rule)
        saveRules()
        applyUserRulesLocally()
    }

    func removeRule(_ rule: ClassificationRule) {
        rules.rules.removeAll { $0 == rule }
        saveRules()
        Task { await refreshAll() }
    }

    func removeSenderRule(_ key: String) {
        rules.senders[key] = nil
        saveRules()
        Task { await refreshAll() }
    }

    @discardableResult
    func addCustomCategory(name: String, color: String) -> CustomCategory? {
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return nil }
        let category = CustomCategory(id: CustomCategory.makeID(name: trimmed, existing: rules.customCategories), name: trimmed, color: color, icon: "folder")
        rules.customCategories.append(category)
        saveRules()
        return category
    }

    func removeCustomCategory(_ category: CustomCategory) {
        rules.customCategories.removeAll { $0.id == category.id }
        rules.rules.removeAll { $0.category == category.category }
        rules.senders = rules.senders.filter { $0.value != category.category }
        saveRules()
        Task { await refreshAll() }
    }

    private func saveRules() {
        do { try ruleStore.save(rules) } catch { banner = "Regeln konnten nicht gespeichert werden." }
    }

    /// Wendet Absender- und eigene Regeln sofort auf geladene Posteingänge an (der Rest beim nächsten Abruf).
    private func applyUserRulesLocally() {
        for account in accounts {
            let path = inboxPath(of: account.id)
            guard let list = messages[account.id]?[path] else { continue }
            messages[account.id]?[path] = list.map { summary in
                var summary = summary
                if let category = rules.category(from: summary.from, to: summary.to + summary.cc, subject: summary.subject) {
                    summary.category = category
                }
                return summary
            }
        }
        updateBadge()
    }

    // MARK: Abbestellen (§11)

    func unsubscribe(_ message: MessageSummary) async {
        guard let method = message.unsubscribe else { return }
        switch method {
        case .oneClick(let url):
            do {
                let (_, response) = try await URLSession.shared.data(for: UnsubscribeMethod.oneClickRequest(url))
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                banner = (200..<300).contains(status) ? "Abbestellt. Es kann ein paar Tage dauern, bis keine Mails mehr kommen."
                                                     : "Abbestellen fehlgeschlagen (HTTP \(status)). Öffne den Link in der Mail."
            } catch {
                banner = "Abbestellen fehlgeschlagen: \(Self.describe(error))"
            }
        case .mail(let address, let subject):
            guard let account = account(id: message.accountId), let session = session(for: account) else { return }
            let mail = OutgoingMessage(from: MailAddress(name: nil, address: account.email), to: [MailAddress(name: nil, address: address)],
                                       subject: subject, body: "unsubscribe")
            do {
                try await session.send(mail)
                banner = "Abmelde-Mail an \(address) gesendet."
            } catch {
                banner = "Abmelde-Mail konnte nicht gesendet werden: \(Self.describe(error))"
            }
        case .website(let url):
            NSWorkspace.shared.open(url)
        }
    }

    // MARK: Später erinnern & später senden (§13)

    func snooze(_ messages: [MessageSummary], until date: Date) {
        for message in messages {
            snoozed.removeAll { $0.matches(message) }
            snoozed.append(SnoozedMessage(accountId: message.accountId, folder: message.folderPath, messageId: message.messageId,
                                          uid: message.uid, until: date, subject: message.subject, sender: message.from?.displayName))
            selectedMessageIDs.remove(message.id)
        }
        try? snoozeStore.save(snoozed)
        banner = "Erinnerung am \(date.formatted(date: .abbreviated, time: .shortened))."
        updateBadge()
    }

    func unsnooze(_ message: MessageSummary) {
        snoozed.removeAll { $0.matches(message) }
        try? snoozeStore.save(snoozed)
    }

    func processDue() async {
        let now = Date()
        // Erinnerungen
        let due = snoozed.filter { $0.until <= now }
        if !due.isEmpty {
            snoozed.removeAll { $0.until <= now }
            try? snoozeStore.save(snoozed)
            let all = accounts.flatMap { account in (messages[account.id] ?? [:]).values.flatMap { $0 } }
            let returning = all.filter { message in due.contains { $0.matches(message) } }
            await setFlag("\\Seen", add: false, on: returning)
            if notificationsEnabled { NotificationService.shared.notify(returning) { [weak self] id in self?.account(id: id)?.email ?? "" } }
        }
        // Geplante Mails
        for entry in outboxStore.due(at: now) {
            await sendScheduled(entry)
        }
        scheduled = outboxStore.list()
    }

    func sendScheduled(_ entry: ScheduledMessage) async {
        guard let account = account(id: entry.accountId), let session = session(for: account) else { return }
        do {
            let raw = try outboxStore.rawMessage(entry)
            let envelope = OutgoingMessage(
                from: MailAddress(name: nil, address: account.email),
                to: (entry.to + entry.cc + entry.bcc).map { MailAddress(name: nil, address: $0) },
                subject: entry.subject, body: "")
            try await session.send(raw: raw, envelope: envelope)
            outboxStore.remove(entry)
            banner = "Geplante Nachricht „\(entry.subject)“ wurde gesendet."
        } catch {
            banner = "Geplante Nachricht konnte nicht gesendet werden: \(Self.describe(error))"
        }
        scheduled = outboxStore.list()
    }

    func deleteScheduled(_ entry: ScheduledMessage) {
        outboxStore.remove(entry)
        scheduled = outboxStore.list()
    }

    // MARK: Verfassen

    func newMessage(to recipient: String = "") {
        guard !accounts.isEmpty else {
            // Ohne Konto zuerst eins hinzufügen (docs/FUNKTIONEN.md §19).
            showAddAccount = true
            return
        }
        guard let firstMailAccount = mailAccounts.first else {
            // Nur Web-Konten: im Webmail selbst schreiben (§20).
            let webAccount = selection?.accountId.flatMap { account(id: $0) } ?? accounts[0]
            selection = .web(webAccount.id, "primary")
            banner = "Schreibe direkt im Postfach – oben auf „Verfassen“ bzw. „Schreiben“ klicken."
            return
        }
        let accountId = selection?.accountId.flatMap { account(id: $0) }.flatMap { $0.isWeb ? nil : $0.id } ?? firstMailAccount.id
        let signature = accountId.flatMap { account(id: $0)?.signature }
        composeDraft = ComposeDraft(accountId: accountId, to: recipient, body: MessageBuilder.bodyWithSignature("", signature: signature))
    }

    func reply(to message: MessageSummary, all: Bool) {
        guard let parsed = parsed(message), let account = account(id: message.accountId) else { return }
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
            body: MessageBuilder.bodyWithSignature(MessageBuilder.quotedBody(original: parsed, intro: intro), signature: account.signature),
            inReplyTo: parsed.messageId,
            references: [parsed.references, parsed.messageId].compactMap { $0 }.joined(separator: " "))
    }

    func forward(_ message: MessageSummary) {
        guard let parsed = parsed(message) else { return }
        let intro = "---------- Weitergeleitete Nachricht ----------\nVon: \(parsed.from.first?.headerValue ?? "")\nBetreff: \(parsed.subject)"
        let attachments = parsed.visibleAttachments.map { OutgoingAttachment(filename: $0.filename, mimeType: $0.mimeType, data: $0.data) }
        composeDraft = ComposeDraft(
            accountId: message.accountId,
            subject: MessageBuilder.prefixedSubject(parsed.subject, prefix: "Fwd:"),
            body: MessageBuilder.bodyWithSignature(MessageBuilder.quotedBody(original: parsed, intro: intro),
                                                   signature: account(id: message.accountId)?.signature),
            attachments: attachments)
    }

    /// Sendet sofort oder legt die Nachricht in den Postausgang („Später senden“).
    func send(_ draft: ComposeDraft, at sendAt: Date? = nil) async throws {
        guard let accountId = draft.accountId, let account = account(id: accountId), let session = session(for: account) else {
            throw UserFacingError("Bitte ein Absender-Konto wählen.")
        }
        let to = AddressParser.parseList(draft.to)
        guard !to.isEmpty else { throw UserFacingError("Bitte mindestens einen gültigen Empfänger angeben.") }
        let message = OutgoingMessage(
            from: MailAddress(name: account.displayName == account.email ? nil : account.displayName, address: account.email),
            to: to, cc: AddressParser.parseList(draft.cc), bcc: AddressParser.parseList(draft.bcc),
            subject: draft.subject, body: draft.body, inReplyTo: draft.inReplyTo,
            references: draft.references?.isEmpty == true ? nil : draft.references,
            attachments: draft.attachments)
        contacts.record(message.allRecipients)
        try? contactStore.save(contacts)
        if let sendAt, sendAt > Date() {
            _ = try outboxStore.add(message, accountId: account.id, sendAt: sendAt)
            scheduled = outboxStore.list()
            banner = "Wird am \(sendAt.formatted(date: .abbreviated, time: .shortened)) gesendet."
            return
        }
        try await session.send(message)
        banner = "Nachricht gesendet."
    }

    func contactSuggestions(for field: String) -> [MailAddress] {
        // Nur der Teil nach dem letzten Komma wird vervollständigt.
        let current = field.split(separator: ",", omittingEmptySubsequences: false).last.map(String.init) ?? field
        return contacts.suggestions(for: current)
    }

    // MARK: oauth.json & Cache

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

    func clearCache() {
        cache.clear()
        banner = "Offline-Cache geleert."
    }

    static func describe(_ error: Error) -> String {
        (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
    }
}
