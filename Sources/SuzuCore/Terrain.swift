import CoreGraphics

/// 座標系はすべて Cocoa のグローバル座標（左下原点・y 上向き・ポイント単位）。
/// CGWindowList の左上原点座標からの変換は App 層で行う。

public struct ScreenInfo: Equatable, Sendable {
    public var id: UInt32
    /// Dock・メニューバーを除いた可視領域
    public var visibleFrame: CGRect
    public init(id: UInt32, visibleFrame: CGRect) { self.id = id; self.visibleFrame = visibleFrame }
}

public struct WindowInfo: Equatable, Sendable {
    public var id: UInt32
    public var frame: CGRect
    public init(id: UInt32, frame: CGRect) { self.id = id; self.frame = frame }
}

public enum SurfaceOwner: Hashable, Sendable {
    case screen(UInt32)
    case window(UInt32)
}

/// 立つことのできる水平な面（スクリーン下端・ウィンドウ上端）
public struct Floor: Equatable, Sendable {
    public var owner: SurfaceOwner
    public var y: CGFloat
    public var minX: CGFloat
    public var maxX: CGFloat
    public func contains(x: CGFloat) -> Bool { x >= minX && x <= maxX }
    public var width: CGFloat { maxX - minX }
}

/// 登ることのできる垂直な面（スクリーン左右端）
public struct Wall: Equatable, Sendable {
    public enum Side: Sendable { case left, right }
    public var owner: SurfaceOwner
    public var side: Side
    public var x: CGFloat
    public var minY: CGFloat
    public var maxY: CGFloat
}

public struct Terrain: Equatable, Sendable {
    public var screens: [ScreenInfo]
    public var floors: [Floor]
    public var walls: [Wall]
    /// 窓に乗っている鈴を窓の移動に追従させるための位置記録
    public var windowFrames: [UInt32: CGRect] = [:]

    public static let empty = Terrain(screens: [], floors: [], walls: [])

    /// - Parameters:
    ///   - windows: 前面から背面の順（CGWindowList の順序）
    ///   - headroom: ウィンドウ上に立ったときに頭が画面外に出ないための高さ
    ///   - minFloorWidth: これより狭い足場は無視する
    public static func build(screens: [ScreenInfo], windows: [WindowInfo],
                             headroom: CGFloat, minFloorWidth: CGFloat = 60) -> Terrain {
        var floors: [Floor] = []
        var walls: [Wall] = []
        for s in screens {
            let f = s.visibleFrame
            floors.append(Floor(owner: .screen(s.id), y: f.minY, minX: f.minX, maxX: f.maxX))
            walls.append(Wall(owner: .screen(s.id), side: .left, x: f.minX, minY: f.minY, maxY: f.maxY))
            walls.append(Wall(owner: .screen(s.id), side: .right, x: f.maxX, minY: f.minY, maxY: f.maxY))
        }
        for (i, w) in windows.enumerated() {
            let top = w.frame.maxY
            // 上端がどの画面の可視領域内にあり、頭上に余裕があるか
            guard let screen = screens.first(where: {
                $0.visibleFrame.minY < top && top + headroom <= $0.visibleFrame.maxY
                    && w.frame.maxX > $0.visibleFrame.minX && w.frame.minX < $0.visibleFrame.maxX
            }) else { continue }
            var segments = [(max(w.frame.minX, screen.visibleFrame.minX), min(w.frame.maxX, screen.visibleFrame.maxX))]
            // 手前のウィンドウに覆われている部分を取り除く
            for front in windows[..<i] where front.frame.minY <= top && front.frame.maxY >= top {
                segments = subtract(segments, front.frame.minX, front.frame.maxX)
            }
            for (a, b) in segments where b - a >= minFloorWidth {
                floors.append(Floor(owner: .window(w.id), y: top, minX: a, maxX: b))
            }
        }
        var t = Terrain(screens: screens, floors: floors, walls: walls)
        t.windowFrames = Dictionary(windows.map { ($0.id, $0.frame) }, uniquingKeysWith: { a, _ in a })
        return t
    }

    static func subtract(_ segs: [(CGFloat, CGFloat)], _ a: CGFloat, _ b: CGFloat) -> [(CGFloat, CGFloat)] {
        segs.flatMap { s -> [(CGFloat, CGFloat)] in
            if b <= s.0 || a >= s.1 { return [s] }
            var out: [(CGFloat, CGFloat)] = []
            if a > s.0 { out.append((s.0, a)) }
            if b < s.1 { out.append((b, s.1)) }
            return out
        }
    }

    /// 点 p の真下（同じ高さを含む）で最も高い床
    public func floor(below p: CGPoint, tolerance: CGFloat = 1) -> Floor? {
        floors.filter { $0.contains(x: p.x) && $0.y <= p.y + tolerance }.max { $0.y < $1.y }
    }

    /// 同じ高さで x を含む床（隣接ディスプレイや分断された足場の乗り継ぎ用）
    public func floor(at x: CGFloat, y: CGFloat, tolerance: CGFloat = 1) -> Floor? {
        floors.first { $0.contains(x: x) && abs($0.y - y) <= tolerance }
    }

    public func screen(containing p: CGPoint) -> ScreenInfo? {
        screens.first { $0.visibleFrame.insetBy(dx: -1, dy: -1).contains(p) }
    }

    /// p に最も近いスクリーン（画面外に出てしまった場合の救済用）
    public func nearestScreen(to p: CGPoint) -> ScreenInfo? {
        screens.min { dist($0.visibleFrame, p) < dist($1.visibleFrame, p) }
    }

    private func dist(_ r: CGRect, _ p: CGPoint) -> CGFloat {
        let dx = max(r.minX - p.x, 0, p.x - r.maxX), dy = max(r.minY - p.y, 0, p.y - r.maxY)
        return dx * dx + dy * dy
    }
}
