import AppKit
import SuzuCore

protocol BallViewDelegate: AnyObject {
    /// つかんだ
    func ballGrabbed()
    /// つかんで動かしている（ボールの中心・グローバル座標）
    func ballMoved(to center: CGPoint)
    /// 離した（速度・pt/秒）
    func ballThrown(velocity: CGVector)
    /// クリックした（動かさずに離した）
    func ballTapped()
}

/// 鈴と遊ぶボールのパネル。転がりに合わせて模様が回る。
/// つかんで投げる・クリックで軽く弾ませることができる。Windows 版の `BallWindow.cs` に相当。
final class BallPanel {
    let panel: MascotPanel
    let view: BallView

    init() {
        panel = MascotPanel(size: NSSize(width: 40, height: 40))
        view = BallView(frame: NSRect(x: 0, y: 0, width: 40, height: 40))
        panel.contentView = view
    }

    var isVisible: Bool { panel.isVisible }

    /// ボールの状態に合わせて描き、中心が ball.position に来るよう置く
    func render(_ ball: Ball) {
        let size = max(8, ceil(ball.radius * 2)) + 4
        if panel.frame.width != size {
            panel.setContentSize(NSSize(width: size, height: size))
            view.frame = NSRect(x: 0, y: 0, width: size, height: size)
            view.needsDisplay = true
        }
        view.setAngle(ball.angle)
        let scale = panel.backingScaleFactor
        var origin = CGPoint(x: ball.position.x - size / 2, y: ball.position.y - size / 2)
        origin.x = (origin.x * scale).rounded() / scale
        origin.y = (origin.y * scale).rounded() / scale
        if panel.frame.origin != origin { panel.setFrameOrigin(origin) }
    }

    func show() { panel.orderFrontRegardless() }
    func hide() { panel.orderOut(nil) }
}

final class BallView: NSView {
    weak var delegate: BallViewDelegate?
    /// 描いた回転（5° 刻み。毎フレーム描き直さなくてよいように）
    private var angleStep = Int.min

    private var downAt: NSPoint?
    private var grabOffset = CGVector.zero
    private var dragging = false
    private var samples: [(t: TimeInterval, p: NSPoint)] = []

    private static let accent = NSColor(srgbRed: 124 / 255, green: 148 / 255, blue: 217 / 255, alpha: 1)
    private static let navy = NSColor(srgbRed: 61 / 255, green: 74 / 255, blue: 143 / 255, alpha: 1)
    private static let bubble = NSColor(srgbRed: 219 / 255, green: 230 / 255, blue: 255 / 255, alpha: 1)

    override var isFlipped: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    func setAngle(_ angle: CGFloat) {
        let step = Int((angle * 180 / .pi / 5).rounded())
        if step != angleStep { angleStep = step; needsDisplay = true }
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let g = NSGraphicsContext.current?.cgContext else { return }
        let size = bounds.width - 4
        let rect = CGRect(x: 2, y: 2, width: size, height: size)
        let cx = rect.midX, cy = rect.midY

        // 白地に鈴の色（水色）の模様。模様だけ回して転がって見せる
        g.saveGState()
        g.addEllipse(in: rect)
        g.clip()
        g.setFillColor(NSColor.white.cgColor)
        g.fill(rect)

        g.saveGState()
        g.translateBy(x: cx, y: cy)
        // ball.angle は時計回りが正。Cocoa は反時計回りが正なので符号を反転する
        g.rotate(by: -CGFloat(angleStep == Int.min ? 0 : angleStep) * 5 * .pi / 180)
        g.setFillColor(Self.accent.cgColor)
        let w = size * 0.22
        g.fill(CGRect(x: -size, y: -w / 2, width: size * 2, height: w))   // 横の帯
        g.fill(CGRect(x: -w / 2, y: -size, width: w, height: size * 2))   // 縦の帯
        // 1 つだけ濃い色の点にして、回る向きがはっきりわかるようにする（全部同じだと速いときに逆回りに見える）
        let d = size * 0.18, o = size * 0.28
        g.setFillColor(Self.bubble.cgColor)
        for (x, y) in [(o, -o), (-o, -o), (-o, o)] {
            g.fillEllipse(in: CGRect(x: x - d / 2, y: y - d / 2, width: d, height: d))
        }
        g.setFillColor(Self.navy.cgColor)
        g.fillEllipse(in: CGRect(x: o - d * 0.6, y: o - d * 0.6, width: d * 1.2, height: d * 1.2))
        g.restoreGState()

        // 立体感：右下に影、左上につや
        let colors = [NSColor(srgbRed: 40 / 255, green: 50 / 255, blue: 110 / 255, alpha: 0).cgColor,
                      NSColor(srgbRed: 40 / 255, green: 50 / 255, blue: 110 / 255, alpha: 70 / 255).cgColor] as CFArray
        if let grad = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: colors, locations: [0, 1]) {
            let c = CGPoint(x: cx - size * 0.2, y: cy + size * 0.2)
            g.drawRadialGradient(grad, startCenter: c, startRadius: 0, endCenter: c, endRadius: size * 0.75, options: [.drawsAfterEndLocation])
        }
        g.setFillColor(NSColor.white.withAlphaComponent(170 / 255).cgColor)
        g.fillEllipse(in: CGRect(x: cx - size * 0.3, y: cy + size * 0.33 - size * 0.14, width: size * 0.22, height: size * 0.14))
        g.restoreGState()

        g.setStrokeColor(Self.navy.cgColor)
        let lw = max(1, size / 28)
        g.setLineWidth(lw)
        g.strokeEllipse(in: rect.insetBy(dx: lw / 2, dy: lw / 2))
    }

    /// 円の外側はクリックを受けない
    override func hitTest(_ point: NSPoint) -> NSView? {
        let p = convert(point, from: superview)
        let r = bounds.width / 2
        return hypot(p.x - bounds.midX, p.y - bounds.midY) <= r ? self : nil
    }

    // MARK: - マウス

    override func mouseDown(with event: NSEvent) {
        let p = NSEvent.mouseLocation
        downAt = p
        dragging = false
        if let w = window { grabOffset = CGVector(dx: w.frame.midX - p.x, dy: w.frame.midY - p.y) }
        samples = []
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = downAt else { return }
        let p = NSEvent.mouseLocation
        if !dragging && hypot(p.x - start.x, p.y - start.y) > 3 {
            dragging = true
            delegate?.ballGrabbed()
        }
        guard dragging else { return }
        samples.append((event.timestamp, p))
        if samples.count > 6 { samples.removeFirst() }
        delegate?.ballMoved(to: CGPoint(x: p.x + grabOffset.dx, y: p.y + grabOffset.dy))
    }

    override func mouseUp(with event: NSEvent) {
        defer { downAt = nil; dragging = false }
        guard downAt != nil else { return }
        guard dragging else { delegate?.ballTapped(); return }
        var v = CGVector.zero
        if let a = samples.first, let b = samples.last, b.t - a.t > 0.005 {
            v = CGVector(dx: (b.p.x - a.p.x) / (b.t - a.t), dy: (b.p.y - a.p.y) / (b.t - a.t))
        }
        delegate?.ballThrown(velocity: v)
    }
}
