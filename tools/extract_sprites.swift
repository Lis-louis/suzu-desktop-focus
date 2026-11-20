// 設定資料シートから全身ポーズを切り出し、Vision の前景マスクで背景を透過した PNG を生成する。
// 使い方: swift tools/extract_sprites.swift <costume.jpg> <sheet.jpg> <outDir>
// 画像そのものは変形しない（切り抜き＋縮小＋背景透過のみ）。
import AppKit
import CoreImage
import Vision

struct Crop { let name: String; let source: Int; let rect: CGRect } // rect: 元画像ピクセル, 左上原点

let crops: [Crop] = [
    // 衣装デザイン図 (source 0)
    Crop(name: "front_costume", source: 0, rect: CGRect(x: 200,  y: 150, width: 1100, height: 2420)),
    Crop(name: "side_right",    source: 0, rect: CGRect(x: 1230, y: 150, width: 640,  height: 2420)),
    Crop(name: "back_costume",  source: 0, rect: CGRect(x: 1860, y: 150, width: 800,  height: 2420)),
    Crop(name: "side_left",     source: 0, rect: CGRect(x: 2640, y: 150, width: 560,  height: 2420)),
    // 設定資料 (source 1)
    Crop(name: "front",         source: 1, rect: CGRect(x: 360,  y: 120, width: 1380, height: 3180)),
    Crop(name: "back",          source: 1, rect: CGRect(x: 1800, y: 120, width: 1280, height: 3180)),
]

let args = CommandLine.arguments
guard args.count == 4 else { print("usage: extract_sprites <costume> <sheet> <outDir>"); exit(1) }
let sources = [args[1], args[2]].map { NSImage(contentsOfFile: $0)!.cgImage(forProposedRect: nil, context: nil, hints: nil)! }
let outDir = URL(fileURLWithPath: args[3])
try FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)
let ctx = CIContext()
let targetHeight: CGFloat = 720

for c in crops {
    guard let cropped = sources[c.source].cropping(to: c.rect) else { continue }
    let handler = VNImageRequestHandler(cgImage: cropped)
    let req = VNGenerateForegroundInstanceMaskRequest()
    try handler.perform([req])
    guard let obs = req.results?.first else { print("no foreground: \(c.name)"); continue }
    // 最大面積のインスタンスのみ（隣の人物の切れ端を除外）
    var best: (Int, Int) = (0, 0)
    for idx in obs.allInstances {
        let m = try obs.generateScaledMaskForImage(forInstances: [idx], from: handler)
        let w = CVPixelBufferGetWidth(m), h = CVPixelBufferGetHeight(m)
        CVPixelBufferLockBaseAddress(m, .readOnly)
        let base = CVPixelBufferGetBaseAddress(m)!.assumingMemoryBound(to: Float32.self)
        let stride = CVPixelBufferGetBytesPerRow(m) / 4
        var n = 0
        for y in Swift.stride(from: 0, to: h, by: 4) { for x in Swift.stride(from: 0, to: w, by: 4) where base[y*stride+x] > 0.5 { n += 1 } }
        CVPixelBufferUnlockBaseAddress(m, .readOnly)
        if n > best.1 { best = (idx, n) }
    }
    let masked = try obs.generateMaskedImage(ofInstances: [best.0], from: handler, croppedToInstancesExtent: true)
    var ci = CIImage(cvPixelBuffer: masked)
    let scale = targetHeight / ci.extent.height
    ci = ci.transformed(by: CGAffineTransform(scaleX: scale, y: scale))
    let cs = CGColorSpace(name: CGColorSpace.sRGB)!
    let url = outDir.appendingPathComponent("\(c.name).png")
    try ctx.writePNGRepresentation(of: ci, to: url, format: .RGBA8, colorSpace: cs)
    print("\(c.name): \(Int(ci.extent.width))x\(Int(ci.extent.height)) instances=\(obs.allInstances.count)")
}
