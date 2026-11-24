import Foundation

/// 鈴の体の姿勢。画像素材はこの単位で用意する（docs/ASSETS.md）。
public enum Pose: String, Codable, CaseIterable, Sendable {
    case stand, walk, sit, sleep, stretch, joy, surprise, troubled, fall, dragged, climb, hang
}

public enum Facing: String, Codable, CaseIterable, Sendable {
    case front, left, right, back
    public var opposite: Facing {
        switch self { case .left: .right; case .right: .left; default: self }
    }
}

/// 頭上に出す吹き出し記号。キャラクター画像は変えずに感情を補足する。
public enum Emote: String, Sendable {
    case none, sleepy = "Zzz", joy = "♪", surprise = "!", question = "?", heart = "♡", dots = "…", stretch = "ん〜"
}

public struct SpriteManifest: Codable, Sendable {
    public struct Entry: Codable, Sendable {
        public var pose: Pose
        public var facing: Facing
        public var frames: [String]
        /// 0 または省略で静止画
        public var fps: Double?
        /// 歩行時の上下の揺れ（画像は変形せず、位置のみ動かす）
        public var bob: Bool?
        public init(pose: Pose, facing: Facing, frames: [String], fps: Double? = nil, bob: Bool? = nil) {
            self.pose = pose; self.facing = facing; self.frames = frames; self.fps = fps; self.bob = bob
        }
    }
    public var version: Int
    public var entries: [Entry]
    public init(version: Int = 1, entries: [Entry]) { self.version = version; self.entries = entries }

    public static func load(from url: URL) throws -> SpriteManifest {
        try JSONDecoder().decode(SpriteManifest.self, from: Data(contentsOf: url))
    }
}

public struct ResolvedSprite: Equatable, Sendable {
    public var frames: [String]
    public var fps: Double
    public var bob: Bool
    /// 左右反転して表示する（髪飾りの位置が逆になるため既定では使わない）
    public var mirrored: Bool
    /// 要求されたポーズの素材がなく、立ち姿などで代用している
    public var isFallback: Bool
}

public struct SpriteCatalog: Sendable {
    public var manifest: SpriteManifest
    public var allowMirroring: Bool

    public init(manifest: SpriteManifest, allowMirroring: Bool = false) {
        self.manifest = manifest; self.allowMirroring = allowMirroring
    }

    func entry(_ pose: Pose, _ facing: Facing) -> SpriteManifest.Entry? {
        manifest.entries.first { $0.pose == pose && $0.facing == facing && !$0.frames.isEmpty }
    }

    /// 本物の素材があるか（反転許可時は反転で作れるものも含む）
    public func has(_ pose: Pose) -> Bool { manifest.entries.contains { $0.pose == pose && !$0.frames.isEmpty } }

    public func has(_ pose: Pose, facing: Facing) -> Bool {
        entry(pose, facing) != nil || (allowMirroring && entry(pose, facing.opposite) != nil && facing != facing.opposite)
    }

    public func resolve(_ pose: Pose, facing: Facing) -> ResolvedSprite {
        func make(_ e: SpriteManifest.Entry, mirrored: Bool, fallback: Bool) -> ResolvedSprite {
            ResolvedSprite(frames: e.frames, fps: e.fps ?? 0, bob: e.bob ?? false, mirrored: mirrored, isFallback: fallback)
        }
        func lookup(_ p: Pose) -> ResolvedSprite? {
            if let e = entry(p, facing) { return make(e, mirrored: false, fallback: p != pose) }
            // 歩くときは、正面のまま横に滑るより自然なので、設定に関係なく反対向きの絵を反転して使う
            if allowMirroring || p == .walk, facing.opposite != facing, let e = entry(p, facing.opposite) {
                return make(e, mirrored: true, fallback: p != pose)
            }
            return nil
        }
        if let r = lookup(pose) { return r }
        // 同じポーズの正面
        if let e = entry(pose, .front) { return make(e, mirrored: false, fallback: facing != .front) }
        if pose != .stand, let r = lookup(.stand) { return ResolvedSprite(frames: r.frames, fps: r.fps, bob: false, mirrored: r.mirrored, isFallback: true) }
        if let e = entry(.stand, .front) { return make(e, mirrored: false, fallback: true) }
        let any = manifest.entries.first!
        return make(any, mirrored: false, fallback: true)
    }

    /// manifest が参照しているが存在しない画像名
    public func missingFiles(existing: Set<String>) -> [String] {
        Array(Set(manifest.entries.flatMap(\.frames)).subtracting(existing)).sorted()
    }

    /// 設計上必要だが未登録のポーズ×向き（ASSETS.md の自動レポート用）
    public static let required: [(Pose, [Facing])] = [
        (.stand, [.front, .left, .right, .back]), (.walk, [.left, .right]),
        (.sit, [.front]), (.sleep, [.front]), (.stretch, [.front]), (.joy, [.front]),
        (.surprise, [.front]), (.troubled, [.front]), (.fall, [.front]), (.dragged, [.front]),
        (.climb, [.left, .right]), (.hang, [.front]),
    ]

    public func missingPoses() -> [(Pose, Facing)] {
        Self.required.flatMap { p, fs in fs.filter { entry(p, $0) == nil }.map { (p, $0) } }
    }
}
