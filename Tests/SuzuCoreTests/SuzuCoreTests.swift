import XCTest
import CoreGraphics
@testable import SuzuCore
@testable import SuzuBridge

final class TerrainTests: XCTestCase {
    let screen = ScreenInfo(id: 1, visibleFrame: CGRect(x: 0, y: 80, width: 1600, height: 900))

    func testScreenFloorAndWalls() {
        let t = Terrain.build(screens: [screen], windows: [], headroom: 200)
        XCTAssertEqual(t.floors.count, 1)
        XCTAssertEqual(t.floors[0].y, 80)
        XCTAssertEqual(t.walls.count, 2)
    }

    func testWindowTopBecomesFloor() {
        let w = WindowInfo(id: 10, frame: CGRect(x: 200, y: 200, width: 600, height: 400)) // top = 600
        let t = Terrain.build(screens: [screen], windows: [w], headroom: 200)
        let f = t.floors.first { $0.owner == .window(10) }
        XCTAssertEqual(f?.y, 600)
        XCTAssertEqual(f?.minX, 200)
        XCTAssertEqual(f?.maxX, 800)
    }

    func testWindowTopTooHighIsIgnored() {
        // 頭が画面上端（980）を超えてしまう高さ
        let w = WindowInfo(id: 10, frame: CGRect(x: 200, y: 200, width: 600, height: 700)) // top = 900
        let t = Terrain.build(screens: [screen], windows: [w], headroom: 200)
        XCTAssertNil(t.floors.first { $0.owner == .window(10) })
    }

    func testOccludedPartIsRemoved() {
        let front = WindowInfo(id: 1, frame: CGRect(x: 400, y: 300, width: 200, height: 400)) // 600 を跨ぐ
        let back = WindowInfo(id: 2, frame: CGRect(x: 200, y: 200, width: 600, height: 400))  // top 600
        let t = Terrain.build(screens: [screen], windows: [front, back], headroom: 200)
        let segs = t.floors.filter { $0.owner == .window(2) }.map { ($0.minX, $0.maxX) }
        XCTAssertEqual(segs.count, 2)
        XCTAssertEqual(segs[0].0, 200); XCTAssertEqual(segs[0].1, 400)
        XCTAssertEqual(segs[1].0, 600); XCTAssertEqual(segs[1].1, 800)
    }

    func testFloorBelowPicksHighest() {
        let w = WindowInfo(id: 10, frame: CGRect(x: 200, y: 200, width: 600, height: 400))
        let t = Terrain.build(screens: [screen], windows: [w], headroom: 200)
        XCTAssertEqual(t.floor(below: CGPoint(x: 300, y: 700))?.y, 600)
        XCTAssertEqual(t.floor(below: CGPoint(x: 100, y: 700))?.y, 80)
    }
}

final class SpriteCatalogTests: XCTestCase {
    let manifest = SpriteManifest(entries: [
        .init(pose: .stand, facing: .front, frames: ["front"]),
        .init(pose: .stand, facing: .right, frames: ["side_right"]),
        .init(pose: .walk, facing: .right, frames: ["side_right"], bob: true),
    ])

    func testExactMatch() {
        let r = SpriteCatalog(manifest: manifest).resolve(.walk, facing: .right)
        XCTAssertEqual(r.frames, ["side_right"]); XCTAssertFalse(r.mirrored); XCTAssertFalse(r.isFallback); XCTAssertTrue(r.bob)
    }

    func testNoMirroringByDefault() {
        // 反転すると髪飾りの位置が逆になるため、既定では反転しない
        let r = SpriteCatalog(manifest: manifest).resolve(.walk, facing: .left)
        XCTAssertFalse(r.mirrored)
        XCTAssertEqual(r.frames, ["front"])
        XCTAssertTrue(r.isFallback)
    }

    func testMirroringWhenAllowed() {
        let r = SpriteCatalog(manifest: manifest, allowMirroring: true).resolve(.walk, facing: .left)
        XCTAssertTrue(r.mirrored); XCTAssertEqual(r.frames, ["side_right"]); XCTAssertFalse(r.isFallback)
    }

    func testMissingPoseFallsBackToStand() {
        let r = SpriteCatalog(manifest: manifest).resolve(.sleep, facing: .front)
        XCTAssertEqual(r.frames, ["front"]); XCTAssertTrue(r.isFallback)
    }

    func testMissingPosesReport() {
        let missing = SpriteCatalog(manifest: manifest).missingPoses()
        XCTAssertTrue(missing.contains { $0 == (.sleep, .front) })
        XCTAssertTrue(missing.contains { $0 == (.walk, .left) })
        XCTAssertFalse(missing.contains { $0 == (.walk, .right) })
    }

    func testBundledManifestDecodes() throws {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("Resources/Sprites/manifest.json")
        let m = try SpriteManifest.load(from: url)
        let files = try FileManager.default.contentsOfDirectory(atPath: url.deletingLastPathComponent().path)
        let names = Set(files.filter { $0.hasSuffix(".png") }.map { String($0.dropLast(4)) })
        XCTAssertEqual(SpriteCatalog(manifest: m).missingFiles(existing: names), [])
    }
}

final class PlannerTests: XCTestCase {
    func ctx(energy: Double = 0.8, hour: Int = 14, onWindow: Bool = false, last: Activity? = nil, all: Bool = true) -> BehaviorContext {
        BehaviorContext(energy: energy, hour: hour, onWindow: onWindow, last: last) { s in all || [.stand, .walk].contains(s.pose) }
    }

    func testUnavailablePosesAreNeverChosen() {
        var p = BehaviorPlanner(); var rng = SplitMix64(seed: 1)
        for i in 0..<500 {
            let (a, _) = p.choose(in: ctx(all: false), now: Double(i) * 1000, rng: &rng)
            XCTAssertFalse([.sit, .sleep, .climb, .hang, .stretch, .joy].contains(a), "\(a)")
        }
    }

    func testTiredAtNightSleepsMore() {
        let p = BehaviorPlanner()
        let sleep = p.spec(.sleep)!
        XCTAssertEqual(p.weight(of: sleep, in: ctx(energy: 1, hour: 14), now: 0), 0)
        XCTAssertGreaterThan(p.weight(of: sleep, in: ctx(energy: 0.1, hour: 2), now: 0), 40)
    }

    func testPlacementRules() {
        let p = BehaviorPlanner()
        XCTAssertEqual(p.weight(of: p.spec(.jumpDown)!, in: ctx(onWindow: false), now: 0), 0)
        XCTAssertGreaterThan(p.weight(of: p.spec(.jumpDown)!, in: ctx(onWindow: true), now: 0), 0)
        XCTAssertEqual(p.weight(of: p.spec(.climb)!, in: ctx(onWindow: true), now: 0), 0)
    }

    func testCooldownPreventsImmediateRepeat() {
        var p = BehaviorPlanner(specs: [BehaviorPlanner().spec(.lookAround)!, BehaviorPlanner().spec(.idle)!])
        var rng = SplitMix64(seed: 3)
        var sawLook = false
        for _ in 0..<50 {
            let (a, _) = p.choose(in: ctx(), now: 0, rng: &rng)
            if a == .lookAround { XCTAssertFalse(sawLook, "cooldown ignored"); sawLook = true }
        }
    }

    func testNotPurelyUniform() {
        var p = BehaviorPlanner(); var rng = SplitMix64(seed: 9)
        var counts: [Activity: Int] = [:]
        for i in 0..<3000 { counts[p.choose(in: ctx(all: false), now: Double(i) * 1000, rng: &rng).0, default: 0] += 1 }
        XCTAssertGreaterThan(counts[.idle, default: 0], counts[.turnAway, default: 0] * 3)
    }
}

final class SimulationTests: XCTestCase {
    let screen = ScreenInfo(id: 1, visibleFrame: CGRect(x: 0, y: 80, width: 1600, height: 900))
    let manifest = SpriteManifest(entries: [
        .init(pose: .stand, facing: .front, frames: ["front"]),
        .init(pose: .walk, facing: .right, frames: ["side_right"], bob: true),
    ])

    func makeSim(windows: [WindowInfo] = [], placeholders: Bool = false) -> MascotSimulation {
        var c = MascotSimulation.Config(); c.allowPlaceholders = placeholders
        let sim = MascotSimulation(config: c, catalog: SpriteCatalog(manifest: manifest), seed: 42)
        sim.hourProvider = { 14 }
        sim.updateTerrain(Terrain.build(screens: [screen], windows: windows, headroom: 200))
        return sim
    }

    func run(_ sim: MascotSimulation, seconds: Double) { for _ in 0..<Int(seconds * 30) { sim.step(1.0 / 30) } }

    func testFallsAndLandsOnScreenFloor() {
        let sim = makeSim()
        sim.place(at: CGPoint(x: 500, y: 700))
        run(sim, seconds: 2)
        XCTAssertEqual(sim.position.y, 80, accuracy: 0.01)
        if case .grounded(let f) = sim.motion { XCTAssertEqual(f.owner, .screen(1)) } else { XCTFail() }
    }

    func testLandsOnWindowTop() {
        let sim = makeSim(windows: [WindowInfo(id: 7, frame: CGRect(x: 300, y: 200, width: 600, height: 400))])
        sim.place(at: CGPoint(x: 500, y: 900))
        run(sim, seconds: 2)
        XCTAssertEqual(sim.position.y, 600, accuracy: 0.01)
    }

    func testFollowsMovingWindowAndFallsWhenClosed() {
        let w = WindowInfo(id: 7, frame: CGRect(x: 300, y: 200, width: 600, height: 400))
        let sim = makeSim(windows: [w])
        sim.config.paused = true
        sim.place(at: CGPoint(x: 500, y: 600))
        var moved = w; moved.frame.origin = CGPoint(x: 350, y: 150)
        sim.updateTerrain(Terrain.build(screens: [screen], windows: [moved], headroom: 200))
        XCTAssertEqual(sim.position.x, 550, accuracy: 0.01)
        XCTAssertEqual(sim.position.y, 550, accuracy: 0.01)
        sim.updateTerrain(Terrain.build(screens: [screen], windows: [], headroom: 200))
        XCTAssertEqual(sim.activity, .falling)
        run(sim, seconds: 2)
        XCTAssertEqual(sim.position.y, 80, accuracy: 0.01)
    }

    func testStaysInsideScreensForLongRun() {
        let sim = makeSim(windows: [WindowInfo(id: 7, frame: CGRect(x: 300, y: 200, width: 600, height: 400))], placeholders: true)
        sim.place(at: CGPoint(x: 800, y: 80))
        for _ in 0..<(30 * 60 * 30) { // 30 分ぶん
            sim.step(1.0 / 30)
            XCTAssertTrue(screen.visibleFrame.insetBy(dx: -1, dy: -200).contains(sim.position), "\(sim.position) \(sim.activity)")
        }
    }

    func testDragAndThrow() {
        let sim = makeSim()
        sim.place(at: CGPoint(x: 500, y: 80))
        sim.beginDrag()
        sim.drag(to: CGPoint(x: 900, y: 600))
        XCTAssertEqual(sim.snapshot.pose, .dragged)
        sim.endDrag(velocity: CGVector(dx: 300, dy: 0))
        run(sim, seconds: 3)
        XCTAssertEqual(sim.position.y, 80, accuracy: 0.01)
        XCTAssertGreaterThan(sim.position.x, 900)
    }

    func testJumpDownFromWindow() {
        let sim = makeSim(windows: [WindowInfo(id: 7, frame: CGRect(x: 300, y: 200, width: 600, height: 400))])
        sim.place(at: CGPoint(x: 500, y: 600))
        sim.force(.jumpDown)
        run(sim, seconds: 15)
        XCTAssertEqual(sim.position.y, 80, accuracy: 0.01)
    }

    func testClickWakesFromDoze() {
        let sim = makeSim()
        sim.place(at: CGPoint(x: 500, y: 80))
        sim.force(.doze)
        XCTAssertEqual(sim.snapshot.emote, .sleepy)
        sim.poke()
        XCTAssertEqual(sim.activity, .react)
        XCTAssertEqual(sim.snapshot.emote, .surprise)
    }

    func testClimbAndHangWithPlaceholders() {
        let sim = makeSim(windows: [WindowInfo(id: 7, frame: CGRect(x: 300, y: 200, width: 600, height: 400))], placeholders: true)
        sim.place(at: CGPoint(x: 100, y: 80))
        sim.force(.climb)
        run(sim, seconds: 6)
        if case .climbing = sim.motion {} else { XCTFail("expected climbing, got \(sim.motion)") }
        sim.place(at: CGPoint(x: 500, y: 600))
        sim.force(.hang)
        run(sim, seconds: 6)
        if case .hanging = sim.motion {} else { XCTFail("expected hanging, got \(sim.motion)") }
    }
}

final class MultiScreenTests: XCTestCase {
    // 左：Dock ありの内蔵画面（床 80）、右：外部モニタ（床 0 と 300 の 2 パターン）
    let left = ScreenInfo(id: 1, visibleFrame: CGRect(x: 0, y: 80, width: 1600, height: 900),
                          frame: CGRect(x: 0, y: 0, width: 1600, height: 1000))
    let manifest = SpriteManifest(entries: [
        .init(pose: .stand, facing: .front, frames: ["front"]),
        .init(pose: .walk, facing: .right, frames: ["side_right"], bob: true),
    ])

    func right(floorY: CGFloat) -> ScreenInfo {
        ScreenInfo(id: 2, visibleFrame: CGRect(x: 1600, y: floorY, width: 1920, height: 1000),
                   frame: CGRect(x: 1600, y: floorY, width: 1920, height: 1025))
    }

    func makeSim(_ screens: [ScreenInfo]) -> MascotSimulation {
        let sim = MascotSimulation(catalog: SpriteCatalog(manifest: manifest), seed: 7)
        sim.hourProvider = { 14 }
        sim.updateTerrain(Terrain.build(screens: screens, windows: [], headroom: 200))
        return sim
    }

    func testSharedEdgeIsNotAWall() {
        let t = Terrain.build(screens: [left, right(floorY: 0)], windows: [], headroom: 200)
        XCTAssertEqual(t.walls.count, 2)
        XCTAssertEqual(t.adjacentScreen(to: left, side: .right)?.id, 2)
        XCTAssertNil(t.adjacentScreen(to: left, side: .left))
    }

    func crosses(to floorY: CGFloat, from x: CGFloat = 1200) {
        let sim = makeSim([left, right(floorY: floorY)])
        sim.config.paused = true
        sim.place(at: CGPoint(x: x, y: 80))
        sim.force(.crossScreen)
        XCTAssertEqual(sim.activity, .crossScreen)
        for _ in 0..<(30 * 40) { sim.step(1.0 / 30); if sim.position.x > 1600, case .grounded = sim.motion { break } }
        guard case .grounded(let f) = sim.motion else { return XCTFail("not grounded: \(sim.motion)") }
        XCTAssertEqual(f.owner, .screen(2))
        XCTAssertEqual(sim.position.y, floorY, accuracy: 0.01)
        XCTAssertGreaterThan(sim.position.x, 1600)
    }

    func testCrossToLowerScreen() { crosses(to: 0) }
    func testCrossToHigherScreen() { crosses(to: 300) }
    func testCrossToSameHeightScreen() { crosses(to: 80) }

    func testThrowAcrossSharedEdge() {
        let sim = makeSim([left, right(floorY: 0)])
        sim.place(at: CGPoint(x: 1500, y: 80))
        sim.beginDrag(); sim.drag(to: CGPoint(x: 1580, y: 500))
        sim.endDrag(velocity: CGVector(dx: 600, dy: 0))
        for _ in 0..<90 { sim.step(1.0 / 30) }
        XCTAssertGreaterThan(sim.position.x, 1600)
        XCTAssertEqual(sim.position.y, 0, accuracy: 0.01)
    }

    func testEventuallyVisitsOtherScreenOnItsOwn() {
        let sim = makeSim([left, right(floorY: 0)])
        sim.place(at: CGPoint(x: 800, y: 80))
        var visited = false
        for _ in 0..<(30 * 60 * 30) {
            sim.step(1.0 / 30)
            if sim.position.x > 1600 { visited = true; break }
        }
        XCTAssertTrue(visited)
    }
}

final class BallTests: XCTestCase {
    let screen = ScreenInfo(id: 1, visibleFrame: CGRect(x: 0, y: 80, width: 1600, height: 900))
    lazy var floor = Terrain.build(screens: [screen], windows: [], headroom: 200)
    let manifest = SpriteManifest(entries: [
        .init(pose: .stand, facing: .front, frames: ["front"]),
        .init(pose: .walk, facing: .right, frames: ["side_right"], bob: true),
    ])

    func run(_ b: Ball, seconds: Double, terrain: Terrain? = nil) {
        for _ in 0..<Int(seconds * 60) { b.step(1.0 / 60, terrain: terrain ?? floor, gravity: 2200) }
    }

    func testBouncesThenRestsOnFloor() {
        let b = Ball(center: CGPoint(x: 500, y: 700), radius: 18)
        var bounced = false
        for _ in 0..<(60 * 4) {
            b.step(1.0 / 60, terrain: floor, gravity: 2200)
            if b.velocity.dy > 0 { bounced = true }
        }
        XCTAssertTrue(bounced)
        XCTAssertTrue(b.onFloor)
        XCTAssertEqual(b.position.y, 80 + 18, accuracy: 1)
    }

    func testRollsToAStop() {
        let b = Ball(center: CGPoint(x: 500, y: 98), radius: 18)
        run(b, seconds: 0.5)
        b.kick(CGVector(dx: 300, dy: 0))
        run(b, seconds: 8)
        XCTAssertTrue(b.isResting)
        XCTAssertGreaterThan(b.position.x, 520)
    }

    func testBouncesOffScreenEdges() {
        let b = Ball(center: CGPoint(x: 1500, y: 300), radius: 18)
        b.kick(CGVector(dx: 2000, dy: 0))
        run(b, seconds: 3)
        XCTAssertGreaterThanOrEqual(b.position.x, 18)
        XCTAssertLessThanOrEqual(b.position.x, 1600 - 18)
    }

    func testLandsOnWindowTop() {
        let t = Terrain.build(screens: [screen], windows: [WindowInfo(id: 7, frame: CGRect(x: 300, y: 200, width: 600, height: 400))], headroom: 200)
        let b = Ball(center: CGPoint(x: 500, y: 900), radius: 18)
        run(b, seconds: 4, terrain: t)
        XCTAssertEqual(b.position.y, 600 + 18, accuracy: 1)
    }

    func testHeldBallDoesNotFallAndCanBeThrown() {
        let b = Ball(center: CGPoint(x: 500, y: 700), radius: 18)
        b.hold(at: CGPoint(x: 600, y: 800))
        run(b, seconds: 1)
        XCTAssertEqual(b.position, CGPoint(x: 600, y: 800))
        b.release(velocity: CGVector(dx: -500, dy: 300))
        b.step(1.0 / 60, terrain: floor, gravity: 2200)
        XCTAssertFalse(b.held)
        XCTAssertLessThan(b.position.x, 600)
    }

    func testRollingRightSpinsClockwise() {
        let b = Ball(center: CGPoint(x: 500, y: 98), radius: 18)
        b.step(1.0 / 60, terrain: floor, gravity: 2200)
        b.kick(CGVector(dx: 300, dy: 0))
        run(b, seconds: 0.5)
        XCTAssertGreaterThan(b.angle, 0)
    }

    func makeSim(allowBall: Bool = true) -> MascotSimulation {
        var c = MascotSimulation.Config(); c.allowBallPlay = allowBall
        let sim = MascotSimulation(config: c, catalog: SpriteCatalog(manifest: manifest), seed: 7)
        sim.hourProvider = { 14 }
        sim.updateTerrain(floor)
        sim.place(at: CGPoint(x: 800, y: 80))
        return sim
    }

    func testChasesAndKicksTheBall() {
        let sim = makeSim()
        sim.ball = Ball(center: CGPoint(x: 1000, y: 98), radius: 18)
        sim.force(.playBall, duration: 30)
        var i = 0
        while i < 30 * 20 && sim.kicks == 0 { sim.step(1.0 / 30); i += 1 }
        XCTAssertGreaterThan(sim.kicks, 0)
        XCTAssertEqual(sim.position.x, 1000, accuracy: 50)
    }

    func testKeepsPlayingAndThenStops() {
        let sim = makeSim()
        sim.ball = Ball(center: CGPoint(x: 900, y: 98), radius: 18)
        sim.force(.playBall, duration: 25)
        for _ in 0..<(30 * 25) { sim.step(1.0 / 30) }
        XCTAssertGreaterThanOrEqual(sim.kicks, 3, "kicks=\(sim.kicks)")
        for _ in 0..<(30 * 30) { sim.step(1.0 / 30) }
        XCTAssertNotEqual(sim.activity, .playBall)
    }

    func testAsksForBallWhenPlayingWithoutOne() {
        let sim = makeSim()
        sim.force(.playBall, duration: 30)
        XCTAssertTrue(sim.wantsBall)
        sim.ball = Ball(center: CGPoint(x: 900, y: 300), radius: 18)
        XCTAssertFalse(sim.wantsBall)
    }

    func testNoticesThrownBallUnlessDisabled() {
        let sim = makeSim()
        sim.ball = Ball(center: CGPoint(x: 900, y: 300), radius: 18)
        sim.noticeBall()
        XCTAssertEqual(sim.activity, .playBall)

        let off = makeSim(allowBall: false)
        off.ball = Ball(center: CGPoint(x: 900, y: 300), radius: 18)
        off.noticeBall()
        XCTAssertNotEqual(off.activity, .playBall)
    }

    func testPlannerSkipsBallPlayWhenDisabled() {
        let p = BehaviorPlanner()
        let spec = p.spec(.playBall)!
        var on = BehaviorContext(energy: 0.9, hour: 14, onWindow: false, canPlayBall: true, last: nil) { _ in true }
        XCTAssertGreaterThan(p.weight(of: spec, in: on, now: 0), 0)
        on.canPlayBall = false
        XCTAssertEqual(p.weight(of: spec, in: on, now: 0), 0)
    }
}

final class BridgeTests: XCTestCase {
    func testDecode() {
        XCTAssertEqual(DistributedNotificationBridge.decode(#"{"type":"expression","pose":"joy","duration":2}"#), .expression(.joy, duration: 2))
        XCTAssertEqual(DistributedNotificationBridge.decode(#"{"type":"notify","title":"a","body":"b"}"#), .notify(title: "a", body: "b"))
        XCTAssertNil(DistributedNotificationBridge.decode(#"{"type":"unknown"}"#))
        XCTAssertNil(DistributedNotificationBridge.decode("not json"))
    }
}

final class ReleaseTests: XCTestCase {
    func testParsesVersions() {
        XCTAssertEqual(AppVersion("0.1.0"), AppVersion(0, 1, 0))
        XCTAssertEqual(AppVersion("v0.2.0"), AppVersion(0, 2, 0))
        XCTAssertEqual(AppVersion("V1.2"), AppVersion(1, 2, 0))
        XCTAssertEqual(AppVersion("1.2.3-beta.1"), AppVersion(1, 2, 3))
        XCTAssertEqual(AppVersion("1.2.3+abc123"), AppVersion(1, 2, 3))
        XCTAssertEqual(AppVersion("1.2.3.0"), AppVersion(1, 2, 3))
        XCTAssertEqual(AppVersion(" 2.10.4\n"), AppVersion(2, 10, 4))
    }

    func testRejectsInvalidVersions() {
        for s in [nil, "", "latest", "1.x.0", "-1.0.0", "1..0"] as [String?] { XCTAssertNil(AppVersion(s), "\(String(describing: s))") }
    }

    func testComparesNumericallyNotAlphabetically() {
        XCTAssertGreaterThan(AppVersion("0.10.0")!, AppVersion("0.9.9")!)
        XCTAssertGreaterThan(AppVersion("1.0.0")!, AppVersion("0.99.99")!)
        XCTAssertGreaterThan(AppVersion("0.2.1")!, AppVersion("0.2.0")!)
        XCTAssertFalse(AppVersion("v0.2.0")! > AppVersion("0.2.0")!)
    }

    private let latestJSON = """
    {
      "tag_name": "v0.2.0", "html_url": "https://github.com/Lis-louis/suzu-desktop-focus/releases/tag/v0.2.0",
      "draft": false, "prerelease": false,
      "assets": [
        { "name": "SuzuSetup-0.2.0-x64.exe", "state": "uploaded", "size": 62000000,
          "browser_download_url": "https://example.invalid/SuzuSetup-0.2.0-x64.exe", "digest": "sha256:AAAA" },
        { "name": "Suzu-0.2.0.pkg", "state": "uploaded", "size": 3000000,
          "browser_download_url": "https://example.invalid/Suzu-0.2.0.pkg", "digest": "sha256:ABCDEF0123" }
      ]
    }
    """

    func testFindsMacInstallerInLatestRelease() throws {
        let r = try XCTUnwrap(ReleaseInfo.fromGitHubJSON(Data(latestJSON.utf8)) { $0.hasPrefix("Suzu-") && $0.hasSuffix(".pkg") })
        XCTAssertEqual(r.version, AppVersion(0, 2, 0))
        XCTAssertEqual(r.assetName, "Suzu-0.2.0.pkg")
        XCTAssertEqual(r.size, 3_000_000)
        XCTAssertEqual(r.sha256, "abcdef0123")
    }

    func testReturnsNilWithoutMatchingAsset() {
        XCTAssertNil(ReleaseInfo.fromGitHubJSON(Data(latestJSON.utf8)) { $0.hasSuffix(".zip") })
    }

    func testIgnoresDraftsAndPrereleases() {
        let draft = latestJSON.replacingOccurrences(of: "\"draft\": false", with: "\"draft\": true")
        let pre = latestJSON.replacingOccurrences(of: "\"prerelease\": false", with: "\"prerelease\": true")
        XCTAssertNil(ReleaseInfo.fromGitHubJSON(Data(draft.utf8)) { _ in true })
        XCTAssertNil(ReleaseInfo.fromGitHubJSON(Data(pre.utf8)) { _ in true })
    }

    func testToleratesMissingDigest() throws {
        let json = latestJSON.replacingOccurrences(of: ", \"digest\": \"sha256:ABCDEF0123\"", with: "")
        let r = try XCTUnwrap(ReleaseInfo.fromGitHubJSON(Data(json.utf8)) { $0.hasSuffix(".pkg") })
        XCTAssertNil(r.sha256)
    }
}
