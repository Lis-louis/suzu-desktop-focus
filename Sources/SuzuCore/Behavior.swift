import Foundation

/// 鈴の「今やっていること」。自律行動として選ばれるものと、外部要因によるものがある。
public enum Activity: String, CaseIterable, Sendable {
    // 自律行動
    case idle, walk, lookAround, turnAway, doze, sit, sleep, stretch, joy, goToEdge, jumpDown, climb, hang
    // 外部要因（自律選択されない）
    case react, dragged, falling

    public static let autonomous: [Activity] = [.idle, .walk, .lookAround, .turnAway, .doze, .sit, .sleep,
                                                .stretch, .joy, .goToEdge, .jumpDown, .climb, .hang]
}

/// 行動の発生条件・確率・継続時間の定義。数値調整はここに集約する。
public struct BehaviorSpec: Sendable {
    public enum Placement: Sendable { case anywhere, screenFloor, windowTop }
    public var activity: Activity
    /// この行動に必要な画像素材のポーズ
    public var pose: Pose
    public var weight: Double
    public var duration: ClosedRange<Double>
    public var cooldown: Double
    public var placement: Placement

    public static let defaults: [BehaviorSpec] = [
        .init(activity: .idle,       pose: .stand,   weight: 30, duration: 3...8,    cooldown: 0,   placement: .anywhere),
        .init(activity: .walk,       pose: .walk,    weight: 26, duration: 4...20,   cooldown: 0,   placement: .anywhere),
        .init(activity: .lookAround, pose: .stand,   weight: 10, duration: 4...7,    cooldown: 10,  placement: .anywhere),
        .init(activity: .turnAway,   pose: .stand,   weight: 3,  duration: 3...6,    cooldown: 40,  placement: .anywhere),
        .init(activity: .doze,       pose: .stand,   weight: 0,  duration: 6...15,   cooldown: 30,  placement: .anywhere),
        .init(activity: .sit,        pose: .sit,     weight: 12, duration: 15...40,  cooldown: 20,  placement: .anywhere),
        .init(activity: .sleep,      pose: .sleep,   weight: 0,  duration: 40...150, cooldown: 300, placement: .anywhere),
        .init(activity: .stretch,    pose: .stretch, weight: 4,  duration: 2.5...3.5, cooldown: 60, placement: .anywhere),
        .init(activity: .joy,        pose: .joy,     weight: 3,  duration: 2...3,    cooldown: 45,  placement: .anywhere),
        .init(activity: .goToEdge,   pose: .walk,    weight: 4,  duration: 30...30,  cooldown: 60,  placement: .screenFloor),
        .init(activity: .jumpDown,   pose: .walk,    weight: 5,  duration: 30...30,  cooldown: 30,  placement: .windowTop),
        .init(activity: .climb,      pose: .climb,   weight: 3,  duration: 8...20,   cooldown: 120, placement: .screenFloor),
        .init(activity: .hang,       pose: .hang,    weight: 4,  duration: 6...15,   cooldown: 90,  placement: .windowTop),
    ]
}

/// 行動選択に使う状況
public struct BehaviorContext {
    public var energy: Double          // 0(眠い)...1(元気)
    public var hour: Int               // 0...23
    public var onWindow: Bool
    public var last: Activity?
    public var available: (BehaviorSpec) -> Bool

    public init(energy: Double, hour: Int, onWindow: Bool, last: Activity?, available: @escaping (BehaviorSpec) -> Bool) {
        self.energy = energy; self.hour = hour; self.onWindow = onWindow; self.last = last; self.available = available
    }
}

/// 重み付き抽選＋状況補正＋クールダウンで、完全なランダムにならない行動選択を行う。
public struct BehaviorPlanner: Sendable {
    public var specs: [BehaviorSpec]
    public var cooldownUntil: [Activity: Double] = [:]

    public init(specs: [BehaviorSpec] = BehaviorSpec.defaults) { self.specs = specs }

    public func spec(_ a: Activity) -> BehaviorSpec? { specs.first { $0.activity == a } }

    public func weight(of s: BehaviorSpec, in ctx: BehaviorContext, now: Double) -> Double {
        guard ctx.available(s) else { return 0 }
        if let until = cooldownUntil[s.activity], until > now { return 0 }
        switch s.placement {
        case .screenFloor where ctx.onWindow: return 0
        case .windowTop where !ctx.onWindow: return 0
        default: break
        }
        let tired = 1 - ctx.energy
        let night = ctx.hour < 6 || ctx.hour >= 23
        let morning = (6..<10).contains(ctx.hour)
        var w = s.weight
        switch s.activity {
        case .doze:  w += 30 * tired * tired + (night ? 8 : 0)
        case .sleep: w += 50 * pow(tired, 3) + (night ? 15 : 0)
        case .walk, .goToEdge: w *= (0.4 + ctx.energy) * (night ? 0.6 : 1)
        case .sit:   w *= ctx.onWindow ? 2.5 : 1; w *= 1 + tired
        case .stretch:
            if morning { w *= 2 }
            if ctx.last == .sleep || ctx.last == .sit || ctx.last == .doze { w *= 5 }
        case .joy:   w *= 0.5 + ctx.energy
        default: break
        }
        // 同じ行動の連続や歩きっぱなしを避ける
        if ctx.last == s.activity { w *= s.activity == .idle ? 0.6 : 0.3 }
        if ctx.last == .walk && s.activity == .idle { w *= 1.5 }
        return max(0, w)
    }

    public mutating func choose<G: RandomNumberGenerator>(in ctx: BehaviorContext, now: Double, rng: inout G) -> (Activity, Double) {
        let weighted = specs.map { ($0, weight(of: $0, in: ctx, now: now)) }.filter { $0.1 > 0 }
        let total = weighted.reduce(0) { $0 + $1.1 }
        guard total > 0 else { return (.idle, 5) }
        var r = Double.random(in: 0..<total, using: &rng)
        for (s, w) in weighted {
            if r < w {
                if s.cooldown > 0 { cooldownUntil[s.activity] = now + s.cooldown }
                return (s.activity, Double.random(in: s.duration, using: &rng))
            }
            r -= w
        }
        let s = weighted.last!.0
        return (s.activity, s.duration.lowerBound)
    }
}

/// テスト可能な決定的乱数
public struct SplitMix64: RandomNumberGenerator, Sendable {
    var state: UInt64
    public init(seed: UInt64) { state = seed }
    public mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
}
