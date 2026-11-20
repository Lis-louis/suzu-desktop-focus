import AppKit
import SuzuCore

/// 画像素材の読み込み。.app 内の Resources/Sprites、環境変数 SUZU_ASSETS、開発時のリポジトリ内の順に探す。
final class SpriteStore {
    let directory: URL
    let manifest: SpriteManifest
    private(set) var images: [String: CGImage] = [:]

    init() throws {
        directory = try Self.locate()
        manifest = try SpriteManifest.load(from: directory.appendingPathComponent("manifest.json"))
        for name in Set(manifest.entries.flatMap(\.frames)) {
            let url = directory.appendingPathComponent("\(name).png")
            guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
                  let img = CGImageSourceCreateImageAtIndex(src, 0, nil) else {
                NSLog("[Suzu] 画像が見つかりません: \(url.path)")
                continue
            }
            images[name] = img
        }
    }

    func image(_ name: String) -> CGImage? { images[name] }

    /// 全スプライト中の最大の幅/高さ比（ウィンドウ幅の決定用）
    var maxAspect: CGFloat {
        images.values.map { CGFloat($0.width) / CGFloat($0.height) }.max() ?? 0.5
    }

    private static func locate() throws -> URL {
        var candidates: [URL] = []
        if let r = Bundle.main.resourceURL { candidates.append(r.appendingPathComponent("Sprites")) }
        if let env = ProcessInfo.processInfo.environment["SUZU_ASSETS"] { candidates.append(URL(fileURLWithPath: env)) }
        // swift run 時: Sources/SuzuApp/SpriteStore.swift → リポジトリ直下
        candidates.append(URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("Resources/Sprites"))
        for c in candidates where FileManager.default.fileExists(atPath: c.appendingPathComponent("manifest.json").path) {
            return c
        }
        throw NSError(domain: "Suzu", code: 1, userInfo: [NSLocalizedDescriptionKey: "Sprites/manifest.json が見つかりません"])
    }
}
