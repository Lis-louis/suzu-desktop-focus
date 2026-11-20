// 設定資料の正面の顔部分からアプリアイコン(.icns)を作る。画像は切り抜き・縮小のみ。
// 使い方: swift tools/make_icon.swift <sheet.jpg> <out.icns>
import AppKit
let args = CommandLine.arguments
let src = NSImage(contentsOfFile: args[1])!.cgImage(forProposedRect: nil, context: nil, hints: nil)!
// 設定資料 右上の笑顔の表情（元画像ピクセル, 左上原点）
let face = src.cropping(to: CGRect(x: 3060, y: 150, width: 900, height: 900))!
let tmp = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("Suzu.iconset")
try? FileManager.default.removeItem(at: tmp)
try! FileManager.default.createDirectory(at: tmp, withIntermediateDirectories: true)
for (size, scale) in [(16,1),(16,2),(32,1),(32,2),(128,1),(128,2),(256,1),(256,2),(512,1),(512,2)] {
    let px = size * scale
    let ctx = CGContext(data: nil, width: px, height: px, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.interpolationQuality = .high
    let inset = CGFloat(px) * 0.09, r = CGRect(x: inset, y: inset, width: CGFloat(px) - inset*2, height: CGFloat(px) - inset*2)
    let path = CGPath(roundedRect: r, cornerWidth: r.width * 0.225, cornerHeight: r.width * 0.225, transform: nil)
    ctx.addPath(path); ctx.clip()
    ctx.draw(face, in: r)
    ctx.resetClip()
    ctx.addPath(path); ctx.setStrokeColor(CGColor(srgbRed: 0.49, green: 0.58, blue: 0.85, alpha: 1)); ctx.setLineWidth(max(1, CGFloat(px) * 0.012)); ctx.strokePath()
    let name = scale == 1 ? "icon_\(size)x\(size).png" : "icon_\(size)x\(size)@2x.png"
    let rep = NSBitmapImageRep(cgImage: ctx.makeImage()!)
    try! rep.representation(using: .png, properties: [:])!.write(to: tmp.appendingPathComponent(name))
}
let p = Process(); p.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
p.arguments = ["-c", "icns", tmp.path, "-o", args[2]]; try! p.run(); p.waitUntilExit()
print("icon:", args[2])
