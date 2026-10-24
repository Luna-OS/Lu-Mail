import LuMailCore
import SwiftUI

struct ContentView: View {
    @Environment(AppModel.self) private var model
    @State private var theme = ThemeSettings.shared
    @State private var lock = AppLock.shared

    var body: some View {
        @Bindable var model = model
        NavigationSplitView {
            SidebarView()
                .navigationSplitViewColumnWidth(min: 230, ideal: 280, max: 360)
        } detail: {
            if case .web(let accountId, let folder) = model.selection, let account = model.account(id: accountId) {
                WebAccountView(account: account, folder: folder)
            } else {
                HSplitView {
                    MessageListView()
                        .frame(minWidth: 300, idealWidth: 400, maxWidth: 560)
                    MessageDetailView()
                        .frame(minWidth: 380, maxWidth: .infinity)
                }
            }
        }
        .id(theme.revision)
        .background(LuTheme.background)
        .searchable(text: $model.searchText, placement: .toolbar, prompt: "Suchen (Enter: auch auf dem Server)")
        .onSubmit(of: .search) { Task { await model.searchOnServer() } }
        .onChange(of: model.searchText) { _, newValue in if newValue.isEmpty { model.serverResults = [] } }
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                Button {
                    Task { await model.refreshAll() }
                } label: {
                    Label("Aktualisieren", systemImage: "arrow.clockwise")
                }
                .help("Alle Postfächer aktualisieren (⌘R)")
                Button {
                    model.newMessage()
                } label: {
                    Label("Neue E-Mail", systemImage: "square.and.pencil")
                }
                .help("Neue E-Mail (⌘N)")
                .disabled(model.accounts.isEmpty)
            }
        }
        .onChange(of: model.selection) { _, _ in model.selectionChanged() }
        .onChange(of: model.selectedMessageIDs) { _, _ in model.openSelectedMessage() }
        .sheet(isPresented: $model.showAddAccount) {
            AddAccountView().environment(model)
        }
        .sheet(item: $model.composeDraft) { draft in
            ComposeView(draft: draft).environment(model)
        }
        .safeAreaInset(edge: .top, spacing: 0) {
            VStack(spacing: 0) {
                UpdateBar()
                if model.isOffline {
                    HStack(spacing: 8) {
                        Image(systemName: "wifi.slash")
                        Text("Offline – du siehst die zuletzt geladenen Nachrichten.")
                        Spacer()
                    }
                    .font(.callout)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 6)
                    .background(LuTheme.moon.opacity(0.15))
                }
            }
        }
        .overlay(alignment: .bottom) {
            if let banner = model.banner {
                BannerView(text: banner) { model.banner = nil }
                    .padding(.bottom, 16)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                    .task(id: banner) {
                        try? await Task.sleep(for: .seconds(5))
                        if model.banner == banner { model.banner = nil }
                    }
            }
        }
        .animation(.easeOut(duration: 0.2), value: model.banner)
        .overlay {
            if model.accounts.isEmpty {
                WelcomeView()
            }
        }
        .overlay {
            if lock.isLocked {
                LockView()
            }
        }
    }
}

struct BannerView: View {
    let text: String
    let dismiss: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            MoonMark(size: 14)
            Text(text).font(.callout)
            Button(action: dismiss) {
                Image(systemName: "xmark").font(.caption.weight(.bold))
            }
            .buttonStyle(.plain)
            .foregroundStyle(LuTheme.textSecondary)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).fill(LuTheme.surface))
        .overlay(RoundedRectangle(cornerRadius: LuTheme.radiusMedium).stroke(LuTheme.border))
        .shadow(color: .black.opacity(0.15), radius: 12, y: 4)
    }
}

/// Sperrbildschirm (Touch ID / Passwort).
struct LockView: View {
    @State private var lock = AppLock.shared

    var body: some View {
        VStack(spacing: 16) {
            MoonMark(size: 56)
            Text("Lu-Mail ist gesperrt").font(.title2.weight(.semibold))
            Button {
                lock.unlock()
            } label: {
                Label("Mit \(lock.biometryName) entsperren", systemImage: "lock.open")
                    .padding(.horizontal, 8)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            if let error = lock.lastError {
                Text(error).font(.caption).foregroundStyle(LuTheme.danger)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(.ultraThickMaterial)
        .onAppear { lock.unlock() }
    }
}

/// Erster Start: noch kein Konto.
struct WelcomeView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(spacing: 18) {
            MoonMark(size: 64)
            Text("Willkommen bei Lu-Mail")
                .font(.largeTitle.weight(.semibold))
                .foregroundStyle(LuTheme.textPrimary)
            Text("Alle deine Postfächer an einem Ort –\nsortiert nach Anbieter, Konto und Kategorie.")
                .multilineTextAlignment(.center)
                .foregroundStyle(LuTheme.textSecondary)
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 5), spacing: 8) {
                ForEach(model.catalog.providers) { provider in
                    HStack(spacing: 6) {
                        ProviderDot(providerId: provider.id)
                        Text(provider.name).font(.callout).lineLimit(1)
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(Capsule().fill(LuTheme.surface))
                }
            }
            .frame(maxWidth: 720)
            Button {
                model.showAddAccount = true
            } label: {
                Label("Erstes Konto hinzufügen", systemImage: "plus")
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(LuTheme.background)
    }
}
