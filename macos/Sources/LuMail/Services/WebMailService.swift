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
        let webView = WKWebView(frame: .zero, configuration: configuration)
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
        let target = config.folderURL(folder)
        if entry.webView.url?.absoluteString == target { return }
        if entry.pendingURL == target, entry.webView.isLoading || entry.webView.url == nil { return }
        load(entry, target)
    }

    func reload(_ accountId: UUID) { entries[accountId]?.webView.reload() }
    func goBack(_ accountId: UUID) { entries[accountId]?.webView.goBack() }
    func goForward(_ accountId: UUID) { entries[accountId]?.webView.goForward() }

    func currentURL(_ accountId: UUID) -> URL? { entries[accountId]?.webView.url }

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
struct WebViewHost: NSViewRepresentable {
    let webView: WKWebView

    func makeNSView(context: Context) -> NSView {
        let container = NSView()
        attach(to: container)
        return container
    }

    func updateNSView(_ container: NSView, context: Context) {
        if webView.superview !== container {
            container.subviews.forEach { $0.removeFromSuperview() }
            attach(to: container)
        }
    }

    static func dismantleNSView(_ container: NSView, coordinator: ()) {
        container.subviews.forEach { $0.removeFromSuperview() }
    }

    private func attach(to container: NSView) {
        webView.frame = container.bounds
        webView.autoresizingMask = [.width, .height]
        container.addSubview(webView)
    }
}
