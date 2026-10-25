import LuMailCore
import SwiftUI

/// Animierter Nachthimmel mit Mond (docs/FUNKTIONEN.md §22).
///
/// Mehrere Ebenen (Seitenleiste, Liste, leerer Lesebereich) zeichnen denselben Himmel in Fensterkoordinaten,
/// damit er über die Spalten hinweg nahtlos wirkt. Lage und Fenstergröße liefert AppKit (`WindowFrameReader`) –
/// die Spalten von NavigationSplitView liegen in eigenen Host-Views, SwiftUI-Koordinaten passen dort nicht zusammen.
struct NightSkyLayer: View {
    /// Auch im hellen Modus zeigen (Willkommens- und Sperrbildschirm).
    var alwaysVisible = false
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var theme = ThemeSettings.shared
    @State private var placement = WindowFrameReader.Placement()

    var body: some View {
        if theme.nightSky && (alwaysVisible || colorScheme == .dark) {
            let haze = LuTheme.accent.opacity(0.08)
            let current = placement
            TimelineView(.animation(minimumInterval: 1.0 / 30, paused: reduceMotion)) { timeline in
                Canvas { context, size in
                    let known = current.window != .zero
                    NightSky.draw(in: &context, canvas: size,
                                  origin: known ? current.frame.origin : .zero,
                                  sky: known ? current.window : size,
                                  haze: haze, time: timeline.date.timeIntervalSinceReferenceDate)
                }
            }
            .background(WindowFrameReader(placement: $placement))
            .ignoresSafeArea()
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
    }
}

/// Meldet die Lage einer Ebene im Fenster (oben links = 0,0) und die Größe des Fensterinhalts.
struct WindowFrameReader: NSViewRepresentable {
    struct Placement: Equatable {
        var frame: CGRect = .zero
        var window: CGSize = .zero
    }

    @Binding var placement: Placement

    func makeNSView(context: Context) -> ReaderView {
        let view = ReaderView()
        view.onChange = { newValue in
            // Nicht während eines SwiftUI-Updates ändern.
            DispatchQueue.main.async { if placement != newValue { placement = newValue } }
        }
        return view
    }

    func updateNSView(_ view: ReaderView, context: Context) {
        view.report()
    }

    final class ReaderView: NSView {
        var onChange: ((Placement) -> Void)?
        private var observers: [NSObjectProtocol] = []

        override func viewWillMove(toWindow newWindow: NSWindow?) {
            super.viewWillMove(toWindow: newWindow)
            for observer in observers { NotificationCenter.default.removeObserver(observer) }
            observers = []
        }

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            if let window {
                // Ändert sich nur die Fenstergröße (nicht diese Ebene), verschiebt sich trotzdem der Himmel.
                observers.append(NotificationCenter.default.addObserver(forName: NSWindow.didResizeNotification, object: window,
                                                                        queue: .main) { [weak self] _ in
                    MainActor.assumeIsolated { self?.report() }
                })
            }
            report()
        }

        override func layout() {
            super.layout()
            report()
        }

        override func setFrameSize(_ newSize: NSSize) {
            super.setFrameSize(newSize)
            report()
        }

        func report() {
            guard let content = window?.contentView else { return }
            let rect = convert(bounds, to: content)
            let top = content.isFlipped ? rect.minY : content.bounds.height - rect.maxY
            onChange?(Placement(frame: CGRect(x: rect.minX, y: top, width: rect.width, height: rect.height),
                                window: content.bounds.size))
        }
    }
}

enum NightSky {
    struct Star {
        var x: Double
        var y: Double
        var radius: Double
        var period: Double
        var phase: Double
        var tint: Color
    }

    /// 160 Sterne mit festem Startwert – bei jedem Start gleich.
    static let stars: [Star] = {
        var generator = SeededGenerator(seed: 0x4C75_4D61_696C)
        let tints = [Color.white, Color(hex: "#DCE6FF"), Color(hex: "#FFF1D6")]
        return (0..<160).map { _ in
            Star(x: generator.unit(), y: generator.unit(),
                 radius: 0.4 + generator.unit() * 0.8,
                 period: 2 + generator.unit() * 4,
                 phase: generator.unit() * .pi * 2,
                 tint: tints[Int(generator.unit() * Double(tints.count)) % tints.count])
        }
    }()

    // Farben einmal anlegen statt in jedem Bild neu zu parsen.
    private static let skyGradient = Gradient(colors: [Color(hex: "#070A1C"), Color(hex: "#141638"), Color(hex: "#24205A")])
    private static let moonGlow = Color(hex: "#FFE7A3")
    private static let moonGradient = Gradient(colors: [Color(hex: "#FFF4D6"), Color(hex: "#F5D06F")])
    private static let crater = Color(hex: "#D9AE4A").opacity(0.35)

    static func draw(in context: inout GraphicsContext, canvas: CGSize, origin: CGPoint, sky: CGSize, haze: Color,
                     time: TimeInterval) {
        let w = sky.width
        let h = sky.height
        guard w > 0, h > 0 else { return }
        let skyRect = CGRect(x: -origin.x, y: -origin.y, width: w, height: h)
        let canvasRect = CGRect(origin: .zero, size: canvas)

        // Himmel und violetter Schleier – die ganze Ebene füllen (der Verlauf richtet sich nach dem Fenster).
        context.fill(Path(canvasRect), with: .linearGradient(
            skyGradient,
            startPoint: CGPoint(x: skyRect.midX, y: skyRect.minY), endPoint: CGPoint(x: skyRect.midX, y: skyRect.maxY)))
        let hazeCenter = CGPoint(x: skyRect.minX + w * 0.15, y: skyRect.minY + h * 0.95)
        context.fill(Path(canvasRect), with: .radialGradient(
            Gradient(colors: [haze, .clear]),
            center: hazeCenter, startRadius: 0, endRadius: max(w, h) * 0.6))

        // Sterne (funkeln, die Ebene driftet langsam) – nur die in dieser Ebene sichtbaren zeichnen.
        let drift = 12 * sin(time * 2 * .pi / 90)
        let visible = canvasRect
        for star in stars {
            let point = CGPoint(x: skyRect.minX + star.x * w + drift, y: skyRect.minY + star.y * h)
            guard visible.insetBy(dx: -6, dy: -6).contains(point) else { continue }
            let alpha = 0.25 + 0.75 * (0.5 + 0.5 * sin(time * 2 * .pi / star.period + star.phase))
            if star.radius > 0.9 {
                let glow = star.radius * 3
                context.fill(Path(ellipseIn: CGRect(x: point.x - glow, y: point.y - glow, width: glow * 2, height: glow * 2)),
                             with: .color(star.tint.opacity(alpha * 0.15)))
            }
            context.fill(Path(ellipseIn: CGRect(x: point.x - star.radius, y: point.y - star.radius,
                                                width: star.radius * 2, height: star.radius * 2)),
                         with: .color(star.tint.opacity(alpha)))
        }

        drawShootingStar(in: &context, skyRect: skyRect, time: time)
        drawMoon(in: &context, skyRect: skyRect, time: time)
    }

    private static func drawMoon(in context: inout GraphicsContext, skyRect: CGRect, time: TimeInterval) {
        let r = 38.0
        let center = CGPoint(x: skyRect.minX + skyRect.width * 0.82,
                             y: skyRect.minY + skyRect.height * 0.16 + 3 * sin(time * 2 * .pi / 8))
        let glowAlpha = 0.25 + 0.15 * (0.5 + 0.5 * sin(time * 2 * .pi / 6))
        let glowRect = CGRect(x: center.x - r * 3, y: center.y - r * 3, width: r * 6, height: r * 6)
        context.fill(Path(ellipseIn: glowRect), with: .radialGradient(
            Gradient(colors: [moonGlow.opacity(glowAlpha), .clear]),
            center: center, startRadius: r * 0.8, endRadius: r * 3))
        let body = CGRect(x: center.x - r, y: center.y - r, width: r * 2, height: r * 2)
        context.fill(Path(ellipseIn: body), with: .radialGradient(
            moonGradient,
            center: CGPoint(x: center.x - r * 0.35, y: center.y - r * 0.35), startRadius: 0, endRadius: r * 1.6))
        for (dx, dy, cr) in [(0.30, -0.25, 0.16), (-0.25, 0.30, 0.12), (0.10, 0.42, 0.09)] {
            let c = CGPoint(x: center.x + dx * r, y: center.y + dy * r)
            context.fill(Path(ellipseIn: CGRect(x: c.x - cr * r, y: c.y - cr * r, width: cr * r * 2, height: cr * r * 2)),
                         with: .color(crater))
        }
    }

    /// Eine Sternschnuppe pro 13,5-s-Fenster, Start zufällig in den ersten 4,5 s – Abstände also 9–18 s; 0,9 s lang.
    private static func drawShootingStar(in context: inout GraphicsContext, skyRect: CGRect, time: TimeInterval) {
        let window = 13.5
        let index = floor(time / window)
        let offset = hash(index, 1) * 4.5
        let progress = (time - index * window - offset) / 0.9
        guard progress >= 0, progress <= 1 else { return }
        let start = CGPoint(x: skyRect.minX + skyRect.width * (0.3 + 0.6 * hash(index, 2)),
                            y: skyRect.minY + skyRect.height * (0.05 + 0.3 * hash(index, 3)))
        let direction = CGVector(dx: -0.894, dy: 0.447)
        let travel = 260.0
        let head = CGPoint(x: start.x + direction.dx * travel * progress, y: start.y + direction.dy * travel * progress)
        let tail = CGPoint(x: head.x - direction.dx * 90, y: head.y - direction.dy * 90)
        var path = Path()
        path.move(to: tail)
        path.addLine(to: head)
        let fade = 1 - progress
        context.stroke(path, with: .linearGradient(Gradient(colors: [.clear, Color.white.opacity(0.9 * fade)]),
                                                   startPoint: tail, endPoint: head),
                       style: StrokeStyle(lineWidth: 1.6, lineCap: .round))
    }

    private static func hash(_ index: Double, _ salt: Double) -> Double {
        let value = sin(index * 12.9898 + salt * 78.233) * 43758.5453
        return value - floor(value)
    }
}

/// Kleiner, deterministischer Zufallsgenerator (für feste Sternpositionen).
struct SeededGenerator {
    private var state: UInt64

    init(seed: UInt64) { state = seed }

    mutating func next() -> UInt64 {
        state = state &* 6_364_136_223_846_793_005 &+ 1_442_695_040_888_963_407
        return state
    }

    mutating func unit() -> Double {
        Double(next() >> 11) / Double(1 << 53)
    }
}
