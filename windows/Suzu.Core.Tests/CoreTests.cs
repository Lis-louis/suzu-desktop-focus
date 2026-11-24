using Suzu.Core;
using Xunit;

namespace Suzu.Core.Tests;

// macOS 版 Tests/SuzuCoreTests/SuzuCoreTests.swift と同じ内容のテスト

public class TerrainTests
{
    private static readonly ScreenInfo Screen = new(1, new RectD(0, 80, 1600, 900));

    [Fact]
    public void ScreenFloorAndWalls()
    {
        var t = Terrain.Build([Screen], [], 200);
        Assert.Single(t.Floors);
        Assert.Equal(80, t.Floors[0].Y);
        Assert.Equal(2, t.Walls.Count);
    }

    [Fact]
    public void WindowTopBecomesFloor()
    {
        var w = new WindowInfo(10, new RectD(200, 200, 600, 400)); // top = 600
        var t = Terrain.Build([Screen], [w], 200);
        var f = t.Floors.Single(x => x.Owner == SurfaceOwner.Window(10));
        Assert.Equal(600, f.Y);
        Assert.Equal(200, f.MinX);
        Assert.Equal(800, f.MaxX);
    }

    [Fact]
    public void WindowTopTooHighIsIgnored()
    {
        var w = new WindowInfo(10, new RectD(200, 200, 600, 700)); // top = 900
        var t = Terrain.Build([Screen], [w], 200);
        Assert.DoesNotContain(t.Floors, x => x.Owner == SurfaceOwner.Window(10));
    }

    [Fact]
    public void OccludedPartIsRemoved()
    {
        var front = new WindowInfo(1, new RectD(400, 300, 200, 400));
        var back = new WindowInfo(2, new RectD(200, 200, 600, 400));
        var t = Terrain.Build([Screen], [front, back], 200);
        var segs = t.Floors.Where(x => x.Owner == SurfaceOwner.Window(2)).ToList();
        Assert.Equal(2, segs.Count);
        Assert.Equal((200.0, 400.0), (segs[0].MinX, segs[0].MaxX));
        Assert.Equal((600.0, 800.0), (segs[1].MinX, segs[1].MaxX));
    }

    [Fact]
    public void FloorBelowPicksHighest()
    {
        var w = new WindowInfo(10, new RectD(200, 200, 600, 400));
        var t = Terrain.Build([Screen], [w], 200);
        Assert.Equal(600, t.FloorBelow(new PointD(300, 700))?.Y);
        Assert.Equal(80, t.FloorBelow(new PointD(100, 700))?.Y);
    }
}

public class SpriteCatalogTests
{
    private static readonly SpriteManifest Manifest = new([
        new(Pose.Stand, Facing.Front, ["front"]),
        new(Pose.Stand, Facing.Right, ["side_right"]),
        new(Pose.Walk, Facing.Right, ["side_right"], bob: true),
    ]);

    [Fact]
    public void ExactMatch()
    {
        var r = new SpriteCatalog(Manifest).Resolve(Pose.Walk, Facing.Right);
        Assert.Equal(new[] { "side_right" }, r.Frames);
        Assert.False(r.Mirrored); Assert.False(r.IsFallback); Assert.True(r.Bob);
    }

    [Fact]
    public void NoMirroringByDefault()
    {
        var r = new SpriteCatalog(Manifest).Resolve(Pose.Walk, Facing.Left);
        Assert.False(r.Mirrored);
        Assert.Equal(new[] { "front" }, r.Frames);
        Assert.True(r.IsFallback);
    }

    [Fact]
    public void MirroringWhenAllowed()
    {
        var r = new SpriteCatalog(Manifest, allowMirroring: true).Resolve(Pose.Walk, Facing.Left);
        Assert.True(r.Mirrored); Assert.Equal(new[] { "side_right" }, r.Frames); Assert.False(r.IsFallback);
    }

    [Fact]
    public void MissingPoseFallsBackToStand()
    {
        var r = new SpriteCatalog(Manifest).Resolve(Pose.Sleep, Facing.Front);
        Assert.Equal(new[] { "front" }, r.Frames); Assert.True(r.IsFallback);
    }

    [Fact]
    public void MissingPosesReport()
    {
        var missing = new SpriteCatalog(Manifest).MissingPoses();
        Assert.Contains((Pose.Sleep, Facing.Front), missing);
        Assert.Contains((Pose.Walk, Facing.Left), missing);
        Assert.DoesNotContain((Pose.Walk, Facing.Right), missing);
    }

    [Fact]
    public void BundledManifestDecodes()
    {
        // テストの出力先から上へたどってリポジトリの Resources/Sprites を探す
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Resources", "Sprites", "manifest.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var sprites = Path.Combine(dir!.FullName, "Resources", "Sprites");
        var m = SpriteManifest.Load(Path.Combine(sprites, "manifest.json"));
        var names = Directory.GetFiles(sprites, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet();
        Assert.Empty(new SpriteCatalog(m).MissingFiles(names));
        Assert.Contains(m.Entries, e => e.Pose == Pose.Walk && e.Bob == true);
    }
}

public class PlannerTests
{
    private static BehaviorContext Ctx(double energy = 0.8, int hour = 14, bool onWindow = false, Activity? last = null, bool all = true) =>
        new(energy, hour, onWindow, last, s => all || s.Pose is Pose.Stand or Pose.Walk);

    [Fact]
    public void UnavailablePosesAreNeverChosen()
    {
        var p = new BehaviorPlanner(); var rng = new SplitMix64(1);
        for (int i = 0; i < 500; i++)
        {
            var (a, _) = p.Choose(Ctx(all: false), i * 1000, rng);
            Assert.DoesNotContain(a, new[] { Activity.Sit, Activity.Sleep, Activity.Climb, Activity.Hang, Activity.Stretch, Activity.Joy });
        }
    }

    [Fact]
    public void TiredAtNightSleepsMore()
    {
        var p = new BehaviorPlanner();
        var sleep = p.Spec(Activity.Sleep)!.Value;
        Assert.Equal(0.0, p.Weight(sleep, Ctx(energy: 1, hour: 14), 0));
        Assert.True(p.Weight(sleep, Ctx(energy: 0.1, hour: 2), 0) > 40);
    }

    [Fact]
    public void PlacementRules()
    {
        var p = new BehaviorPlanner();
        Assert.Equal(0.0, p.Weight(p.Spec(Activity.JumpDown)!.Value, Ctx(onWindow: false), 0));
        Assert.True(p.Weight(p.Spec(Activity.JumpDown)!.Value, Ctx(onWindow: true), 0) > 0);
        Assert.Equal(0.0, p.Weight(p.Spec(Activity.Climb)!.Value, Ctx(onWindow: true), 0));
    }

    [Fact]
    public void CooldownPreventsImmediateRepeat()
    {
        var d = new BehaviorPlanner();
        var p = new BehaviorPlanner([d.Spec(Activity.LookAround)!.Value, d.Spec(Activity.Idle)!.Value]);
        var rng = new SplitMix64(3);
        bool sawLook = false;
        for (int i = 0; i < 50; i++)
        {
            var (a, _) = p.Choose(Ctx(), 0, rng);
            if (a == Activity.LookAround) { Assert.False(sawLook, "cooldown ignored"); sawLook = true; }
        }
    }

    [Fact]
    public void NotPurelyUniform()
    {
        var p = new BehaviorPlanner(); var rng = new SplitMix64(9);
        var counts = new Dictionary<Activity, int>();
        for (int i = 0; i < 3000; i++)
        {
            var a = p.Choose(Ctx(all: false), i * 1000, rng).Activity;
            counts[a] = counts.GetValueOrDefault(a) + 1;
        }
        Assert.True(counts.GetValueOrDefault(Activity.Idle) > counts.GetValueOrDefault(Activity.TurnAway) * 3);
    }
}

public class SimulationTests
{
    private static readonly ScreenInfo Screen = new(1, new RectD(0, 80, 1600, 900));
    private static readonly SpriteManifest Manifest = new([
        new(Pose.Stand, Facing.Front, ["front"]),
        new(Pose.Walk, Facing.Right, ["side_right"], bob: true),
    ]);

    private static MascotSimulation MakeSim(WindowInfo[]? windows = null, bool placeholders = false)
    {
        var c = new MascotSimulation.Config { AllowPlaceholders = placeholders };
        var sim = new MascotSimulation(c, new SpriteCatalog(Manifest), seed: 42) { HourProvider = () => 14 };
        sim.UpdateTerrain(Terrain.Build([Screen], windows ?? [], 200));
        return sim;
    }

    private static void Run(MascotSimulation sim, double seconds)
    {
        for (int i = 0; i < (int)(seconds * 30); i++) sim.Step(1.0 / 30);
    }

    [Fact]
    public void FallsAndLandsOnScreenFloor()
    {
        var sim = MakeSim();
        sim.Place(new PointD(500, 700));
        Run(sim, 2);
        Assert.Equal(80, sim.Position.Y, 2);
        var g = Assert.IsType<Motion.Grounded>(sim.Motion);
        Assert.Equal(SurfaceOwner.Screen(1), g.Floor.Owner);
    }

    [Fact]
    public void LandsOnWindowTop()
    {
        var sim = MakeSim([new WindowInfo(7, new RectD(300, 200, 600, 400))]);
        sim.Place(new PointD(500, 900));
        Run(sim, 2);
        Assert.Equal(600, sim.Position.Y, 2);
    }

    [Fact]
    public void FollowsMovingWindowAndFallsWhenClosed()
    {
        var w = new WindowInfo(7, new RectD(300, 200, 600, 400));
        var sim = MakeSim([w]);
        sim.Cfg.Paused = true;
        sim.Place(new PointD(500, 600));
        var moved = w with { Frame = new RectD(350, 150, 600, 400) };
        sim.UpdateTerrain(Terrain.Build([Screen], [moved], 200));
        Assert.Equal(550, sim.Position.X, 2);
        Assert.Equal(550, sim.Position.Y, 2);
        sim.UpdateTerrain(Terrain.Build([Screen], [], 200));
        Assert.Equal(Activity.Falling, sim.Activity);
        Run(sim, 2);
        Assert.Equal(80, sim.Position.Y, 2);
    }

    [Fact]
    public void StaysInsideScreensForLongRun()
    {
        var sim = MakeSim([new WindowInfo(7, new RectD(300, 200, 600, 400))], placeholders: true);
        sim.Place(new PointD(800, 80));
        var bounds = Screen.VisibleFrame.Inset(-1, -200);
        for (int i = 0; i < 30 * 60 * 30; i++) // 30 分ぶん
        {
            sim.Step(1.0 / 30);
            Assert.True(bounds.Contains(sim.Position), $"{sim.Position} {sim.Activity}");
        }
    }

    [Fact]
    public void DragAndThrow()
    {
        var sim = MakeSim();
        sim.Place(new PointD(500, 80));
        sim.BeginDrag();
        sim.DragTo(new PointD(900, 600));
        Assert.Equal(Pose.Dragged, sim.GetSnapshot().Pose);
        sim.EndDrag(new PointD(300, 0));
        Run(sim, 3);
        Assert.Equal(80, sim.Position.Y, 2);
        Assert.True(sim.Position.X > 900);
    }

    [Fact]
    public void JumpDownFromWindow()
    {
        var sim = MakeSim([new WindowInfo(7, new RectD(300, 200, 600, 400))]);
        sim.Place(new PointD(500, 600));
        sim.Force(Activity.JumpDown);
        Run(sim, 15);
        Assert.Equal(80, sim.Position.Y, 2);
    }

    [Fact]
    public void ClickWakesFromDoze()
    {
        var sim = MakeSim();
        sim.Place(new PointD(500, 80));
        sim.Force(Activity.Doze);
        Assert.Equal(Emote.Sleepy, sim.GetSnapshot().Emote);
        sim.Poke();
        Assert.Equal(Activity.React, sim.Activity);
        Assert.Equal(Emote.Surprise, sim.GetSnapshot().Emote);
    }

    [Fact]
    public void ClimbAndHangWithPlaceholders()
    {
        var sim = MakeSim([new WindowInfo(7, new RectD(300, 200, 600, 400))], placeholders: true);
        sim.Place(new PointD(100, 80));
        sim.Force(Activity.Climb);
        Run(sim, 6);
        Assert.IsType<Motion.Climbing>(sim.Motion);
        sim.Place(new PointD(500, 600));
        sim.Force(Activity.Hang);
        Run(sim, 6);
        Assert.IsType<Motion.Hanging>(sim.Motion);
    }

    [Fact]
    public void SpeedScaleKeepsBehaviorConsistent()
    {
        // 200% 表示（物理ピクセルで 2 倍）でも同じように着地する
        var screen = new ScreenInfo(1, new RectD(0, 160, 3200, 1800));
        var c = new MascotSimulation.Config { BodyHeight = 360, BodyWidth = 140, SpeedScale = 2 };
        var sim = new MascotSimulation(c, new SpriteCatalog(Manifest), seed: 42) { HourProvider = () => 14 };
        sim.UpdateTerrain(Terrain.Build([screen], [], 400));
        sim.Place(new PointD(1000, 1400));
        Run(sim, 2);
        Assert.Equal(160, sim.Position.Y, 2);
    }
}

public class MultiScreenTests
{
    // 左：タスクバーありの画面（床 80）、右：もう 1 枚のモニタ（床の高さを変えて試す）
    private static readonly ScreenInfo Left = new(1, new RectD(0, 80, 1600, 900), new RectD(0, 0, 1600, 1000));
    private static readonly SpriteManifest Manifest = new([
        new(Pose.Stand, Facing.Front, ["front"]),
        new(Pose.Walk, Facing.Right, ["side_right"], bob: true),
    ]);

    private static ScreenInfo Right(double floorY) => new(2, new RectD(1600, floorY, 1920, 1000), new RectD(1600, floorY, 1920, 1025));

    private static MascotSimulation MakeSim(params ScreenInfo[] screens)
    {
        var sim = new MascotSimulation(new MascotSimulation.Config(), new SpriteCatalog(Manifest), seed: 7) { HourProvider = () => 14 };
        sim.UpdateTerrain(Terrain.Build(screens, [], 200));
        return sim;
    }

    [Fact]
    public void SharedEdgeIsNotAWall()
    {
        var t = Terrain.Build([Left, Right(0)], [], 200);
        Assert.Equal(2, t.Walls.Count);
        Assert.Equal(2u, t.AdjacentScreen(Left, WallSide.Right)?.Id);
        Assert.Null(t.AdjacentScreen(Left, WallSide.Left));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(80)]
    public void CrossesToNeighborScreen(double floorY)
    {
        var sim = MakeSim(Left, Right(floorY));
        sim.Cfg.Paused = true;
        sim.Place(new PointD(1200, 80));
        sim.Force(Activity.CrossScreen);
        Assert.Equal(Activity.CrossScreen, sim.Activity);
        for (int i = 0; i < 30 * 40; i++)
        {
            sim.Step(1.0 / 30);
            if (sim.Position.X > 1600 && sim.Motion is Motion.Grounded) break;
        }
        var g = Assert.IsType<Motion.Grounded>(sim.Motion);
        Assert.Equal(SurfaceOwner.Screen(2), g.Floor.Owner);
        Assert.Equal(floorY, sim.Position.Y, 2);
    }

    [Fact]
    public void EventuallyVisitsOtherScreenOnItsOwn()
    {
        var sim = MakeSim(Left, Right(0));
        sim.Place(new PointD(800, 80));
        bool visited = false;
        for (int i = 0; i < 30 * 60 * 30 && !visited; i++) { sim.Step(1.0 / 30); visited = sim.Position.X > 1600; }
        Assert.True(visited);
    }
}

public class BridgeTests
{
    [Fact]
    public void Decode()
    {
        Assert.Equal(new SecretaryEvent.Expression(Pose.Joy, 2), SecretaryJson.Decode("""{"type":"expression","pose":"joy","duration":2}"""));
        Assert.Equal(new SecretaryEvent.Notify("a", "b"), SecretaryJson.Decode("""{"type":"notify","title":"a","body":"b"}"""));
        Assert.Equal(new SecretaryEvent.ActivityChanged(ActivityHint.UserFocused), SecretaryJson.Decode("""{"type":"activity","hint":"userFocused"}"""));
        Assert.Null(SecretaryJson.Decode("""{"type":"unknown"}"""));
        Assert.Null(SecretaryJson.Decode("not json"));
    }

    [Fact]
    public void Encode()
    {
        Assert.Equal("""{"type":"click"}""", SecretaryJson.Encode(new MascotEvent(MascotEventKind.UserClicked)));
        Assert.Contains("\"message\"", SecretaryJson.Encode(new MascotEvent(MascotEventKind.UserMessage, "hi")));
    }
}

public class ReleaseTests
{
    [Theory]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("V1.2", 1, 2, 0)]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]
    [InlineData("1.2.3+abc123", 1, 2, 3)]
    [InlineData("1.2.3.0", 1, 2, 3)]
    [InlineData(" 2.10.4\n", 2, 10, 4)]
    public void ParsesVersions(string text, int major, int minor, int patch)
    {
        Assert.Equal(new AppVersion(major, minor, patch), AppVersion.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.x.0")]
    [InlineData("-1.0.0")]
    public void RejectsInvalidVersions(string? text) => Assert.Null(AppVersion.Parse(text));

    [Fact]
    public void ComparesNumericallyNotAlphabetically()
    {
        Assert.True(AppVersion.Parse("0.10.0") > AppVersion.Parse("0.9.9"));
        Assert.True(AppVersion.Parse("1.0.0") > AppVersion.Parse("0.99.99"));
        Assert.True(AppVersion.Parse("0.2.1") > AppVersion.Parse("0.2.0"));
        Assert.False(AppVersion.Parse("v0.2.0") > AppVersion.Parse("0.2.0"));
    }

    private const string LatestJson = """
    {
      "tag_name": "v0.2.0", "html_url": "https://github.com/Lis-louis/suzu-desktop-focus/releases/tag/v0.2.0",
      "draft": false, "prerelease": false,
      "assets": [
        { "name": "Suzu-0.2.0.pkg", "state": "uploaded", "size": 3000000,
          "browser_download_url": "https://example.invalid/Suzu-0.2.0.pkg", "digest": "sha256:AAAA" },
        { "name": "SuzuSetup-0.2.0-x64.exe", "state": "uploaded", "size": 62000000,
          "browser_download_url": "https://example.invalid/SuzuSetup-0.2.0-x64.exe", "digest": "sha256:ABCDEF0123" }
      ]
    }
    """;

    [Fact]
    public void FindsWindowsInstallerInLatestRelease()
    {
        var r = ReleaseInfo.FromGitHubJson(LatestJson, n => n.StartsWith("SuzuSetup-") && n.EndsWith("-x64.exe"));
        Assert.NotNull(r);
        Assert.Equal(new AppVersion(0, 2, 0), r!.Version);
        Assert.Equal("SuzuSetup-0.2.0-x64.exe", r.AssetName);
        Assert.Equal(62000000, r.Size);
        Assert.Equal("abcdef0123", r.Sha256);
    }

    [Fact]
    public void ReturnsNullWithoutMatchingAsset()
    {
        Assert.Null(ReleaseInfo.FromGitHubJson(LatestJson, n => n.EndsWith(".zip")));
    }

    [Fact]
    public void IgnoresDraftsAndPrereleases()
    {
        Assert.Null(ReleaseInfo.FromGitHubJson(LatestJson.Replace("\"draft\": false", "\"draft\": true"), _ => true));
        Assert.Null(ReleaseInfo.FromGitHubJson(LatestJson.Replace("\"prerelease\": false", "\"prerelease\": true"), _ => true));
    }

    [Fact]
    public void ToleratesMissingDigest()
    {
        var json = LatestJson.Replace(", \"digest\": \"sha256:ABCDEF0123\"", "");
        var r = ReleaseInfo.FromGitHubJson(json, n => n.EndsWith(".exe"));
        Assert.NotNull(r);
        Assert.Null(r!.Sha256);
    }
}
