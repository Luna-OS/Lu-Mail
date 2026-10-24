import LuMailCore
import SwiftUI

struct ContentView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var model = model
        NavigationSplitView {
            SidebarView()
                .navigationSplitViewColumnWidth(min: 230, ideal: 270, max: 360)
        } content: {
            MessageListView()
                .navigationSplitViewColumnWidth(min: 300, ideal: 380, max: 520)
        } detail: {
            MessageDetailView()
        }
        .background(LuTheme.background)
        .searchable(text: $model.searchText, placement: .toolbar, prompt: "Suchen")
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
        .onChange(of: model.selectedMessageID) { _, _ in model.openSelectedMessage() }
        .sheet(isPresented: $model.showAddAccount) {
            AddAccountView().environment(model)
        }
        .sheet(item: $model.composeDraft) { draft in
            ComposeView(draft: draft).environment(model)
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
        .safeAreaInset(edge: .top, spacing: 0) {
            UpdateBar()
        }
        .overlay {
            if model.accounts.isEmpty {
                WelcomeView()
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

/// Erster Start: noch kein Konto.
struct WelcomeView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(spacing: 18) {
            MoonMark(size: 64)
            Text("Willkommen bei Lu-Mail")
                .font(.largeTitle.weight(.semibold))
                .foregroundStyle(LuTheme.textPrimary)
            Text("Gmail, Proton Mail, Mail.ru und Hostinger – alle Postfächer an einem Ort,\nsortiert nach Anbieter, Konto und Kategorie.")
                .multilineTextAlignment(.center)
                .foregroundStyle(LuTheme.textSecondary)
            HStack(spacing: 10) {
                ForEach(model.catalog.providers) { provider in
                    HStack(spacing: 6) {
                        ProviderDot(providerId: provider.id)
                        Text(provider.name).font(.callout)
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(Capsule().fill(LuTheme.surface))
                }
            }
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
