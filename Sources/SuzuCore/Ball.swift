import CoreGraphics
import Foundation

/// 鈴と遊ぶボール。床（画面の下・ウィンドウの上）で弾み、転がり、画面の端で跳ね返る。
/// 座標は鈴と同じ（Cocoa のグローバル座標・左下原点・y 上向き）。position はボールの中心。
/// Windows 版の `Ball.cs` と同じ内容（Mac はポイント単位なので速さの倍率はない）。
public final class Ball {
    /// はね返りの強さ（1 で落とした高さまで戻る）
    public static let restitution: CGFloat = 0.62
    /// この速さより遅く床に当たったら、弾まずに転がる
    private static let restSpeed: CGFloat = 70

    public var radius: CGFloat
    public private(set) var position: CGPoint
    public private(set) var velocity: CGVector = .zero
    /// 転がった分の回転（ラジアン・時計回りが正）。描画用。
    public private(set) var angle: CGFloat = 0
    /// 床の上にいる（弾んでいない）
    public private(set) var onFloor = false
    /// マウスでつかまれている
    public private(set) var held = false

    public init(center: CGPoint, radius: CGFloat) {
        self.position = center
        self.radius = radius
    }

    /// 止まっている（鈴が「もう一回蹴ろう」と思える）
    public var isResting: Bool { onFloor && abs(velocity.dx) < 25 }

    public func kick(_ v: CGVector) {
        held = false
        onFloor = false
        velocity = v
    }

    public func hold(at center: CGPoint) {
        held = true
        onFloor = false
        position = center
        velocity = .zero
    }

    public func release(velocity v: CGVector) {
        let lim: CGFloat = 1600
        kick(CGVector(dx: min(max(v.dx, -lim), lim), dy: min(max(v.dy, -lim), lim)))
    }

    public func step(_ dt: Double, terrain: Terrain, gravity: CGFloat) {
        let dt = CGFloat(min(dt, 0.1))
        if held { return }
        let r = radius
        let prev = position

        if onFloor {
            // 転がる：少しずつ遅くなる。床の端から外れたら落ちる
            var vx = velocity.dx * pow(0.45, dt)
            if abs(vx) < 5 { vx = 0 }
            let p = CGPoint(x: position.x + vx * dt, y: position.y)
            velocity = CGVector(dx: vx, dy: 0)
            position = p
            if let f = terrain.floor(at: p.x, y: p.y - r, tolerance: 2) { position.y = f.y + r } else { onFloor = false }
        } else {
            let vy = max(velocity.dy - gravity * dt, -1800)
            let vx = velocity.dx * pow(0.85, dt)
            let p = CGPoint(x: position.x + vx * dt, y: position.y + vy * dt)
            velocity = CGVector(dx: vx, dy: vy)
            position = p
            if vy <= 0, let f = Ball.landing(terrain, from: prev.y - r, to: p.y - r, x: p.x) {
                position.y = f.y + r
                if -vy * Ball.restitution > Ball.restSpeed { velocity = CGVector(dx: vx * 0.85, dy: -vy * Ball.restitution) }
                else { velocity = CGVector(dx: vx, dy: 0); onFloor = true }
            }
        }

        bounceOffScreenEdges(terrain)
        // 右へ転がると時計回り（描画側で時計回りが正になるように回す）
        angle += (position.x - prev.x) / max(1, r)
        if let lowest = terrain.floors.map(\.y).min(), position.y < lowest - 400 { rescue(terrain) }
    }

    /// 下に動いたとき、底が通り抜けた床のうち一番高いもの
    private static func landing(_ t: Terrain, from fromBottom: CGFloat, to toBottom: CGFloat, x: CGFloat) -> Floor? {
        t.floors.filter { $0.contains(x: x) && $0.y <= fromBottom + 0.5 && $0.y >= toBottom }.max { $0.y < $1.y }
    }

    /// 画面の左右・上で跳ね返る（隣の画面とつながっている辺は素通り）
    private func bounceOffScreenEdges(_ t: Terrain) {
        guard let screen = t.screen(columnContaining: position) else { return }
        let f = screen.visibleFrame
        let r = radius
        var p = position, v = velocity
        if t.adjacentScreen(to: screen, side: .left) == nil, p.x < f.minX + r { p.x = f.minX + r; v.dx = abs(v.dx) * 0.8 }
        if t.adjacentScreen(to: screen, side: .right) == nil, p.x > f.maxX - r { p.x = f.maxX - r; v.dx = -abs(v.dx) * 0.8 }
        if p.y > f.maxY - r { p.y = f.maxY - r; v.dy = -abs(v.dy) * 0.5 }
        position = p; velocity = v
    }

    /// 画面外に落ちてしまったら、最寄りの画面の上から落とし直す
    private func rescue(_ t: Terrain) {
        guard let s = t.nearestScreen(to: position) else { return }
        let f = s.visibleFrame
        position = CGPoint(x: min(max(position.x, f.minX + radius), f.maxX - radius), y: f.maxY - radius * 2)
        velocity = .zero
        onFloor = false
    }
}
