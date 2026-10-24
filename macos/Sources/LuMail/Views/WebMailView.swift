import AppKit
import LuMailCore
import SwiftUI
import WebKit

/// Das Webmail eines Web-Kontos anstelle von Liste und Lesebereich (docs/FUNKTIONEN.md §20).
struct WebMailView: View {
    @Environment(AppModel.self) private var model
    let account: Account
    let folder: String

    var body: some View {
        if let provider = model.provider(for: account), let web = provider.web {
            let webMail = model.webMail
            VStack(spacing: 0) {
                HStack(spacing: 6) {
                    Button { webMail.goBack(account.id) } label: { Image(systemName: "chevron.left") }
                        .disabled(!webMail.canGoBack.contains(account.id))
                        .help("Zurück")
                    Button { webMail.goForward(account.id) } label: { Image(systemName: "chevron.right") }
                        .disabled(!webMail.canGoForward.contains(account.id))
                        .help("Vor")
                    Button { webMail.reload(account.id) } label: { Image(systemName: "arrow.clockwise") }
                        .help("Neu laden")
                    if webMail.loading.contains(account.id) {
                        ProgressView().controlSize(.small)
                    }
                    Spacer(minLength: 8)
                    ProviderDot(providerId: provider.id)
                    Text(account.label)
                        .font(.callout)
                        .foregroundStyle(LuTheme.textSecondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Button {
                        if let url = webMail.currentURL(account.id) { NSWorkspace.shared.open(url) }
                    } label: {
                        Image(systemName: "safari")
                    }
                    .help("Im Standardbrowser öffnen")
                }
                .buttonStyle(.borderless)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(LuTheme.surface)
                Divider().overlay(LuTheme.border)
                WebViewHost(webView: webMail.webView(for: account.id, config: web))
            }
            .navigationTitle(model.title)
            .task(id: "\(account.id.uuidString)|\(folder)") {
                webMail.open(account.id, config: web, folder: folder)
            }
        } else {
            Text("Für diesen Anbieter gibt es kein Webmail.")
                .foregroundStyle(LuTheme.textSecondary)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

/// Anmeldung auf der Webseite des Anbieters im eingebetteten Browser (Schritt im Assistenten).
struct WebLoginView: View {
    let provider: Provider
    let webView: WKWebView

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                Text(String(provider.name.prefix(1)))
                    .font(.headline)
                    .foregroundStyle(.white)
                    .frame(width: 28, height: 28)
                    .background(RoundedRectangle(cornerRadius: 7).fill(LuTheme.provider(provider.id).gradient))
                VStack(alignment: .leading, spacing: 1) {
                    Text("Melde dich bei \(provider.name) an").font(.headline)
                    Text("Ganz normal mit deinem Passwort – Lu-Mail speichert es nicht. Danach geht es automatisch weiter, sonst auf „Fertig“ klicken.")
                        .font(.caption)
                        .foregroundStyle(LuTheme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            WebViewHost(webView: webView)
                .clipShape(RoundedRectangle(cornerRadius: LuTheme.radiusMedium))
                .overlay(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).stroke(LuTheme.border))
        }
    }
}
