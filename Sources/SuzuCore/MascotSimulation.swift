import CoreGraphics
import Foundation

/// 鈴の状態（行動・物理・表情）を進める中核。AppKit に依存せず、テストから直接動かせる。
/// position は「両足の中心」をグローバル Cocoa 座標で表す。
public final class MascotSimulation {
    public struct Config: Sendable {
        public var walkSpeed: CGFloat = 42
        public var climbSpeed: CGFloat = 28
        public var gravity: CGFloat = 2200
        public var maxFallSpeed: CGFloat = 1800
        public var bodyHeight: CGFloat = 180
        public var bodyWidth: CGFloat = 70
        /// 素材がない行動も立ち姿＋吹き出しで試す（開発用）
        public var allowPlaceholders = false
        /// 自律行動を止める
        public var paused = false
        public init() {}
    }

    public enum Motion: Equatable, Sendable {
        case grounded(Floor)
        case falling(CGVector)
        case dragged
        case climbing(Wall, targetY: CGFloat)
        case hanging(Floor, side: Wall.Side)
    }

    public struct Snapshot: Equatable, Sendable {
        public var position: CGPoint
        public var pose: Pose
        public var facing: Facing
        public var emote: Emote
        public var activity: Activity
        /// 歩行の上下揺れ（pt）
        public var bobOffset: CGFloat
    }

    public var config: Config
    public var catalog: SpriteCatalog
    public private(set) var position: CGPoint = .zero
    public private(set) var motion: Motion = .falling(.zero)
    public private(set) var activity: Activity = .idle
    public private(set) var facing: Facing = .front
    public private(set) var energy: Double = 0.8
    public private(set) var terrain: Terrain = .empty
    public var hourProvider: () -> Int = { Calendar.current.component(.hour, from: Date()) }

    private var planner = BehaviorPlanner()
    private var rng: SplitMix64
    private var clock: Double = 0
    private var timeLeft: Double = 2
    private var lastActivity: Activity?
    private var targetX: CGFloat?
    private var reactionPose: Pose = .surprise
    private var reactionEmote: Emote = .surprise
    private var lookTimer: Double = 0
    private var walkDistance: CGFloat = 0
    private var mouse: CGPoint?
    private var hoverTime: Double = 0
    private var transientEmote: (Emote, Double)?

    public init(config: Config = Config(), catalog: SpriteCatalog, seed: UInt64 = UInt64(Date().timeIntervalSince1970)) {
        self.config = config
        self.catalog = catalog
        self.rng = SplitMix64(seed: seed)
    }

    // MARK: - 外部入力

    public func place(at p: CGPoint) {
        position = p
        if let f = terrain.floor(below: p), abs(f.y - p.y) < 2 {
            position.y = f.y
            motion = .grounded(f)
        } else {
            motion = .falling(.zero)
            activity = .falling
        }
    }

    public func updateTerrain(_ new: Terrain) {
        let old = terrain
        terrain = new
        switch motion {
        case .grounded(let f), .hanging(let f, _):
            if let nf = follow(f, old: old) {
                switch motion {
                case .hanging(_, let side):
                    position.x = side == .left ? nf.minX : nf.maxX
                    position.y = nf.y - config.bodyHeight * 0.85
                    motion = .hanging(nf, side: side)
                default:
                    motion = .grounded(nf)
                }
            } else {
                startFalling(.zero)
            }
        case .climbing(let w, let ty):
            if let nw = new.walls.first(where: { $0.owner == w.owner && $0.side == w.side }) {
                position.x = nw.x + (w.side == .left ? config.bodyWidth / 2 : -config.bodyWidth / 2)
                motion = .climbing(nw, targetY: min(ty, nw.maxY - config.bodyHeight))
            } else { startFalling(.zero) }
        case .falling, .dragged:
            break
        }
        if terrain.screens.isEmpty == false, terrain.screen(containing: position) == nil, motion != .dragged {
            rescue()
        }
    }

    /// 足場が動いた・消えたときの追従。見つからなければ nil（＝落下）。
    private func follow(_ f: Floor, old: Terrain) -> Floor? {
        switch f.owner {
        case .screen:
            guard let nf = terrain.floors.first(where: { $0.owner == f.owner }) else { return nil }
            position.y = nf.y
            position.x = min(max(position.x, nf.minX), nf.maxX)
            return nf
        case .window(let id):
            guard let oldFrame = old.windowFrames[id], let newFrame = terrain.windowFrames[id] else {
                // 初回など旧情報がない場合は同じ高さで探す
                return terrain.floor(at: position.x, y: position.y, tolerance: 2)
            }
            let nx = position.x + (newFrame.minX - oldFrame.minX)
            guard let nf = terrain.floors.first(where: { $0.owner == f.owner && $0.contains(x: nx) }) else { return nil }
            position.x = nx
            if case .hanging = motion {} else { position.y = nf.y }
            targetX = targetX.map { $0 + (newFrame.minX - oldFrame.minX) }
            return nf
        }
    }

    public func observeMouse(_ p: CGPoint?) { mouse = p }

    /// クリック
    public func poke() {
        if activity == .dragged || activity == .falling { return }
        if activity == .sleep || activity == .doze {
            react(pose: .surprise, emote: .surprise, duration: 1.6)
            energy = min(1, energy + 0.15)
            return
        }
        let options: [(Pose, Emote)] = [(.joy, .joy), (.surprise, .surprise), (.joy, .heart), (.troubled, .question)]
        let o = options[Int.random(in: 0..<options.count, using: &rng)]
        react(pose: o.0, emote: o.1, duration: 1.8)
    }

    public func react(pose: Pose, emote: Emote, duration: Double) {
        guard case .grounded = motion else { showEmote(emote, for: duration); return }
        reactionPose = pose; reactionEmote = emote
        setActivity(.react, duration: duration)
        facing = .front
    }

    public func showEmote(_ e: Emote, for seconds: Double) { transientEmote = (e, seconds) }

    public func beginDrag() {
        motion = .dragged
        activity = .dragged
        facing = .front
    }

    public func drag(to p: CGPoint) {
        guard motion == .dragged else { return }
        position = p
    }

    public func endDrag(velocity: CGVector) {
        guard motion == .dragged else { return }
        let v = CGVector(dx: max(-900, min(900, velocity.dx)), dy: max(-900, min(900, velocity.dy)))
        startFalling(v)
    }

    // MARK: - 時間発展

    public func step(_ dt: Double) {
        let dt = min(dt, 0.1)
        clock += dt
        if let (e, t) = transientEmote { transientEmote = t - dt > 0 ? (e, t - dt) : nil }
        updateEnergy(dt)

        switch motion {
        case .dragged:
            return
        case .falling(var v):
            v.dy = max(v.dy - config.gravity * dt, -config.maxFallSpeed)
            v.dx *= pow(0.6, dt)
            let prev = position
            position.x += v.dx * dt
            position.y += v.dy * dt
            clampToScreensHorizontally()
            if v.dy <= 0, let f = landing(from: prev, to: position) {
                position.y = f.y
                motion = .grounded(f)
                let hard = v.dy < -1200
                if hard { react(pose: .surprise, emote: .surprise, duration: 1.2) } else { setActivity(.idle, duration: 1.5) }
            } else {
                motion = .falling(v)
                if let lowest = terrain.floors.map(\.y).min(), position.y < lowest - 400 { rescue() }
            }
            return
        case .climbing(let w, let ty):
            if position.y < ty {
                position.y = min(ty, position.y + config.climbSpeed * dt)
                walkDistance += config.climbSpeed * CGFloat(dt)
            }
            timeLeft -= dt
            if timeLeft <= 0 {
                startFalling(CGVector(dx: w.side == .left ? 120 : -120, dy: 0))
            }
            return
        case .hanging(let f, let side):
            timeLeft -= dt
            if timeLeft <= 0 {
                if Bool.random(using: &rng) {
                    // よじ登って戻る
                    position = CGPoint(x: side == .left ? f.minX + config.bodyWidth / 2 : f.maxX - config.bodyWidth / 2, y: f.y)
                    motion = .grounded(f)
                    setActivity(.idle, duration: 2)
                } else {
                    startFalling(.zero)
                }
            }
            return
        case .grounded(let f):
            stepGrounded(on: f, dt)
        }
    }

    private func stepGrounded(on floor: Floor, _ dt: Double) {
        if activity == .falling || activity == .dragged { setActivity(.idle, duration: 1) }
        timeLeft -= dt

        switch activity {
        case .walk, .goToEdge, .jumpDown, .climb, .hang:
            guard let tx = targetX else { finishActivity(); return }
            let dir: CGFloat = tx > position.x ? 1 : -1
            facing = dir > 0 ? .right : .left
            let stepLen = config.walkSpeed * CGFloat(dt)
            let nx = position.x + dir * stepLen
            walkDistance += stepLen
            // 床の乗り継ぎ（隣接ディスプレイ・同じ高さの足場）
            if !floor.contains(x: nx), activity != .jumpDown {
                if let next = terrain.floor(at: nx, y: floor.y) { motion = .grounded(next) }
                else { position.x = min(max(nx, floor.minX), floor.maxX); arrive(); return }
            }
            if activity == .jumpDown, !floor.contains(x: nx) {
                position.x = nx
                startFalling(CGVector(dx: dir * 140, dy: 260))
                return
            }
            position.x = nx
            if abs(tx - position.x) <= stepLen { position.x = tx; arrive() }
            // 散歩だけは時間切れで途中終了（目的地へ瞬間移動はしない）
            else if activity == .walk && timeLeft <= 0 { finishActivity() }
        case .lookAround:
            lookTimer -= dt
            if lookTimer <= 0 {
                let options = [Facing.front, .right, .left, .back].filter { catalog.has(.stand, facing: $0) && $0 != facing }
                if let f = options.randomElement(using: &rng) { facing = f }
                lookTimer = Double.random(in: 1.0...2.2, using: &rng)
            }
            if timeLeft <= 0 { finishActivity() }
        default:
            if activity == .idle { trackMouse(dt) }
            if timeLeft <= 0 { finishActivity() }
        }
    }

    /// 歩行の目的地に到着
    private func arrive() {
        guard case .grounded(let f) = motion else { return }
        switch activity {
        case .climb:
            guard let wall = terrain.walls.first(where: {
                abs($0.x - (position.x < (f.minX + f.maxX) / 2 ? f.minX : f.maxX)) < 1 && $0.minY <= f.y + 1
            }) else { finishActivity(); return }
            let top = wall.maxY - config.bodyHeight
            let ty = f.y + CGFloat.random(in: 0.3...0.8, using: &rng) * max(0, top - f.y)
            facing = wall.side == .left ? .left : .right
            motion = .climbing(wall, targetY: ty)
            timeLeft = Double(max(0, ty - f.y) / config.climbSpeed) + Double.random(in: 2...6, using: &rng)
        case .hang:
            guard case .window = f.owner else { finishActivity(); return }
            let side: Wall.Side = position.x - f.minX < f.maxX - position.x ? .left : .right
            position = CGPoint(x: side == .left ? f.minX : f.maxX, y: f.y - config.bodyHeight * 0.85)
            motion = .hanging(f, side: side)
            facing = .front
        default:
            finishActivity()
        }
    }

    private func trackMouse(_ dt: Double) {
        guard let m = mouse else { hoverTime = 0; return }
        let dx = m.x - position.x, dy = m.y - position.y
        let near = abs(dx) < 260 && dy > -40 && dy < config.bodyHeight + 160
        guard near else { hoverTime = 0; return }
        // 鈴の上にカーソルが留まっていたら気付く
        if abs(dx) < config.bodyWidth / 2 && dy > 0 && dy < config.bodyHeight {
            hoverTime += dt
            if hoverTime > 1.5 && transientEmote == nil { showEmote(energy > 0.4 ? .heart : .question, for: 2); hoverTime = -6 }
        }
        let want: Facing = abs(dx) < config.bodyWidth ? .front : (dx > 0 ? .right : .left)
        if catalog.has(.stand, facing: want) { facing = want }
    }

    private func finishActivity() {
        lastActivity = activity
        if config.paused { setActivity(.idle, duration: 3); return }
        guard case .grounded(let f) = motion else { return }
        let onWindow: Bool = { if case .window = f.owner { return true } else { return false } }()
        let ctx = BehaviorContext(energy: energy, hour: hourProvider(), onWindow: onWindow, last: lastActivity) { [catalog, config] s in
            config.allowPlaceholders || catalog.has(s.pose)
        }
        let (a, d) = planner.choose(in: ctx, now: clock, rng: &rng)
        setActivity(a, duration: d)
        prepare(a, on: f)
    }

    private func prepare(_ a: Activity, on f: Floor) {
        let range = reachableRange(from: f)
        let margin = config.bodyWidth / 2
        switch a {
        case .walk:
            let maxDist = CGFloat.random(in: 80...420, using: &rng)
            let lo = max(range.lowerBound + margin, position.x - maxDist)
            let hi = min(range.upperBound - margin, position.x + maxDist)
            targetX = lo < hi ? CGFloat.random(in: lo...hi, using: &rng) : nil
        case .goToEdge:
            let toLeft = Bool.random(using: &rng)
            targetX = toLeft ? range.lowerBound + margin : range.upperBound - margin
        case .climb:
            // 近い方の壁へ
            let toLeft = position.x - range.lowerBound < range.upperBound - position.x
            targetX = toLeft ? range.lowerBound + margin : range.upperBound - margin
        case .jumpDown:
            let toLeft = position.x - f.minX < f.maxX - position.x
            targetX = toLeft ? f.minX - config.bodyWidth : f.maxX + config.bodyWidth
        case .hang:
            let toLeft = position.x - f.minX < f.maxX - position.x
            targetX = toLeft ? f.minX + margin : f.maxX - margin
        case .turnAway:
            if catalog.has(.stand, facing: .back) { facing = .back }
        case .idle, .sit, .sleep, .doze, .stretch, .joy:
            facing = .front
        default:
            targetX = nil
        }
        if a == .lookAround { lookTimer = 0 }
    }

    /// 同じ高さで連続している床をまとめた歩行可能範囲
    private func reachableRange(from f: Floor) -> ClosedRange<CGFloat> {
        var lo = f.minX, hi = f.maxX
        var changed = true
        while changed {
            changed = false
            for o in terrain.floors where abs(o.y - f.y) < 1 {
                if o.maxX >= lo - 1 && o.minX < lo { lo = o.minX; changed = true }
                if o.minX <= hi + 1 && o.maxX > hi { hi = o.maxX; changed = true }
            }
        }
        return lo...hi
    }

    private func setActivity(_ a: Activity, duration: Double) {
        activity = a
        timeLeft = duration
        if a != .walk && a != .goToEdge && a != .jumpDown && a != .climb && a != .hang { targetX = nil }
    }

    private func startFalling(_ v: CGVector) {
        motion = .falling(v)
        activity = .falling
        targetX = nil
    }

    private func landing(from a: CGPoint, to b: CGPoint) -> Floor? {
        terrain.floors
            .filter { $0.contains(x: b.x) && $0.y <= a.y + 0.5 && $0.y >= b.y }
            .max { $0.y < $1.y }
    }

    private func clampToScreensHorizontally() {
        guard let s = terrain.screen(containing: position) ?? terrain.nearestScreen(to: position) else { return }
        let half = config.bodyWidth / 2
        position.x = min(max(position.x, s.visibleFrame.minX + half), s.visibleFrame.maxX - half)
        if position.y > s.visibleFrame.maxY - config.bodyHeight { position.y = s.visibleFrame.maxY - config.bodyHeight }
    }

    /// 画面外に出てしまったときに最寄りの画面の床に戻す
    private func rescue() {
        guard let s = terrain.nearestScreen(to: position),
              let f = terrain.floors.first(where: { $0.owner == .screen(s.id) }) else { return }
        position = CGPoint(x: min(max(position.x, f.minX + config.bodyWidth), f.maxX - config.bodyWidth), y: f.y)
        motion = .grounded(f)
        setActivity(.idle, duration: 2)
    }

    private func updateEnergy(_ dt: Double) {
        switch activity {
        case .sleep:
            energy = min(1, energy + dt / 120)
            if energy >= 1 && timeLeft > 3 { timeLeft = 3 }
        case .doze, .sit: energy = min(1, energy + dt / 900)
        default: energy = max(0, energy - dt / 2700)
        }
    }

    // MARK: - 表示用

    public var snapshot: Snapshot {
        let pose: Pose
        var emote: Emote = .none
        switch motion {
        case .falling: pose = .fall
        case .dragged: pose = .dragged; emote = .surprise
        case .climbing: pose = .climb
        case .hanging: pose = .hang
        case .grounded:
            switch activity {
            case .walk, .goToEdge, .jumpDown, .climb, .hang: pose = targetX == nil ? .stand : .walk
            case .sit: pose = .sit
            case .sleep: pose = .sleep; emote = .sleepy
            case .doze: pose = .stand; emote = .sleepy
            case .stretch: pose = .stretch; emote = .stretch
            case .joy: pose = .joy; emote = .joy
            case .react: pose = reactionPose; emote = reactionEmote
            default: pose = .stand
            }
        }
        if let (e, _) = transientEmote { emote = e }
        let resolved = catalog.resolve(pose, facing: facing)
        let bob: CGFloat = resolved.bob && pose == .walk ? abs(sin(walkDistance / 14 * .pi)) * 2.5 : 0
        return Snapshot(position: position, pose: pose, facing: facing, emote: emote, activity: activity, bobOffset: bob)
    }

    /// テスト用：現在の活動を強制する
    public func force(_ a: Activity, duration: Double = 10) {
        guard case .grounded(let f) = motion else { return }
        setActivity(a, duration: duration)
        prepare(a, on: f)
    }
}
