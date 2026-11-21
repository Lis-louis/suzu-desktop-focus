// インストーラー（Windows のセットアップウィザード・macOS の .pkg）に使う画像を作る。
// アプリアイコンを、鈴のパレットの淡いグラデーションの上に置くだけ（キャラクター画像は加工しない）。
// 使い方: swift tools/make_installer_art.swift
import AppKit

let root = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : FileManager.default.currentDirectoryPath)
let icon = NSImage(contentsOf: root.appendingPathComponent("Resources/AppIcon/Suzu.icns"))!
let top = NSColor(srgbRed: 0.93, green: 0.95, blue: 1.0, alpha: 1)      // 白〜淡いラベンダー
let bottom = NSColor(srgbRed: 0.80, green: 0.86, blue: 0.98, alpha: 1)  // 淡い青
let navy = NSColor(srgbRed: 0.24, green: 0.29, blue: 0.56, alpha: 1)    // #3D4A8F

enum Format { case bmp, png }

/// 24bit・下から上の順の BMP（古い読み込み処理でも確実に扱える形式）
func bottomUpBMP(_ rep: NSBitmapImageRep) -> Data {
    let w = rep.pixelsWide, h = rep.pixelsHigh
    let rowSize = (w * 3 + 3) & ~3
    var d = Data()
    func u16(_ v: Int) { var x = UInt16(v).littleEndian; d.append(Data(bytes: &x, count: 2)) }
    func u32(_ v: Int) { var x = UInt32(truncatingIfNeeded: v).littleEndian; d.append(Data(bytes: &x, count: 4)) }
    d.append(contentsOf: [0x42, 0x4D]); u32(54 + rowSize * h); u32(0); u32(54)
    u32(40); u32(w); u32(h); u16(1); u16(24); u32(0); u32(rowSize * h); u32(2835); u32(2835); u32(0); u32(0)
    for y in stride(from: h - 1, through: 0, by: -1) {
        var row = [UInt8](repeating: 0, count: rowSize)
        for x in 0..<w {
            let c = rep.colorAt(x: x, y: y)!.usingColorSpace(.sRGB)!
            row[x * 3] = UInt8(max(0, min(255, (c.blueComponent * 255).rounded())))
            row[x * 3 + 1] = UInt8(max(0, min(255, (c.greenComponent * 255).rounded())))
            row[x * 3 + 2] = UInt8(max(0, min(255, (c.redComponent * 255).rounded())))
        }
        d.append(contentsOf: row)
    }
    return d
}

/// - Parameters:
///   - iconRatio: 画像の幅に対するアイコンの大きさ
///   - iconY: 下からの位置（高さに対する割合、アイコン中心）
///   - title: アイコンの下に入れる文字（nil で無し）
func render(width: Int, height: Int, iconRatio: CGFloat, iconY: CGFloat, title: String?, alpha: Bool, to path: String, format: Format) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    let bounds = NSRect(x: 0, y: 0, width: width, height: height)
    if alpha {
        NSColor.clear.setFill(); bounds.fill()
    } else {
        NSGradient(starting: bottom, ending: top)!.draw(in: bounds, angle: 90)
    }
    let side = CGFloat(width) * iconRatio
    let rect = NSRect(x: (CGFloat(width) - side) / 2, y: CGFloat(height) * iconY - side / 2, width: side, height: side)
    icon.draw(in: rect, from: .zero, operation: .sourceOver, fraction: 1)
    if let title {
        let font = NSFont.systemFont(ofSize: CGFloat(width) * 0.13, weight: .semibold)
        let para = NSMutableParagraphStyle(); para.alignment = .center
        let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: navy, .paragraphStyle: para]
        let h = font.pointSize * 1.4
        (title as NSString).draw(in: NSRect(x: 0, y: rect.minY - h - CGFloat(height) * 0.03, width: CGFloat(width), height: h), withAttributes: attrs)
    }
    NSGraphicsContext.restoreGraphicsState()
    let data = format == .bmp ? bottomUpBMP(rep) : rep.representation(using: .png, properties: [:])!
    try! data.write(to: root.appendingPathComponent(path))
    print("art:", path)
}

// Windows（Inno Setup）：ようこそ／完了ページの左の画像と、各ページ右上の小さな画像。100%〜200% 表示用
for (scale, suffix) in [(1, "100"), (2, "200")] {
    render(width: 164 * scale, height: 314 * scale, iconRatio: 0.7, iconY: 0.62, title: "鈴 Suzu", alpha: false,
           to: "windows/installer/wizard-\(suffix).bmp", format: .bmp)
    render(width: 55 * scale, height: 58 * scale, iconRatio: 0.9, iconY: 0.5, title: nil, alpha: false,
           to: "windows/installer/wizard-small-\(suffix).bmp", format: .bmp)
}
// macOS（.pkg）：インストーラーの左下（手順一覧の下）に表示する背景
render(width: 180, height: 180, iconRatio: 0.7, iconY: 0.5, title: nil, alpha: true,
       to: "packaging/macos/resources/background.png", format: .png)
