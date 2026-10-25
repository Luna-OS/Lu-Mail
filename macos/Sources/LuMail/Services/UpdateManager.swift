import AppKit
import Foundation
import LuMailCore
import Observation

/// Auto-Updater für macOS (docs/FUNKTIONEN.md §23, wie Moon Browser): prüft GitHub-Releases, lädt das DMG im
/// Hintergrund, prüft Prüfsumme, Bundle-ID, Version und Signatur der neuen App und ersetzt beim Klick auf „Update“
/// bzw. beim Beenden das laufende Bundle. Lu-Mail ist nicht mit einer Developer-ID signiert, deshalb macht die App
/// das selbst statt über das System.
@MainActor
@Observable
final class UpdateManager {
    static let shared = UpdateManager()

    enum Mode: String, CaseIterable, Identifiable {
        case automatic, notify, off
        var id: String { rawValue }
        var title: String {
            switch self {
            case .automatic: return "Automatisch installieren"
            case .notify: return "Nur benachrichtigen"
            case .off: return "Aus"
            }
        }
    }

    enum State: Equatable {
        case idle
        case checking
        case upToDate
        case available(UpdateInfo)
        case downloading(UpdateInfo)
        /// Neue App liegt entpackt bereit.
        case ready(UpdateInfo)
        case failed(String)
    }

    /// Ändern über `setMode(_:)`, damit die Einstellung gespeichert wird.
    private(set) var mode: Mode = Mode(rawValue: UserDefaults.standard.string(forKey: "updateMode") ?? "") ?? .automatic
    private(set) var state: State = .idle
    private(set) var lastCheck: Date?

    let currentVersion: SemanticVersion
    /// Nur eine echte .app kann sich selbst ersetzen (nicht `swift run`).
    let isAppBundle: Bool
    private let config = UpdateConfig.shared
    private var stagedApp: URL?
    private var downloadedDMG: URL?
    private var loopTask: Task<Void, Never>?
    /// Wird vor dem Neustart in die neue Version aufgerufen (z. B. um die Auswahl zu merken).
    var onBeforeRelaunch: (() -> Void)?

    static let bundleIdentifier = "de.lunaos.lumail"
    /// Terminal-Befehl, der Lu-Mail an Ort und Stelle aktualisiert (scripts/update-mac.sh).
    static let terminalCommand = "curl -fsSL https://github.com/Luna-OS/Lu-Mail/releases/latest/download/update-mac.sh | bash"

    private var workDirectory: URL {
        FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("LuMail/Update", isDirectory: true)
    }

    private init() {
        let versionString = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
        isAppBundle = Bundle.main.bundleURL.pathExtension == "app"
        currentVersion = isAppBundle ? (SemanticVersion(versionString) ?? SemanticVersion(major: 0, minor: 0, patch: 0))
                                     : SemanticVersion(major: 0, minor: 0, patch: 0)
    }

    var automaticChecksEnabled: Bool {
        mode != .off && isAppBundle && !currentVersion.isDevelopmentBuild
    }

    func setMode(_ newMode: Mode) {
        mode = newMode
        UserDefaults.standard.set(newMode.rawValue, forKey: "updateMode")
        start()
    }

    /// Startet die regelmäßige Prüfung (erst nach `initialDelaySeconds`).
    func start() {
        loopTask?.cancel()
        guard automaticChecksEnabled else { return }
        let delay = config.initialDelaySeconds
        let interval = max(1, config.checkIntervalHours) * 3600
        loopTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(delay))
            while !Task.isCancelled {
                await self?.check(userInitiated: false)
                try? await Task.sleep(for: .seconds(interval))
            }
        }
    }

    private var service: UpdateService {
        UpdateService(config: config, currentVersion: currentVersion, platform: "macos")
    }

    func check(userInitiated: Bool) async {
        switch state {
        case .checking, .downloading, .ready: return
        default: break
        }
        state = .checking
        do {
            lastCheck = Date()
            guard let update = try await service.check() else {
                state = .upToDate
                return
            }
            if mode == .automatic && !userInitiated {
                await download(update)
            } else {
                state = .available(update)
            }
        } catch {
            state = userInitiated ? .failed((error as? LocalizedError)?.errorDescription ?? error.localizedDescription) : .idle
        }
    }

    /// Lädt und entpackt das Update; danach `.ready`.
    func download(_ update: UpdateInfo) async {
        state = .downloading(update)
        do {
            let directory = workDirectory
            try? FileManager.default.removeItem(at: directory)
            let dmg = try await service.download(update, to: directory)
            downloadedDMG = dmg
            let version = update.version.description
            stagedApp = try await Task.detached { try Self.extractApp(from: dmg, into: directory, expectedVersion: version) }.value
            state = .ready(update)
        } catch {
            state = .failed((error as? LocalizedError)?.errorDescription ?? error.localizedDescription)
        }
    }

    /// Kann sich diese Kopie selbst ersetzen? Nicht aus dem Disk-Image, nicht aus der App Translocation (macOS
    /// startet heruntergeladene Apps manchmal von einem Zufallsort) und nur in einem beschreibbaren Ordner.
    var canReplaceItself: Bool {
        let bundle = Bundle.main.bundleURL
        let path = bundle.path
        guard isAppBundle, !path.hasPrefix("/Volumes/"), !path.contains("/AppTranslocation/") else { return false }
        let manager = FileManager.default
        return manager.isWritableFile(atPath: bundle.deletingLastPathComponent().path) && manager.isWritableFile(atPath: path)
    }

    /// „Update“ bzw. „Jetzt neu starten“.
    func installAndRelaunch() {
        guard case .ready(let update) = state else { return }
        guard canReplaceItself else {
            // Wie Moon Browser: aus dem Disk-Image bzw. ohne Schreibrecht die Download-Seite öffnen.
            if let page = update.releasePage { NSWorkspace.shared.open(page) } else if let downloadedDMG { NSWorkspace.shared.open(downloadedDMG) }
            state = .failed("Lu-Mail kann sich hier nicht selbst aktualisieren – bitte die App aus dem Disk-Image nach „Programme“ ziehen und von dort starten.")
            return
        }
        onBeforeRelaunch?()
        if launchInstaller(relaunch: true) {
            NSApplication.shared.terminate(nil)
        }
    }

    /// Wird beim Beenden aufgerufen: liegt ein Update bereit, wird es still eingespielt (ohne Neustart).
    func installOnQuitIfReady() {
        guard case .ready = state, canReplaceItself else { return }
        _ = launchInstaller(relaunch: false)
    }

    // MARK: Installation

    private nonisolated static func run(_ tool: String, _ arguments: [String]) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            throw UpdateError.installFailed("\(URL(fileURLWithPath: tool).lastPathComponent) beendet mit Code \(process.terminationStatus)")
        }
    }

    private nonisolated static func output(_ tool: String, _ arguments: [String]) throws -> String {
        let process = Process()
        let pipe = Pipe()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        try process.run()
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            throw UpdateError.installFailed("\(URL(fileURLWithPath: tool).lastPathComponent) beendet mit Code \(process.terminationStatus)")
        }
        return String(decoding: data, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Prüft die neue App wie Moon Browser: richtige Bundle-ID, angekündigte Version, gültige Signatur.
    private nonisolated static func verify(app: URL, expectedVersion: String) throws {
        let plist = app.appendingPathComponent("Contents/Info.plist").path
        let identifier = try output("/usr/bin/plutil", ["-extract", "CFBundleIdentifier", "raw", "-o", "-", plist])
        guard identifier == bundleIdentifier else { throw UpdateError.installFailed("Das Update ist nicht Lu-Mail") }
        let version = try output("/usr/bin/plutil", ["-extract", "CFBundleShortVersionString", "raw", "-o", "-", plist])
        guard version == expectedVersion else {
            throw UpdateError.installFailed("Das Update hat Version \(version) statt \(expectedVersion)")
        }
        do {
            try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
        } catch {
            throw UpdateError.installFailed("Die Signatur des Updates ist beschädigt")
        }
    }

    /// Hängt das DMG ein, kopiert `Lu-Mail.app` nach `directory/staged/`, hängt es wieder aus und prüft die App.
    private nonisolated static func extractApp(from dmg: URL, into directory: URL, expectedVersion: String) throws -> URL {
        let mountPoint = directory.appendingPathComponent("mount", isDirectory: true)
        let staging = directory.appendingPathComponent("staged", isDirectory: true)
        try FileManager.default.createDirectory(at: mountPoint, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: staging, withIntermediateDirectories: true)
        try run("/usr/bin/hdiutil", ["attach", dmg.path, "-nobrowse", "-readonly", "-noautoopen", "-quiet", "-mountpoint", mountPoint.path])
        defer { try? run("/usr/bin/hdiutil", ["detach", mountPoint.path, "-force", "-quiet"]) }
        let contents = try FileManager.default.contentsOfDirectory(at: mountPoint, includingPropertiesForKeys: nil)
        guard let app = contents.first(where: { $0.pathExtension == "app" }) else {
            throw UpdateError.installFailed("Im Disk-Image wurde keine App gefunden")
        }
        let target = staging.appendingPathComponent(app.lastPathComponent)
        try run("/usr/bin/ditto", [app.path, target.path])
        try verify(app: target, expectedVersion: expectedVersion)
        return target
    }

    /// Startet ein Hilfsskript, das auf das Ende von Lu-Mail wartet und dann das Bundle austauscht.
    /// Gibt `false` zurück, wenn ein Austausch nicht möglich ist (dann wird das DMG geöffnet).
    private func launchInstaller(relaunch: Bool) -> Bool {
        let destination = Bundle.main.bundleURL
        guard let stagedApp, canReplaceItself else {
            if let downloadedDMG { NSWorkspace.shared.open(downloadedDMG) }
            state = .failed("Lu-Mail kann sich an diesem Ort nicht selbst aktualisieren. Das Disk-Image wurde geöffnet – bitte die App nach „Programme“ ziehen.")
            return false
        }
        let script = """
        #!/bin/bash
        PID="$1"; SRC="$2"; DST="$3"; RELAUNCH="$4"
        for _ in $(seq 1 300); do kill -0 "$PID" 2>/dev/null || break; sleep 0.2; done
        rm -rf "$DST.lumail-old"
        if mv "$DST" "$DST.lumail-old" && /usr/bin/ditto "$SRC" "$DST"; then
          rm -rf "$DST.lumail-old"
        else
          rm -rf "$DST"; mv "$DST.lumail-old" "$DST"
        fi
        /usr/bin/xattr -dr com.apple.quarantine "$DST" 2>/dev/null
        /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$DST" >/dev/null 2>&1
        rm -rf "$SRC"
        if [ "$RELAUNCH" = "1" ]; then /usr/bin/open "$DST"; fi
        """
        do {
            let scriptURL = workDirectory.appendingPathComponent("install.sh")
            try script.write(to: scriptURL, atomically: true, encoding: .utf8)
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/bin/sh")
            // nohup + & löst das Skript von Lu-Mail, damit es nach dem Beenden weiterläuft.
            let quoted = [scriptURL.path, String(ProcessInfo.processInfo.processIdentifier), stagedApp.path, destination.path, relaunch ? "1" : "0"]
                .map { "'" + $0.replacingOccurrences(of: "'", with: "'\\''") + "'" }
                .joined(separator: " ")
            process.arguments = ["-c", "nohup /bin/bash \(quoted) >/dev/null 2>&1 &"]
            try process.run()
            process.waitUntilExit()
            return true
        } catch {
            state = .failed("Update konnte nicht gestartet werden: \(error.localizedDescription)")
            return false
        }
    }
}

/// Fängt das Beenden der App ab, um ein bereitliegendes Update einzuspielen.
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        MainActor.assumeIsolated { UpdateManager.shared.start() }
    }

    func applicationWillTerminate(_ notification: Notification) {
        MainActor.assumeIsolated { UpdateManager.shared.installOnQuitIfReady() }
    }
}
