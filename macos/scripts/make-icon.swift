// Zeichnet das Lu-Mail-Symbol (Mond + Briefumschlag im Luna-Theme) als .iconset.
// Aufruf: swift scripts/make-icon.swift <Zielordner.iconset>
import AppKit

let output = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "AppIcon.iconset")
try? FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

func color(_ hex: UInt32) -> NSColor {
    NSColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
}

func render(size: Int) -> Data {
    let s = CGFloat(size)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)

    // Hintergrund: abgerundetes Quadrat mit Nachthimmel-Verlauf
    let inset = s * 0.09
    let rect = NSRect(x: inset, y: inset, width: s - 2 * inset, height: s - 2 * inset)
    let background = NSBezierPath(roundedRect: rect, xRadius: rect.width * 0.23, yRadius: rect.width * 0.23)
    NSGradient(starting: color(0x2D2A5C), ending: color(0x0F1020))!.draw(in: background, angle: -90)

    // Briefumschlag
    let envelope = NSRect(x: rect.minX + rect.width * 0.2, y: rect.minY + rect.height * 0.2,
                          width: rect.width * 0.6, height: rect.height * 0.42)
    let body = NSBezierPath(roundedRect: envelope, xRadius: envelope.width * 0.08, yRadius: envelope.width * 0.08)
    color(0xECEBFF).setFill()
    body.fill()
    let flap = NSBezierPath()
    flap.move(to: NSPoint(x: envelope.minX + envelope.width * 0.04, y: envelope.maxY - envelope.height * 0.06))
    flap.line(to: NSPoint(x: envelope.midX, y: envelope.minY + envelope.height * 0.42))
    flap.line(to: NSPoint(x: envelope.maxX - envelope.width * 0.04, y: envelope.maxY - envelope.height * 0.06))
    flap.lineWidth = max(1, s * 0.025)
    flap.lineJoinStyle = .round
    flap.lineCapStyle = .round
    color(0x8B7CFF).setStroke()
    flap.stroke()

    // Mondsichel oben rechts
    let moonSize = rect.width * 0.34
    let moonRect = NSRect(x: rect.maxX - moonSize - rect.width * 0.1, y: rect.maxY - moonSize - rect.height * 0.08,
                          width: moonSize, height: moonSize)
    let moon = NSBezierPath(ovalIn: moonRect)
    let cut = NSBezierPath(ovalIn: moonRect.offsetBy(dx: moonSize * 0.32, dy: moonSize * 0.18))
    NSGraphicsContext.current?.saveGraphicsState()
    moon.addClip()
    color(0xF5D06F).setFill()
    moon.fill()
    NSGraphicsContext.current?.compositingOperation = .clear
    cut.fill()
    NSGraphicsContext.current?.restoreGraphicsState()

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

let sizes: [(String, Int)] = [
    ("icon_16x16", 16), ("icon_16x16@2x", 32), ("icon_32x32", 32), ("icon_32x32@2x", 64),
    ("icon_128x128", 128), ("icon_128x128@2x", 256), ("icon_256x256", 256), ("icon_256x256@2x", 512),
    ("icon_512x512", 512), ("icon_512x512@2x", 1024),
]
for (name, size) in sizes {
    try render(size: size).write(to: output.appendingPathComponent("\(name).png"))
}
print("Symbol geschrieben nach \(output.path)")
