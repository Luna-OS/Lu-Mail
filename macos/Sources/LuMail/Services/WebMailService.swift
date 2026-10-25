import AppKit
import LuMailCore
import Observation
import SwiftUI
import WebKit

/// Eingebettete Webmail-Postfächer für Web-Konten (docs/FUNKTIONEN.md §20).
/// Jedes Konto hat einen eigenen, dauerhaften Cookie-Speicher und einen Browser, der im Hintergrund geladen bleibt.
@MainActor
@Observable
final class WebMailService {
    /// Ungelesene im Posteingang je Web-Konto (aus dem Seitentitel).
    private(set) var unread: [UUID: Int] = [:]
    private(set) var canGoBack: Set<UUID> = []
    private(set) var canGoForward: Set<UUID> = []
    private(set) var loading: Set<UUID> = []

    /// Die Anmeldung eines neuen Kontos ist abgeschlossen (Adresse passt zu `loggedInPrefixes`).
    @ObservationIgnored var onLoggedIn: ((UUID) -> Void)?
    /// Im Seitentitel wurde eine E-Mail-Adresse gefunden.
    @ObservationIgnored var onEmailFound: ((UUID, String) -> Void)?
    /// Die Zahl ungelesener Mails ist gestiegen (Konto, Anzahl neuer Mails).
    @ObservationIgnored var onNewMail: ((UUID, Int) -> Void)?
    @ObservationIgnored var onUnreadChanged: (() -> Void)?

    @ObservationIgnored private var entries: [UUID: Entry] = [:]

    /// Lu-Ansicht (§21): gelesene Listen je Konto und Ordner (`key(_:_:)`).
    private(set) var readerItems: [String: [WebReader.Item]] = [:]
    /// Laufende Ladevorgänge je Ordner (Zähler: Neu laden kann einen zweiten starten, solange der erste läuft).
    private(set) var readerLoadCount: [String: Int] = [:]

    func isLoadingReader(_ key: String) -> Bool { (readerLoadCount[key] ?? 0) > 0 }
    /// Warteschlange je Konto, damit Liste und Nachricht nicht gleichzeitig im Browser arbeiten.
    @ObservationIgnored private var queues: [UUID: Task<Void, Never>] = [:]
    /// Konten, deren Seite beim nächsten Umschalten auf die Webseite stehen bleiben soll (offene Nachricht, Verfassen).
    /// Gilt nur für den Ordner, in dem die Seite gehalten wurde, und nur kurz (beim Umschalten auf die Webseite).
    @ObservationIgnored private var holdPage: [UUID: (folder: String, until: Date)] = [:]

    enum ReaderOutcome: Sendable {
        case items([WebReader.Item])
        /// Abgebrochen (z. B. anderer Ordner gewählt) – nichts tun.
        case cancelled
        case needsLogin
        case failed
    }

    static func key(_ accountId: UUID, _ folder: String) -> String { "\(accountId.uuidString)|\(folder)" }

    private final class Entry {
        let webView: WKWebView
        let config: WebConfig
        let coordinator: WebMailCoordinator
        var observations: [NSKeyValueObservation] = []
        var awaitingLogin = false
        var pendingURL: String?
        /// Zeitpunkt der letzten fertig geladenen Seite – kurz danach sind Titel noch im Wandel.
        var settledSince = Date.distantFuture

        init(webView: WKWebView, config: WebConfig, coordinator: WebMailCoordinator) {
            self.webView = webView
            self.config = config
            self.coordinator = coordinator
        }
    }

    // MARK: Browser je Konto

    /// Der Browser eines Kontos (wird bei Bedarf angelegt, lädt aber noch nichts).
    func webView(for accountId: UUID, config: WebConfig) -> WKWebView {
        entry(for: accountId, config: config).webView
    }

    private func entry(for accountId: UUID, config: WebConfig) -> Entry {
        if let entry = entries[accountId] { return entry }
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = WKWebsiteDataStore(forIdentifier: accountId)
        configuration.preferences.javaScriptCanOpenWindowsAutomatically = true
        // Auch im Hintergrund weiterlaufen, damit der Ungelesen-Zähler aktuell bleibt.
        configuration.preferences.inactiveSchedulingPolicy = WKPreferences.InactiveSchedulingPolicy.none
        // Mitschnitt der Webmail-API für die Lu-Ansicht (§21) – im Seitenkontext ab Dokumentbeginn.
        if config.reader != nil {
            configuration.userContentController.addUserScript(WKUserScript(
                source: SharedResources.webmailCaptureJS, injectionTime: .atDocumentStart, forMainFrameOnly: true, in: .page))
        }
        // Feste Größe, damit das Webmail auch unsichtbar im Hintergrund vollständig gerendert wird.
        let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: 1280, height: 900), configuration: configuration)
        webView.customUserAgent = ProviderCatalog.shared.webUserAgent["macos"]
        webView.allowsBackForwardNavigationGestures = true

        let coordinator = WebMailCoordinator(config: config)
        webView.navigationDelegate = coordinator
        webView.uiDelegate = coordinator
        let entry = Entry(webView: webView, config: config, coordinator: coordinator)
        coordinator.onFinish = { [weak self, weak entry] in
            guard let self, let entry else { return }
            entry.settledSince = Date()
            self.titleChanged(accountId, entry)
        }
        entry.observations = [
            webView.observe(\.url) { [weak self, weak entry] _, _ in
                MainActor.assumeIsolated {
                    guard let self, let entry else { return }
                    self.urlChanged(accountId, entry)
                }
            },
            webView.observe(\.title) { [weak self, weak entry] _, _ in
                MainActor.assumeIsolated {
                    guard let self, let entry else { return }
                    self.titleChanged(accountId, entry)
                }
            },
            webView.observe(\.isLoading) { [weak self] view, _ in
                MainActor.assumeIsolated {
                    if view.isLoading { self?.loading.insert(accountId) } else { self?.loading.remove(accountId) }
                }
            },
            webView.observe(\.canGoBack) { [weak self] view, _ in
                MainActor.assumeIsolated {
                    if view.canGoBack { self?.canGoBack.insert(accountId) } else { self?.canGoBack.remove(accountId) }
                }
            },
            webView.observe(\.canGoForward) { [weak self] view, _ in
                MainActor.assumeIsolated {
                    if view.canGoForward { self?.canGoForward.insert(accountId) } else { self?.canGoForward.remove(accountId) }
                }
            },
        ]
        entries[accountId] = entry
        return entry
    }

    /// Lädt das Postfach eines bestehenden Web-Kontos im Hintergrund (beim Start).
    func preload(_ accountId: UUID, config: WebConfig) {
        guard entries[accountId] == nil else { return }
        load(entry(for: accountId, config: config), config.homeUrl)
    }

    /// Öffnet die Anmeldeseite in einem neuen Speicher; `onLoggedIn` meldet den Abschluss.
    func beginLogin(_ accountId: UUID, config: WebConfig) -> WKWebView {
        let entry = entry(for: accountId, config: config)
        entry.awaitingLogin = true
        load(entry, config.loginUrl)
        return entry.webView
    }

    /// Anmeldung abgeschlossen (automatisch erkannt oder „Fertig“ geklickt).
    func finishLogin(_ accountId: UUID) {
        entries[accountId]?.awaitingLogin = false
    }

    /// Zeigt einen Ordner des Postfachs (z. B. `promotions` → Gmail-Tab „Werbung“).
    func open(_ accountId: UUID, config: WebConfig, folder: String) {
        let entry = entry(for: accountId, config: config)
        if let hold = holdPage.removeValue(forKey: accountId), hold.folder == folder, hold.until > Date() { return }
        let target = config.folderURL(folder)
        // Schon in diesem Ordner (auch in einer seiner Nachrichten)?
        if entry.webView.url?.absoluteString.hasPrefix(target) == true { return }
        if entry.pendingURL == target, entry.webView.isLoading || entry.webView.url == nil { return }
        load(entry, target)
    }

    func reload(_ accountId: UUID) { entries[accountId]?.webView.reload() }
    func goBack(_ accountId: UUID) { entries[accountId]?.webView.goBack() }
    func goForward(_ accountId: UUID) { entries[accountId]?.webView.goForward() }

    func currentURL(_ accountId: UUID) -> URL? { entries[accountId]?.webView.url }

    /// Eine Seite des Anbieters öffnen (z. B. „Verfassen“).
    func navigate(_ accountId: UUID, config: WebConfig, to url: String, holdingFolder folder: String) {
        holdPage[accountId] = (folder, Date().addingTimeInterval(5))
        load(entry(for: accountId, config: config), url)
    }

    /// Erneut anmelden: Anmeldeseite im bestehenden Speicher öffnen.
    func showLogin(_ accountId: UUID, config: WebConfig) {
        load(entry(for: accountId, config: config), config.loginUrl)
    }

    /// Browser schließen und den Cookie-Speicher des Kontos löschen.
    func discard(_ accountId: UUID) {
        if let entry = entries.removeValue(forKey: accountId) {
            entry.observations.forEach { $0.invalidate() }
            entry.webView.stopLoading()
            entry.webView.navigationDelegate = nil
            entry.webView.uiDelegate = nil
            entry.webView.removeFromSuperview()
        }
        unread[accountId] = nil
        loading.remove(accountId)
        canGoBack.remove(accountId)
        canGoForward.remove(accountId)
        queues[accountId] = nil
        let prefix = accountId.uuidString + "|"
        readerItems = readerItems.filter { !$0.key.hasPrefix(prefix) }
        onUnreadChanged?()
        // Der Speicher lässt sich erst löschen, wenn kein Browser ihn mehr benutzt.
        Task {
            for _ in 0..<5 {
                try? await Task.sleep(for: .seconds(1))
                do {
                    try await WKWebsiteDataStore.remove(forIdentifier: accountId)
                    return
                } catch {
                    continue
                }
            }
        }
    }

    // MARK: Lu-Ansicht (§21)

    /// Liest die Nachrichtenliste eines Ordners aus dem Webmail.
    func loadList(_ accountId: UUID, config: WebConfig, folder: String) async -> ReaderOutcome {
        guard let reader = config.reader else { return .failed }
        let key = Self.key(accountId, folder)
        readerLoadCount[key, default: 0] += 1
        defer {
            let remaining = (readerLoadCount[key] ?? 1) - 1
            readerLoadCount[key] = remaining > 0 ? remaining : nil
        }
        return await serial(accountId) { [self] in
            if Task.isCancelled { return .cancelled }
            let entry = entry(for: accountId, config: config)
            let target = config.folderURL(folder)
            let current = entry.webView.url?.absoluteString ?? ""
            if current != target {
                if current.hasPrefix(target) {
                    // In einer Nachricht dieses Ordners: zurück zur Liste, ohne die Seite neu zu laden.
                    _ = await evaluate(entry, "history.back(); 'ok'")
                    try? await Task.sleep(for: .milliseconds(800))
                } else if !(entry.pendingURL == target && entry.webView.isLoading) {
                    load(entry, target)
                }
            }
            let start = Date()
            while Date().timeIntervalSince(start) < 20 {
                try? await Task.sleep(for: .milliseconds(700))
                if Task.isCancelled { return .cancelled }
                let elapsed = Date().timeIntervalSince(start)
                let url = entry.webView.url?.absoluteString ?? ""
                if elapsed > 6, !entry.webView.isLoading, !config.loggedInPrefixes.isEmpty, !config.isLoggedIn(url: url) {
                    return .needsLogin
                }
                guard let result = WebReader.decode(WebReader.ListResult.self,
                                                    from: await evaluate(entry, WebReader.listScript(reader: reader))) else { continue }
                switch result.state {
                case .ok where !result.items.isEmpty || elapsed > 4:
                    readerItems[key] = result.items
                    return .items(result.items)
                case .login:
                    return .needsLogin
                case .unsupported:
                    return .failed
                case .error where elapsed > 4:
                    // Erst nach ein paar Sekunden aufgeben – kurz nach dem Laden ist die Seite oft noch unfertig.
                    return .failed
                default:
                    continue
                }
            }
            return .failed
        }
    }

    /// Öffnet eine Nachricht im Webmail und liest ihren Inhalt.
    func loadMessage(_ accountId: UUID, config: WebConfig, item: WebReader.Item) async -> WebReader.MessageResult? {
        guard let reader = config.reader else { return nil }
        return await serial(accountId) { [self] in
            let entry = entry(for: accountId, config: config)
            let opened = WebReader.decode(WebReader.ActionResult.self,
                                          from: await evaluate(entry, WebReader.openScript(reader: reader, id: item.id)))
            guard opened?.state == .ok else { return nil }
            let start = Date()
            var latest: WebReader.MessageResult?
            while Date().timeIntervalSince(start) < 15 {
                try? await Task.sleep(for: .milliseconds(500))
                if Task.isCancelled { return nil }
                guard let message = WebReader.decode(WebReader.MessageResult.self,
                                                     from: await evaluate(entry, WebReader.messageScript(reader: reader))),
                      message.state == .ok, !message.parts.isEmpty else { continue }
                latest = message
                // Bei Ansichten mit Liste und Nachricht nebeneinander kann kurz noch die vorige Nachricht zu sehen sein.
                if Self.subjectsMatch(item.subject, message.subject) { break }
            }
            if latest != nil { markRead(accountId, id: item.id) }
            return latest
        }
    }

    /// Öffnet eine Nachricht im Webmail (für „In der Webseite öffnen“).
    func openInPage(_ accountId: UUID, config: WebConfig, folder: String, item: WebReader.Item) async {
        guard let reader = config.reader else { return }
        await serial(accountId) { [self] in
            let entry = entry(for: accountId, config: config)
            holdPage[accountId] = (folder, Date().addingTimeInterval(5))
            _ = await evaluate(entry, WebReader.openScript(reader: reader, id: item.id))
        }
    }

    private func markRead(_ accountId: UUID, id: String) {
        let prefix = accountId.uuidString + "|"
        for (key, items) in readerItems where key.hasPrefix(prefix) {
            if let index = items.firstIndex(where: { $0.id == id }), items[index].unread {
                readerItems[key]?[index].unread = false
            }
        }
    }

    static func subjectsMatch(_ expected: String, _ actual: String) -> Bool {
        let a = expected.lowercased().trimmingCharacters(in: .whitespaces)
        let b = actual.lowercased().trimmingCharacters(in: .whitespaces)
        return a.isEmpty || b.isEmpty || a.contains(b) || b.contains(a)
    }

    private func serial<T: Sendable>(_ accountId: UUID, _ operation: @escaping @MainActor () async -> T) async -> T {
        let previous = queues[accountId]
        let task = Task { @MainActor () -> T in
            _ = await previous?.value
            return await operation()
        }
        queues[accountId] = Task { @MainActor in _ = await task.value }
        // Wird der Aufrufer abgebrochen (anderer Ordner), bricht auch die Arbeit im Browser ab.
        return await withTaskCancellationHandler {
            await task.value
        } onCancel: {
            task.cancel()
        }
    }

    /// Führt JavaScript in einer eigenen Content-World aus (die Webseite sieht den Leser nicht).
    private func evaluate(_ entry: Entry, _ script: String) async -> String? {
        await withCheckedContinuation { continuation in
            entry.webView.evaluateJavaScript(script, in: nil, in: .defaultClient) { result in
                switch result {
                case .success(let value): continuation.resume(returning: value as? String)
                case .failure: continuation.resume(returning: nil)
                }
            }
        }
    }

    // MARK: Beobachten

    private func load(_ entry: Entry, _ urlString: String) {
        guard let url = URL(string: urlString) else { return }
        entry.pendingURL = urlString
        entry.settledSince = .distantFuture
        entry.webView.load(URLRequest(url: url))
    }

    private func urlChanged(_ accountId: UUID, _ entry: Entry) {
        guard let url = entry.webView.url?.absoluteString else { return }
        if entry.awaitingLogin, entry.config.isLoggedIn(url: url) {
            entry.awaitingLogin = false
            onLoggedIn?(accountId)
        }
        titleChanged(accountId, entry)
    }

    private func titleChanged(_ accountId: UUID, _ entry: Entry) {
        let title = entry.webView.title ?? ""
        if let email = WebConfig.email(fromTitle: title) { onEmailFound?(accountId, email) }
        guard !entry.awaitingLogin, let url = entry.webView.url?.absoluteString, entry.config.isOnInbox(url: url) else { return }
        // Keine Zahl im Titel im fertig geladenen Posteingang = nichts ungelesen.
        guard let count = entry.config.unreadCount(fromTitle: title) ?? (entry.webView.isLoading ? nil : 0) else { return }
        let previous = unread[accountId]
        guard previous != count else { return }
        unread[accountId] = count
        onUnreadChanged?()
        if let previous, count > previous, Date().timeIntervalSince(entry.settledSince) > 15 {
            onNewMail?(accountId, count - previous)
        }
    }
}

/// Navigation, Pop-ups, Downloads und JavaScript-Dialoge eines Webmail-Browsers.
@MainActor
final class WebMailCoordinator: NSObject, WKNavigationDelegate, WKUIDelegate, WKDownloadDelegate {
    let config: WebConfig
    var onFinish: (() -> Void)?

    init(config: WebConfig) {
        self.config = config
    }

    /// Externe Seiten im Standardbrowser öffnen; Weiterleitungs-Links („google.com/url?q=…“) direkt zum Ziel.
    private func openExternally(_ url: URL) {
        let target = WebConfig.redirectTarget(of: url.absoluteString).flatMap(URL.init(string:)) ?? url
        NSWorkspace.shared.open(target)
    }

    private func isExternal(_ url: URL) -> Bool {
        guard let scheme = url.scheme?.lowercased(), scheme == "http" || scheme == "https" else { return false }
        return !config.isInternal(url: url.absoluteString) || WebConfig.redirectTarget(of: url.absoluteString) != nil
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction) async -> WKNavigationActionPolicy {
        if navigationAction.shouldPerformDownload { return .download }
        guard let url = navigationAction.request.url else { return .allow }
        if let scheme = url.scheme?.lowercased(), scheme == "mailto" || scheme == "tel" {
            NSWorkspace.shared.open(url)
            return .cancel
        }
        // Angeklickte Links zu fremden Seiten gehören in den Standardbrowser; Anmelde-Weiterleitungen bleiben hier.
        if navigationAction.navigationType == .linkActivated, navigationAction.targetFrame?.isMainFrame != false, isExternal(url) {
            openExternally(url)
            return .cancel
        }
        return .allow
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse) async -> WKNavigationResponsePolicy {
        navigationResponse.canShowMIMEType ? .allow : .download
    }

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) {
        download.delegate = self
    }

    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) {
        download.delegate = self
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        onFinish?()
    }

    // MARK: Pop-ups und Dialoge

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        guard let url = navigationAction.request.url, url.absoluteString != "about:blank" else { return nil }
        if isExternal(url) {
            openExternally(url)
        } else {
            webView.load(navigationAction.request)
        }
        return nil
    }

    func webView(_ webView: WKWebView, runOpenPanelWith parameters: WKOpenPanelParameters,
                 initiatedByFrame frame: WKFrameInfo) async -> [URL]? {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = parameters.allowsMultipleSelection
        panel.canChooseDirectories = parameters.allowsDirectories
        panel.canChooseFiles = true
        return panel.runModal() == .OK ? panel.urls : nil
    }

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo) async {
        let alert = NSAlert()
        alert.messageText = message
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo) async -> Bool {
        let alert = NSAlert()
        alert.messageText = message
        alert.addButton(withTitle: "OK")
        alert.addButton(withTitle: "Abbrechen")
        return alert.runModal() == .alertFirstButtonReturn
    }

    // MARK: Downloads (Anhänge) → Download-Ordner

    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse,
                  suggestedFilename: String) async -> URL? {
        let folder = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        let name = suggestedFilename.isEmpty ? "Anhang" : suggestedFilename
        var candidate = folder.appendingPathComponent(name)
        let base = candidate.deletingPathExtension().lastPathComponent
        let ext = candidate.pathExtension
        var index = 2
        while FileManager.default.fileExists(atPath: candidate.path) {
            candidate = folder.appendingPathComponent(ext.isEmpty ? "\(base) \(index)" : "\(base) \(index).\(ext)")
            index += 1
        }
        return candidate
    }

    func downloadDidFinish(_ download: WKDownload) {
        NSSound(named: "Glass")?.play()
    }
}

/// Bettet einen (wiederverwendeten) WKWebView in SwiftUI ein.
/// `hidden`: unsichtbar und nicht anklickbar, aber im Fenster – so rendert das Webmail für die Lu-Ansicht weiter.
struct WebViewHost: NSViewRepresentable {
    let webView: WKWebView
    var hidden = false

    final class Container: NSView {
        var passthrough = false

        override func hitTest(_ point: NSPoint) -> NSView? {
            passthrough ? nil : super.hitTest(point)
        }
    }

    func makeNSView(context: Context) -> Container {
        let container = Container()
        attach(to: container)
        return container
    }

    func updateNSView(_ container: Container, context: Context) {
        if webView.superview !== container {
            container.subviews.forEach { $0.removeFromSuperview() }
            attach(to: container)
        }
        container.passthrough = hidden
        webView.alphaValue = hidden ? 0 : 1
    }

    static func dismantleNSView(_ container: Container, coordinator: ()) {
        container.subviews.forEach { $0.removeFromSuperview() }
    }

    private func attach(to container: Container) {
        container.passthrough = hidden
        webView.alphaValue = hidden ? 0 : 1
        webView.frame = container.bounds
        webView.autoresizingMask = [.width, .height]
        container.addSubview(webView)
    }
}
