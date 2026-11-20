import AppKit
import Combine
import SwiftUI
import SuzuBridge
import SuzuCore

final class AppDelegate: NSObject, NSApplicationDelegate, MascotViewDelegate {
    private let settings = AppSettings()
    private let sensor = EnvironmentSensor()
    private var sprites: SpriteStore!
    private var sim: MascotSimulation!
    private var panel: MascotPanel!
    private var view: MascotView!
    private let bubble = SpeechBubble()
    private var statusItem: NSStatusItem!
    private let bridge: SecretaryBridge = DistributedNotificationBridge()
    private let conversation = ConversationModel()

    private var frameTimer: Timer?
    private var terrainTimer: Timer?
    private var lastTick = CACurrentMediaTime()
    private var lastSave = CACurrentMediaTime()
    private var animationClock: Double = 0
    private var cancellables = Set<AnyCancellable>()
    private var settingsWindow: NSWindow?
    private var conversationWindow: NSWindow?
    private var controlWindow: NSWindow?
    private let status = MascotStatus()

    // MARK: - 起動・終了

    func applicationDidFinishLaunching(_ note: Notification) {
        do { sprites = try SpriteStore() } catch {
            let a = NSAlert(); a.messageText = "鈴の画像素材を読み込めませんでした"; a.informativeText = error.localizedDescription
            a.runModal(); NSApp.terminate(nil); return
        }
        var config = MascotSimulation.Config()
        config.bodyHeight = settings.bodyHeight
        config.bodyWidth = settings.bodyHeight * 0.38
        config.paused = settings.paused
        config.allowPlaceholders = settings.allowPlaceholders
        sim = MascotSimulation(config: config, catalog: SpriteCatalog(manifest: sprites.manifest, allowMirroring: settings.allowMirroring))

        panel = MascotPanel(size: windowSize())
        view = MascotView(frame: NSRect(origin: .zero, size: windowSize()))
        view.bodyHeight = settings.bodyHeight
        view.delegate = self
        panel.contentView = view

        sensor.interactWithWindows = settings.interactWithWindows
        sensor.headroom = settings.bodyHeight + 20
        sim.updateTerrain(sensor.terrain())
        sim.place(at: initialPosition())

        setupStatusItem()
        setupMainMenu()
        observeSettings()
        observeSystem()
        setupBridge()

        if settings.mascotRunning { startMascot() } else { status.running = false }
        if settings.showControlPanelOnLaunch || !settings.mascotRunning { openControlPanel() }
    }

    /// Finder や Dock からもう一度開かれたらコントロールパネルを出す
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        openControlPanel()
        return false
    }

    // MARK: - 起動・停止（アプリ自体は動いたまま、鈴だけを出し入れする）

    private func startMascot() {
        refreshTerrain()
        if sim.terrain.screen(containing: sim.position) == nil { sim.place(at: initialPosition()) }
        render()
        panel.orderFrontRegardless()
        startTimers()
        settings.mascotRunning = true
        status.running = true
        updateStatusText()
        rebuildMenu()
    }

    private func stopMascot() {
        settings.savedPosition = sim.position
        stopTimers()
        bubble.close()
        panel.orderOut(nil)
        settings.mascotRunning = false
        status.running = false
        rebuildMenu()
    }

    @objc private func toggleRunning() {
        if status.running { stopMascot() } else { startMascot() }
    }

    private func updateStatusText() {
        let a = sim.activity
        let text: String = switch a {
        case .idle: "のんびり立っている"
        case .walk: "お散歩中"
        case .lookAround: "きょろきょろしている"
        case .turnAway: "あっちを見ている"
        case .doze: "うとうとしている"
        case .sit: "座っている"
        case .sleep: "眠っている"
        case .stretch: "伸びをしている"
        case .joy: "ごきげん"
        case .goToEdge: "画面の端へ向かっている"
        case .jumpDown: "飛び降りようとしている"
        case .climb: "よじ登っている"
        case .hang: "ぶら下がっている"
        case .react: "反応している"
        case .dragged: "つままれている"
        case .falling: "落ちている"
        }
        if status.activityText != text { status.activityText = text }
        if status.bridgeConnected != bridge.isConnected { status.bridgeConnected = bridge.isConnected }
    }

    func applicationWillTerminate(_ note: Notification) {
        settings.savedPosition = sim?.position
        stopTimers()
        bridge.stop()
        bubble.close()
    }

    private func initialPosition() -> CGPoint {
        if let p = settings.savedPosition, sim.terrain.screen(containing: p) != nil { return p }
        let s = NSScreen.main?.visibleFrame ?? .zero
        return CGPoint(x: s.midX, y: s.minY + s.height * 0.6) // 少し上から降りてくる
    }

    private func windowSize() -> NSSize {
        let h = settings.bodyHeight
        return NSSize(width: ceil(h * (sprites?.maxAspect ?? 0.45)) + 80, height: ceil(h + 34))
    }

    // MARK: - ループ

    private func startTimers() {
        guard frameTimer == nil else { return }
        lastTick = CACurrentMediaTime()
        let t = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in self?.tick() }
        t.tolerance = 0.005
        RunLoop.main.add(t, forMode: .common)
        frameTimer = t
        let e = Timer(timeInterval: 1.0, repeats: true) { [weak self] _ in self?.refreshTerrain() }
        e.tolerance = 0.2
        RunLoop.main.add(e, forMode: .common)
        terrainTimer = e
    }

    private func stopTimers() {
        frameTimer?.invalidate(); frameTimer = nil
        terrainTimer?.invalidate(); terrainTimer = nil
    }

    private func tick() {
        let now = CACurrentMediaTime()
        let dt = now - lastTick
        lastTick = now
        animationClock += dt
        sim.observeMouse(NSEvent.mouseLocation)
        sim.step(dt)
        render()
        if now - lastSave > 30 { settings.savedPosition = sim.position; lastSave = now }
    }

    private func refreshTerrain() {
        if controlWindow?.isVisible == true { updateStatusText() }
        if sim.motion == .dragged { return }
        sim.updateTerrain(sensor.terrain())
    }

    private func render() {
        let snap = sim.snapshot
        let resolved = sim.catalog.resolve(snap.pose, facing: snap.facing)
        let frameIndex = resolved.fps > 0 ? Int(animationClock * resolved.fps) % resolved.frames.count : 0
        let image = sprites.image(resolved.frames[frameIndex])
        let scale = panel.backingScaleFactor
        view.render(image: image, mirrored: resolved.mirrored, bob: snap.bobOffset, emote: snap.emote, scale: scale)
        let size = panel.frame.size
        var origin = CGPoint(x: snap.position.x - size.width / 2, y: snap.position.y)
        origin.x = (origin.x * scale).rounded() / scale
        origin.y = (origin.y * scale).rounded() / scale
        if panel.frame.origin != origin { panel.setFrameOrigin(origin) }
        if bubble.isVisible { bubble.reposition(above: spriteRectOnScreen()) }
    }

    private func spriteRectOnScreen() -> NSRect {
        NSRect(x: panel.frame.midX - 30, y: panel.frame.minY, width: 60, height: settings.bodyHeight)
    }

    // MARK: - 設定・システム

    private func observeSettings() {
        settings.$scale.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.applyScale() } }.store(in: &cancellables)
        settings.$paused.dropFirst().sink { [weak self] v in self?.sim.config.paused = v; self?.rebuildMenu() }.store(in: &cancellables)
        settings.$allowPlaceholders.dropFirst().sink { [weak self] v in self?.sim.config.allowPlaceholders = v }.store(in: &cancellables)
        settings.$allowMirroring.dropFirst().sink { [weak self] v in self?.sim.catalog.allowMirroring = v }.store(in: &cancellables)
        settings.$interactWithWindows.dropFirst().sink { [weak self] v in
            self?.sensor.interactWithWindows = v
            DispatchQueue.main.async { self?.refreshTerrain() }
        }.store(in: &cancellables)
    }

    private func applyScale() {
        let h = settings.bodyHeight
        sim.config.bodyHeight = h
        sim.config.bodyWidth = h * 0.38
        sensor.headroom = h + 20
        view.bodyHeight = h
        panel.setContentSize(windowSize())
        view.frame = NSRect(origin: .zero, size: windowSize())
        refreshTerrain()
        render()
    }

    private func observeSystem() {
        let nc = NotificationCenter.default
        nc.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            self?.refreshTerrain()
        }
        let ws = NSWorkspace.shared.notificationCenter
        // 画面スリープ中は計算を止めて CPU を使わない
        ws.addObserver(forName: NSWorkspace.screensDidSleepNotification, object: nil, queue: .main) { [weak self] _ in self?.stopTimers() }
        ws.addObserver(forName: NSWorkspace.screensDidWakeNotification, object: nil, queue: .main) { [weak self] _ in
            guard let self, self.status.running else { return }
            self.refreshTerrain(); self.startTimers()
        }
        ws.addObserver(forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
            self?.refreshTerrain()
        }
    }

    // MARK: - 鈴秘書連携

    private func setupBridge() {
        conversation.onSend = { [weak self] text in
            guard let self else { return }
            self.bridge.send(.userMessage(text))
            if !self.bridge.isConnected { self.sim.react(pose: .troubled, emote: .question, duration: 2) }
        }
        bridge.onEvent = { [weak self] event in self?.handle(event) }
        bridge.start()
    }

    private func handle(_ event: SecretaryEvent) {
        conversation.connected = bridge.isConnected
        switch event {
        case .notify(let title, let body):
            sim.react(pose: .surprise, emote: .surprise, duration: 1.5)
            bubble.show(title.isEmpty ? body : "\(title)\n\(body)", above: spriteRectOnScreen(), seconds: 8)
        case .speak(let text):
            conversation.messages.append(.init(fromSuzu: true, text: text))
            bubble.show(text, above: spriteRectOnScreen(), seconds: max(4, Double(text.count) * 0.18))
        case .expression(let pose, let duration):
            let emote: Emote = switch pose { case .joy: .joy; case .surprise: .surprise; case .troubled: .question; case .sleep: .sleepy; default: .none }
            sim.react(pose: pose, emote: emote, duration: duration)
        case .activity(let hint):
            // 集中中・会議中は静かに過ごす
            sim.config.paused = settings.paused || hint == .userFocused || hint == .meeting
            if hint == .breakTime { sim.react(pose: .joy, emote: .joy, duration: 2) }
        }
    }

    // MARK: - MascotViewDelegate

    func mascotClicked() {
        sim.poke()
        bridge.send(.userClicked)
    }

    func mascotDoubleClicked() {
        sim.react(pose: .joy, emote: .joy, duration: 2)
        bridge.send(.conversationRequested)
        openConversation()
    }

    func mascotDragBegan() { sim.beginDrag(); bubble.close() }
    func mascotDragged(feetAt p: CGPoint) { sim.drag(to: p); render() }
    func mascotDragEnded(velocity: CGVector) { sim.endDrag(velocity: velocity) }
    func mascotMenu() -> NSMenu { makeMenu() }

    // MARK: - メニュー・ウィンドウ

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "bell.fill", accessibilityDescription: "鈴")
        rebuildMenu()
    }

    /// Dock 表示時の画面上部のメニュー（⌘Q で終了、⌘W で閉じる など標準操作）
    private func setupMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "コントロールパネル", action: #selector(openControlPanel), keyEquivalent: "1").target = self
        appMenu.addItem(withTitle: "設定…", action: #selector(openSettings), keyEquivalent: ",").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "ウインドウを隠す", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "鈴を終了", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let edit = NSMenu(title: "編集")
        edit.addItem(withTitle: "取り消す", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(.separator())
        edit.addItem(withTitle: "カット", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "コピー", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "ペースト", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "すべてを選択", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)

        let windowItem = NSMenuItem()
        let windowMenu = NSMenu(title: "ウインドウ")
        windowMenu.addItem(withTitle: "閉じる", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowMenu.addItem(withTitle: "しまう", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowItem.submenu = windowMenu
        main.addItem(windowItem)
        NSApp.windowsMenu = windowMenu
        NSApp.mainMenu = main
    }

    /// Dock アイコンの右クリックメニュー
    func applicationDockMenu(_ sender: NSApplication) -> NSMenu? {
        let m = NSMenu()
        m.addItem(withTitle: status.running ? "鈴を止める" : "鈴を起動する", action: #selector(toggleRunning), keyEquivalent: "").target = self
        m.addItem(withTitle: "鈴を呼ぶ", action: #selector(summon), keyEquivalent: "").target = self
        m.addItem(withTitle: "コントロールパネル", action: #selector(openControlPanel), keyEquivalent: "").target = self
        return m
    }

    private func rebuildMenu() { statusItem?.menu = makeMenu() }

    private func makeMenu() -> NSMenu {
        let m = NSMenu()
        m.addItem(withTitle: status.running ? "鈴を止める" : "鈴を起動する", action: #selector(toggleRunning), keyEquivalent: "").target = self
        m.addItem(withTitle: "コントロールパネル…", action: #selector(openControlPanel), keyEquivalent: "").target = self
        m.addItem(.separator())
        let summonItem = m.addItem(withTitle: "鈴を呼ぶ", action: #selector(summon), keyEquivalent: "")
        summonItem.target = self
        let pause = m.addItem(withTitle: "自律行動を一時停止", action: #selector(togglePause), keyEquivalent: "")
        pause.target = self
        pause.state = settings.paused ? .on : .off
        m.addItem(.separator())
        m.addItem(withTitle: "鈴と話す…", action: #selector(openConversation), keyEquivalent: "").target = self
        m.addItem(withTitle: "設定…", action: #selector(openSettings), keyEquivalent: ",").target = self
        m.addItem(.separator())
        m.addItem(withTitle: "終了", action: #selector(quit), keyEquivalent: "q").target = self
        return m
    }

    /// カーソルのある画面に呼び寄せる
    @objc private func summon() {
        guard status.running else { startMascot(); return }
        let mouse = NSEvent.mouseLocation
        let s = NSScreen.screens.first { $0.frame.contains(mouse) } ?? NSScreen.main
        guard let f = s?.visibleFrame else { return }
        refreshTerrain()
        sim.place(at: CGPoint(x: min(max(mouse.x, f.minX + 60), f.maxX - 60), y: f.maxY - settings.bodyHeight - 10))
        sim.showEmote(.joy, for: 2)
    }

    @objc private func togglePause() { settings.paused.toggle() }

    @objc private func openControlPanel() {
        if controlWindow == nil {
            let view = ControlPanelView(
                status: status, settings: settings,
                onToggleRunning: { [weak self] in self?.toggleRunning() },
                onSummon: { [weak self] in self?.summon() },
                onConversation: { [weak self] in self?.openConversation() },
                onSettings: { [weak self] in self?.openSettings() },
                onQuit: { NSApp.terminate(nil) })
            let w = NSWindow(contentViewController: NSHostingController(rootView: view))
            w.title = "鈴 コントロールパネル"
            w.styleMask = [.titled, .closable, .miniaturizable]
            w.isReleasedWhenClosed = false
            w.center()
            controlWindow = w
        }
        updateStatusText()
        NSApp.activate()
        controlWindow?.makeKeyAndOrderFront(nil)
    }

    @objc private func openSettings() {
        if settingsWindow == nil {
            let missing = sim.catalog.missingPoses().map { "\($0.0.rawValue)/\($0.1.rawValue)" }
            let w = NSWindow(contentViewController: NSHostingController(rootView: SettingsView(settings: settings, missingAssets: missing)))
            w.title = "鈴の設定"
            w.styleMask = [.titled, .closable]
            w.isReleasedWhenClosed = false
            settingsWindow = w
        }
        NSApp.activate()
        settingsWindow?.center()
        settingsWindow?.makeKeyAndOrderFront(nil)
    }

    @objc private func openConversation() {
        conversation.connected = bridge.isConnected
        if conversationWindow == nil {
            let w = NSWindow(contentViewController: NSHostingController(rootView: ConversationView(model: conversation)))
            w.title = "鈴と話す"
            w.styleMask = [.titled, .closable, .resizable]
            w.isReleasedWhenClosed = false
            w.setContentSize(NSSize(width: 360, height: 420))
            conversationWindow = w
        }
        NSApp.activate()
        if let w = conversationWindow, !w.isVisible {
            w.setFrameTopLeftPoint(NSPoint(x: panel.frame.maxX + 8, y: panel.frame.maxY + 200))
            if let s = panel.screen { w.setFrame(w.frame.intersection(s.visibleFrame).isEmpty ? w.frame : w.constrainFrameRect(w.frame, to: s), display: false) }
        }
        conversationWindow?.makeKeyAndOrderFront(nil)
    }

    @objc private func quit() { NSApp.terminate(nil) }
}
