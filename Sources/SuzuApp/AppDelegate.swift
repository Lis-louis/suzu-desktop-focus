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

    /// モニターのリフレッシュに合わせて呼ばれる（鈴のいる画面に自動で追従する）
    private var displayLink: CADisplayLink?
    private var framesSinceMeasure = 0
    private var lastMeasure = CACurrentMediaTime()
    private var terrainTimer: Timer?
    private var lastTick = CACurrentMediaTime()
    private var lastSave = CACurrentMediaTime()
    private var animationClock: Double = 0
    private var cancellables = Set<AnyCancellable>()
    private var settingsWindow: NSWindow?
    private var conversationWindow: NSWindow?
    private var controlWindow: NSWindow?
    private let status = MascotStatus()
    private let updater = Updater()

    // MARK: - 起動・終了

    func applicationDidFinishLaunching(_ note: Notification) {
        do { sprites = try SpriteStore() } catch {
            let a = NSAlert(); a.messageText = settings.language.pick("鈴の画像素材を読み込めませんでした", "Couldn't load Suzu's image assets"); a.informativeText = error.localizedDescription
            a.runModal(); NSApp.terminate(nil); return
        }
        var config = MascotSimulation.Config()
        config.bodyHeight = settings.bodyHeight
        config.bodyWidth = settings.bodyHeight * 0.38
        config.paused = settings.paused
        config.walkSpeed = 42 * settings.moveSpeed
        config.climbSpeed = 28 * settings.moveSpeed
        config.allowPlaceholders = settings.allowPlaceholders
        sim = MascotSimulation(config: config, catalog: SpriteCatalog(manifest: sprites.manifest, allowMirroring: settings.allowMirroring))

        panel = MascotPanel(size: windowSize())
        view = MascotView(frame: NSRect(origin: .zero, size: windowSize()))
        view.bodyHeight = settings.bodyHeight
        view.delegate = self
        panel.contentView = view
        applyWindowAppearance()
        applyFrameRate()

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
        setupUpdater()
    }

    /// 自動更新：新しい版がダウンロードできたら、メニューに出して鈴の吹き出しで知らせる
    private func setupUpdater() {
        updater.$state.removeDuplicates().dropFirst().sink { [weak self] _ in
            DispatchQueue.main.async { self?.rebuildMenu() }
        }.store(in: &cancellables)
        updater.onReady = { [weak self] in
            guard let self, self.status.running, let v = self.updater.release?.version else { return }
            self.bubble.show(self.L.pick("新しいバージョン \(v) が届きました。メニューの「アップデートをインストール」から入れ替えられます。",
                                         "Version \(v) has arrived. Choose \"Install Update\" from the menu."),
                             above: self.spriteRectOnScreen(), seconds: 8)
        }
        updater.start()
    }

    @objc private func installUpdate() { updater.install() }

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
        let text = L.activityText(sim.activity)
        if status.activityText != text { status.activityText = text }
        if status.bridgeConnected != bridge.isConnected { status.bridgeConnected = bridge.isConnected }
    }

    /// 現在の表示言語
    private var L: AppLanguage { settings.language }

    /// 言語変更をメニュー・ウィンドウタイトルなど AppKit 側に反映する
    private func applyLanguage() {
        setupMainMenu()
        rebuildMenu()
        updateStatusText()
        controlWindow?.title = L.pick("鈴 コントロールパネル", "Suzu Control Panel")
        settingsWindow?.title = L.pick("鈴の設定", "Suzu Settings")
        conversationWindow?.title = L.pick("鈴と話す", "Chat with Suzu")
        statusItem?.button?.image?.accessibilityDescription = L.pick("鈴", "Suzu")
        render()
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
        guard displayLink == nil else { return }
        lastTick = CACurrentMediaTime()
        lastMeasure = lastTick
        framesSinceMeasure = 0
        let link = view.displayLink(target: self, selector: #selector(displayTick(_:)))
        link.add(to: .main, forMode: .common)
        displayLink = link
        applyFrameRate()
        let e = Timer(timeInterval: 1.0, repeats: true) { [weak self] _ in self?.refreshTerrain() }
        e.tolerance = 0.2
        RunLoop.main.add(e, forMode: .common)
        terrainTimer = e
    }

    private func stopTimers() {
        displayLink?.invalidate(); displayLink = nil
        terrainTimer?.invalidate(); terrainTimer = nil
    }

    @objc private func displayTick(_ link: CADisplayLink) { tick() }

    /// 鈴のいるモニターのリフレッシュレート
    private var currentRefreshRate: Int {
        max(1, (panel.screen ?? NSScreen.main)?.maximumFramesPerSecond ?? 60)
    }

    /// 設定の上限とモニターのリフレッシュレートから描画回数を決める
    private func applyFrameRate() {
        let hz = Float(currentRefreshRate)
        let target = settings.frameRate > 0 ? min(Float(settings.frameRate), hz) : hz
        displayLink?.preferredFrameRateRange = CAFrameRateRange(minimum: min(30, target), maximum: target, preferred: target)
        status.refreshRate = Int(hz)
        status.maxRefreshRate = NSScreen.screens.map(\.maximumFramesPerSecond).max() ?? Int(hz)
    }

    /// 1 秒ごと：実際の fps を測り、モニターが変わっていればフレームレートを合わせ直す
    private func measureFrameRate() {
        let now = CACurrentMediaTime()
        let elapsed = now - lastMeasure
        if elapsed > 0 {
            let fps = Int((Double(framesSinceMeasure) / elapsed).rounded())
            if status.settingsVisible, status.measuredFPS != fps { status.measuredFPS = fps }
        }
        framesSinceMeasure = 0
        lastMeasure = now
        if currentRefreshRate != status.refreshRate { applyFrameRate() }
    }

    private func tick() {
        let now = CACurrentMediaTime()
        framesSinceMeasure += 1
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
        if displayLink != nil { measureFrameRate() }
        if sim.motion == .dragged { return }
        sim.updateTerrain(sensor.terrain())
    }

    private func render() {
        let snap = sim.snapshot
        let resolved = sim.catalog.resolve(snap.pose, facing: snap.facing)
        let frameIndex = resolved.fps > 0 ? Int(animationClock * resolved.fps) % resolved.frames.count : 0
        let image = sprites.image(resolved.frames[frameIndex])
        let scale = panel.backingScaleFactor
        view.render(image: image, mirrored: resolved.mirrored, bob: snap.bobOffset, emoteText: L.emoteText(snap.emote), scale: scale)
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
        settings.$opacity.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.applyWindowAppearance() } }.store(in: &cancellables)
        settings.$alwaysOnTop.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.applyWindowAppearance(); self?.rebuildMenu() } }.store(in: &cancellables)
        settings.$moveSpeed.dropFirst().sink { [weak self] v in
            self?.sim.config.walkSpeed = 42 * v
            self?.sim.config.climbSpeed = 28 * v
        }.store(in: &cancellables)
        settings.$frameRate.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.applyFrameRate() } }.store(in: &cancellables)
        settings.$language.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.applyLanguage() } }.store(in: &cancellables)
        settings.$interactWithWindows.dropFirst().sink { [weak self] v in
            self?.sensor.interactWithWindows = v
            DispatchQueue.main.async { self?.refreshTerrain() }
        }.store(in: &cancellables)
    }

    /// 不透明度・最前面表示を反映する（吹き出しは読みやすさのため常に不透明）
    private func applyWindowAppearance() {
        panel.alphaValue = CGFloat(settings.opacity)
        panel.level = settings.alwaysOnTop ? .floating : .normal
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
        rebuildMenu()
    }

    private func observeSystem() {
        let nc = NotificationCenter.default
        nc.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            self?.refreshTerrain()
            self?.applyFrameRate()
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
    func mascotResize(steps: Int) { settings.adjustScale(by: steps) }

    // MARK: - メニュー・ウィンドウ

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "bell.fill", accessibilityDescription: L.pick("鈴", "Suzu"))
        rebuildMenu()
    }

    /// Dock 表示時の画面上部のメニュー（⌘Q で終了、⌘W で閉じる など標準操作）
    private func setupMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: L.pick("コントロールパネル", "Control Panel"), action: #selector(openControlPanel), keyEquivalent: "1").target = self
        appMenu.addItem(withTitle: L.pick("設定…", "Settings…"), action: #selector(openSettings), keyEquivalent: ",").target = self
        appMenu.addItem(withTitle: L.pick("アップデートを確認…", "Check for Updates…"), action: #selector(checkForUpdates), keyEquivalent: "").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: L.pick("ウインドウを隠す", "Hide Windows"), action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: L.pick("鈴を終了", "Quit Suzu"), action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let edit = NSMenu(title: L.pick("編集", "Edit"))
        edit.addItem(withTitle: L.pick("取り消す", "Undo"), action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(.separator())
        edit.addItem(withTitle: L.pick("カット", "Cut"), action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: L.pick("コピー", "Copy"), action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: L.pick("ペースト", "Paste"), action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: L.pick("すべてを選択", "Select All"), action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)

        let windowItem = NSMenuItem()
        let windowMenu = NSMenu(title: L.pick("ウインドウ", "Window"))
        windowMenu.addItem(withTitle: L.pick("閉じる", "Close"), action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowMenu.addItem(withTitle: L.pick("しまう", "Minimize"), action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowItem.submenu = windowMenu
        main.addItem(windowItem)
        NSApp.windowsMenu = windowMenu
        NSApp.mainMenu = main
    }

    /// Dock アイコンの右クリックメニュー
    func applicationDockMenu(_ sender: NSApplication) -> NSMenu? {
        let m = NSMenu()
        m.addItem(withTitle: status.running ? L.pick("鈴を止める", "Stop Suzu") : L.pick("鈴を起動する", "Start Suzu"), action: #selector(toggleRunning), keyEquivalent: "").target = self
        m.addItem(withTitle: L.pick("鈴を呼ぶ", "Call Suzu"), action: #selector(summon), keyEquivalent: "").target = self
        m.addItem(withTitle: L.pick("コントロールパネル", "Control Panel"), action: #selector(openControlPanel), keyEquivalent: "").target = self
        return m
    }

    private func rebuildMenu() { statusItem?.menu = makeMenu() }

    private func makeMenu() -> NSMenu {
        let m = NSMenu()
        if updater.state == .ready, let v = updater.release?.version {
            m.addItem(withTitle: L.pick("アップデートをインストール（\(v)）…", "Install Update (\(v))…"), action: #selector(installUpdate), keyEquivalent: "").target = self
            m.addItem(.separator())
        }
        m.addItem(withTitle: status.running ? L.pick("鈴を止める", "Stop Suzu") : L.pick("鈴を起動する", "Start Suzu"), action: #selector(toggleRunning), keyEquivalent: "").target = self
        m.addItem(withTitle: L.pick("コントロールパネル…", "Control Panel…"), action: #selector(openControlPanel), keyEquivalent: "").target = self
        m.addItem(.separator())
        let summonItem = m.addItem(withTitle: L.pick("鈴を呼ぶ", "Call Suzu"), action: #selector(summon), keyEquivalent: "")
        summonItem.target = self
        let pause = m.addItem(withTitle: L.pick("自律行動を一時停止", "Pause Autonomous Behavior"), action: #selector(togglePause), keyEquivalent: "")
        pause.target = self
        pause.state = settings.paused ? .on : .off
        m.addItem(makeSizeMenuItem())
        let top = m.addItem(withTitle: L.pick("ほかのウィンドウより手前に表示", "Keep Above Other Windows"), action: #selector(toggleAlwaysOnTop), keyEquivalent: "")
        top.target = self
        top.state = settings.alwaysOnTop ? .on : .off
        m.addItem(.separator())
        m.addItem(withTitle: L.pick("鈴と話す…", "Chat with Suzu…"), action: #selector(openConversation), keyEquivalent: "").target = self
        m.addItem(withTitle: L.pick("設定…", "Settings…"), action: #selector(openSettings), keyEquivalent: ",").target = self
        m.addItem(.separator())
        m.addItem(withTitle: L.pick("終了", "Quit"), action: #selector(quit), keyEquivalent: "q").target = self
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
    @objc private func toggleAlwaysOnTop() { settings.alwaysOnTop.toggle() }

    /// 「大きさ」サブメニュー（よく使う大きさ＋1 段階ずつの調整）
    private func makeSizeMenuItem() -> NSMenuItem {
        let item = NSMenuItem(title: L.pick("大きさ（\(Int((settings.scale * 100).rounded()))%）", "Size (\(Int((settings.scale * 100).rounded()))%)"), action: nil, keyEquivalent: "")
        let sub = NSMenu()
        let larger = sub.addItem(withTitle: L.pick("大きくする", "Larger"), action: #selector(scaleUp), keyEquivalent: "")
        larger.target = self
        larger.isEnabled = settings.scale < AppSettings.scaleRange.upperBound
        let smaller = sub.addItem(withTitle: L.pick("小さくする", "Smaller"), action: #selector(scaleDown), keyEquivalent: "")
        smaller.target = self
        smaller.isEnabled = settings.scale > AppSettings.scaleRange.lowerBound
        sub.addItem(.separator())
        for preset in AppSettings.scalePresets {
            let title = "\(Int(preset * 100))%" + (preset == 1.0 ? L.pick("（標準）", " (standard)") : "")
            let p = sub.addItem(withTitle: title, action: #selector(setScalePreset(_:)), keyEquivalent: "")
            p.target = self
            p.representedObject = preset
            p.state = abs(settings.scale - preset) < 0.001 ? .on : .off
        }
        sub.autoenablesItems = false
        item.submenu = sub
        return item
    }

    @objc private func scaleUp() { settings.adjustScale(by: 1) }
    @objc private func scaleDown() { settings.adjustScale(by: -1) }
    @objc private func setScalePreset(_ sender: NSMenuItem) {
        if let v = sender.representedObject as? Double { settings.scale = v }
    }

    @objc private func openControlPanel() {
        if controlWindow == nil {
            let view = ControlPanelView(
                status: status, settings: settings, updater: updater,
                onToggleRunning: { [weak self] in self?.toggleRunning() },
                onSummon: { [weak self] in self?.summon() },
                onConversation: { [weak self] in self?.openConversation() },
                onSettings: { [weak self] in self?.openSettings() },
                onQuit: { NSApp.terminate(nil) })
            let w = NSWindow(contentViewController: NSHostingController(rootView: view))
            w.title = L.pick("鈴 コントロールパネル", "Suzu Control Panel")
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
            let w = NSWindow(contentViewController: NSHostingController(rootView: SettingsView(settings: settings, status: status, updater: updater, missingAssets: missing)))
            w.title = L.pick("鈴の設定", "Suzu Settings")
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
            let w = NSWindow(contentViewController: NSHostingController(rootView: ConversationView(model: conversation, settings: settings)))
            w.title = L.pick("鈴と話す", "Chat with Suzu")
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

    /// 手動で確認する（結果は設定画面の「アプリ」に出る）
    @objc private func checkForUpdates() {
        openSettings()
        updater.check(manual: true)
    }

    @objc private func quit() { NSApp.terminate(nil) }
}
