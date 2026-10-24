// swift-tools-version:5.9
import PackageDescription

var products: [Product] = [
    .library(name: "LuMailCore", targets: ["LuMailCore"]),
]

var targets: [Target] = [
    // Plattformunabhängiger Kern: IMAP/SMTP-Protokoll, MIME, Sortierung, OAuth, Browser-Login.
    // Läuft auch unter Linux, damit die Tests überall laufen.
    .target(name: "LuMailCore"),
    .testTarget(name: "LuMailCoreTests", dependencies: ["LuMailCore"]),
]

#if os(macOS)
// Die SwiftUI-App gibt es nur auf dem Mac.
products.append(.executable(name: "LuMail", targets: ["LuMail"]))
targets.append(.executableTarget(name: "LuMail", dependencies: ["LuMailCore"]))
#endif

let package = Package(
    name: "LuMail",
    defaultLocalization: "de",
    platforms: [.macOS(.v14)],
    products: products,
    targets: targets
)
