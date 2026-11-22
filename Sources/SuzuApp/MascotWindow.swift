import AppKit
import QuartzCore
import SuzuCore

/// 枠なし・背景透過・非アクティブ化の常駐パネル。透明な部分のクリックは背面へ通る。
final class MascotPanel: NSPanel {
    init(size: NSSize) {
        super.init(contentRect: NSRect(origin: .zero, size: size),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        level = .floating
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        isMovable = false // 移動はシミュレーション側で管理
        collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        animationBehavior = .none
        canHide = false // ⌘H（アプリを隠す）でも鈴は残る
    }
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

protocol MascotViewDelegate: AnyObject {
    func mascotClicked()
    func mascotDoubleClicked()
    func mascotDragBegan()
    func mascotDragged(feetAt p: CGPoint)
    func mascotDragEnded(velocity: CGVector)
    func mascotMenu() -> NSMenu
    /// ⌘ を押しながらのスクロール（steps が正なら大きく）
    func mascotResize(steps: Int)
}

/// スプライトと吹き出し記号を描く。画像は変形せず、位置・左右反転（許可時のみ）だけを扱う。
final class MascotView: NSView {
    weak var delegate: MascotViewDelegate?
    let spriteLayer = CALayer()
    let emoteLayer = CATextLayer()
    let emoteBubble = CALayer()
    var bodyHeight: CGFloat = 180
    private var currentImage: CGImage?
    private var dragStart: NSPoint?
    private var grabOffset = CGVector.zero
    private var isDragging = false
    private var samples: [(t: TimeInterval, p: NSPoint)] = []

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
        spriteLayer.contentsGravity = .resizeAspect
        spriteLayer.minificationFilter = .trilinear
        spriteLayer.anchorPoint = CGPoint(x: 0.5, y: 0)
        layer?.addSublayer(spriteLayer)

        emoteBubble.backgroundColor = NSColor.white.withAlphaComponent(0.95).cgColor
        emoteBubble.borderColor = NSColor(srgbRed: 0.49, green: 0.58, blue: 0.85, alpha: 1).cgColor
        emoteBubble.borderWidth = 1.5
        emoteBubble.cornerRadius = 12
        emoteBubble.isHidden = true
        emoteLayer.alignmentMode = .center
        emoteLayer.foregroundColor = NSColor(srgbRed: 0.24, green: 0.29, blue: 0.56, alpha: 1).cgColor
        emoteLayer.font = NSFont.systemFont(ofSize: 15, weight: .semibold)
        emoteLayer.fontSize = 15
        emoteBubble.addSublayer(emoteLayer)
        layer?.addSublayer(emoteBubble)
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    /// - Parameter emoteText: 頭上の吹き出しの文字（nil で非表示）
    func render(image: CGImage?, mirrored: Bool, bob: CGFloat, emoteText: String?, scale: CGFloat) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        if image !== currentImage { spriteLayer.contents = image; currentImage = image }
        spriteLayer.contentsScale = scale
        let aspect = image.map { CGFloat($0.width) / CGFloat($0.height) } ?? 0.5
        let w = bodyHeight * aspect
        spriteLayer.bounds = CGRect(x: 0, y: 0, width: w, height: bodyHeight)
        spriteLayer.position = CGPoint(x: bounds.midX, y: bob)
        spriteLayer.setAffineTransform(mirrored ? CGAffineTransform(scaleX: -1, y: 1) : .identity)

        if let text = emoteText {
            emoteLayer.string = text
            emoteLayer.contentsScale = scale
            let textW = max(26, CGFloat(text.count) * (text.allSatisfy(\.isASCII) ? 10 : 15) + 12)
            emoteBubble.frame = CGRect(x: bounds.midX + w * 0.15, y: bodyHeight - 6, width: textW, height: 24)
            emoteLayer.frame = CGRect(x: 0, y: 2, width: textW, height: 20)
            emoteBubble.isHidden = false
        } else {
            emoteBubble.isHidden = true
        }
        CATransaction.commit()
    }

    /// 透明部分はクリックを受けない
    override func hitTest(_ point: NSPoint) -> NSView? {
        let p = convert(point, from: superview)
        return isOpaquePixel(at: p) ? self : nil
    }

    private func isOpaquePixel(at p: NSPoint) -> Bool {
        guard let img = currentImage else { return false }
        let f = spriteLayer.frame
        guard f.contains(p) else { return false }
        var u = (p.x - f.minX) / f.width
        if spriteLayer.affineTransform().a < 0 { u = 1 - u }
        let v = 1 - (p.y - f.minY) / f.height
        let x = Int(u * CGFloat(img.width)), y = Int(v * CGFloat(img.height))
        var pixel: [UInt8] = [0, 0, 0, 0]
        guard let ctx = CGContext(data: &pixel, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return true }
        ctx.draw(img, in: CGRect(x: -x, y: y - img.height + 1, width: img.width, height: img.height))
        return pixel[3] > 24
    }

    // MARK: - マウス

    override func mouseDown(with event: NSEvent) {
        dragStart = NSEvent.mouseLocation
        isDragging = false
        if let w = window {
            let feet = CGPoint(x: w.frame.midX, y: w.frame.minY)
            grabOffset = CGVector(dx: feet.x - dragStart!.x, dy: feet.y - dragStart!.y)
        }
        samples = []
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = dragStart else { return }
        let p = NSEvent.mouseLocation
        if !isDragging && hypot(p.x - start.x, p.y - start.y) > 3 {
            isDragging = true
            delegate?.mascotDragBegan()
        }
        guard isDragging else { return }
        samples.append((event.timestamp, p))
        if samples.count > 6 { samples.removeFirst() }
        delegate?.mascotDragged(feetAt: CGPoint(x: p.x + grabOffset.dx, y: p.y + grabOffset.dy))
    }

    override func mouseUp(with event: NSEvent) {
        defer { dragStart = nil; isDragging = false }
        if isDragging {
            var v = CGVector.zero
            if let a = samples.first, let b = samples.last, b.t - a.t > 0.005 {
                v = CGVector(dx: (b.p.x - a.p.x) / (b.t - a.t), dy: (b.p.y - a.p.y) / (b.t - a.t))
            }
            delegate?.mascotDragEnded(velocity: v)
        } else if event.clickCount >= 2 {
            delegate?.mascotDoubleClicked()
        } else {
            delegate?.mascotClicked()
        }
    }

    private var scrollAccumulator: CGFloat = 0

    override func scrollWheel(with event: NSEvent) {
        guard event.modifierFlags.contains(.command) else { super.scrollWheel(with: event); return }
        // トラックパッドは細かい値が続けて届くので、ある程度たまってから 1 段階変える
        let delta = event.hasPreciseScrollingDeltas ? event.scrollingDeltaY / 12 : event.scrollingDeltaY
        scrollAccumulator += delta
        let steps = Int(scrollAccumulator.rounded(.towardZero))
        guard steps != 0 else { return }
        scrollAccumulator -= CGFloat(steps)
        delegate?.mascotResize(steps: steps)
    }

    override func rightMouseDown(with event: NSEvent) {
        if let menu = delegate?.mascotMenu() { NSMenu.popUpContextMenu(menu, with: event, for: self) }
    }
}
