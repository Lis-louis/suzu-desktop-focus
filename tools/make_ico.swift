// macOS 用アイコン(.icns)から Windows 用アイコン(.ico)を作る。画像は縮小のみ。
// 使い方: swift tools/make_ico.swift Resources/AppIcon/Suzu.icns Resources/AppIcon/Suzu.ico
import AppKit

let args = CommandLine.arguments
guard args.count >= 3, let src = NSImage(contentsOfFile: args[1]) else {
    print("usage: swift tools/make_ico.swift <in.icns|in.png> <out.ico>"); exit(1)
}
let sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
var images: [Data] = []
for px in sizes {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    src.draw(in: NSRect(x: 0, y: 0, width: px, height: px), from: .zero, operation: .copy, fraction: 1)
    NSGraphicsContext.restoreGraphicsState()
    images.append(rep.representation(using: .png, properties: [:])!)
}
// ICO: ヘッダ(6) + エントリ(16×n) + PNG データ（Windows Vista 以降は PNG をそのまま格納できる）
var out = Data()
func u16(_ v: Int) { var x = UInt16(v).littleEndian; out.append(Data(bytes: &x, count: 2)) }
func u32(_ v: Int) { var x = UInt32(v).littleEndian; out.append(Data(bytes: &x, count: 4)) }
u16(0); u16(1); u16(sizes.count)
var offset = 6 + 16 * sizes.count
for (px, data) in zip(sizes, images) {
    out.append(UInt8(px >= 256 ? 0 : px)); out.append(UInt8(px >= 256 ? 0 : px)); out.append(0); out.append(0)
    u16(1); u16(32); u32(data.count); u32(offset)
    offset += data.count
}
for d in images { out.append(d) }
try! out.write(to: URL(fileURLWithPath: args[2]))
print("icon:", args[2])
