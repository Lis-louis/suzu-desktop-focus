namespace Suzu.Core;

/// <summary>鈴の物理的な状態</summary>
public abstract record Motion
{
    public sealed record Grounded(Floor Floor) : Motion;
    public sealed record Falling(PointD Velocity) : Motion;
    public sealed record Dragged : Motion { public static readonly Dragged Instance = new(); }
    public sealed record Climbing(Wall Wall, double TargetY) : Motion;
    public sealed record Hanging(Floor Floor, WallSide Side) : Motion;
}

/// <summary>
/// 鈴の状態（行動・物理・表情）を進める中核。Windows API に依存せず、テストから直接動かせる。
/// Position は「両足の中心」を y 上向きのグローバル座標で表す。
/// （macOS 版 Sources/SuzuCore/MascotSimulation.swift の移植）
/// </summary>
public sealed class MascotSimulation
{
    public sealed class Config
    {
        public double WalkSpeed { get; set; } = 42;
        public double ClimbSpeed { get; set; } = 28;
        public double Gravity { get; set; } = 2200;
        public double MaxFallSpeed { get; set; } = 1800;
        public double BodyHeight { get; set; } = 180;
        public double BodyWidth { get; set; } = 70;
        /// <summary>素材がない行動も立ち姿＋吹き出しで試す（開発用）</summary>
        public bool AllowPlaceholders { get; set; }
        /// <summary>自律行動を止める</summary>
        public bool Paused { get; set; }
        /// <summary>
        /// 速度・重力などの倍率。Windows 版は物理ピクセルで計算するため、
        /// 表示倍率（DPI）に合わせて動きの速さをそろえるのに使う。
        /// </summary>
        public double SpeedScale { get; set; } = 1;
        /// <summary>ボール遊びをする（ときどき自分でボールを出して遊ぶ）</summary>
        public bool AllowBallPlay { get; set; } = true;
    }

    public readonly record struct Snapshot(PointD Position, Pose Pose, Facing Facing, Emote Emote, Activity Activity, double BobOffset);

    public Config Cfg { get; }
    public SpriteCatalog Catalog { get; }
    public PointD Position { get; private set; } = PointD.Zero;
    public Motion Motion { get; private set; } = new Motion.Falling(PointD.Zero);
    public Activity Activity { get; private set; } = Activity.Idle;
    public Facing Facing { get; private set; } = Facing.Front;
    public double Energy { get; private set; } = 0.8;
    public Terrain Terrain { get; private set; } = Terrain.Empty;
    public Func<int> HourProvider { get; set; } = () => DateTime.Now.Hour;
    /// <summary>今出ているボール（App 層が出し入れする）。Step で一緒に動かす。</summary>
    public Ball? Ball { get; set; }
    /// <summary>ボール遊びを始めたがボールがない（App 層がボールを出す合図）</summary>
    public bool WantsBall => Activity == Activity.PlayBall && Ball == null && Motion is Motion.Grounded;
    /// <summary>これまでに蹴った回数（表示・テスト用）</summary>
    public int Kicks { get; private set; }

    private readonly BehaviorPlanner _planner = new();
    private readonly SplitMix64 _rng;
    private double _clock;
    private double _timeLeft = 2;
    private Activity? _lastActivity;
    private double? _targetX;
    private Pose _reactionPose = Pose.Surprise;
    private Emote _reactionEmote = Emote.Surprise;
    private double _lookTimer;
    private double _walkDistance;
    private PointD? _mouse;
    private double _hoverTime;
    private (Emote Emote, double Seconds)? _transientEmote;
    private double _kickCooldown;
    /// <summary>跳び乗る先の窓と着地点</summary>
    private (Floor Floor, double LandX)? _jumpTarget;
    private bool _running;

    public MascotSimulation(Config config, SpriteCatalog catalog, ulong? seed = null)
    {
        Cfg = config;
        Catalog = catalog;
        _rng = new SplitMix64(seed ?? (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private double S => Cfg.SpeedScale;

    // ---- 外部入力 ----

    public void Place(PointD p)
    {
        Position = p;
        if (Terrain.FloorBelow(p) is { } f && Math.Abs(f.Y - p.Y) < 2)
        {
            Position = p with { Y = f.Y };
            Motion = new Motion.Grounded(f);
        }
        else
        {
            Motion = new Motion.Falling(PointD.Zero);
            Activity = Activity.Falling;
        }
    }

    public void UpdateTerrain(Terrain next)
    {
        var old = Terrain;
        Terrain = next;
        switch (Motion)
        {
            case Motion.Grounded g:
                if (Follow(g.Floor, old) is { } nf) Motion = new Motion.Grounded(nf);
                else StartFalling(PointD.Zero);
                break;
            case Motion.Hanging h:
                if (Follow(h.Floor, old) is { } hf)
                {
                    Position = new PointD(h.Side == WallSide.Left ? hf.MinX : hf.MaxX, hf.Y - Cfg.BodyHeight * 0.85);
                    Motion = new Motion.Hanging(hf, h.Side);
                }
                else StartFalling(PointD.Zero);
                break;
            case Motion.Climbing c:
                var nw = next.Walls.Where(w => w.Owner == c.Wall.Owner && w.Side == c.Wall.Side).Select(w => (Wall?)w).FirstOrDefault();
                if (nw is { } wall)
                {
                    Position = Position with { X = wall.X + (c.Wall.Side == WallSide.Left ? Cfg.BodyWidth / 2 : -Cfg.BodyWidth / 2) };
                    Motion = new Motion.Climbing(wall, Math.Min(c.TargetY, wall.MaxY - Cfg.BodyHeight));
                }
                else StartFalling(PointD.Zero);
                break;
        }
        // 落下中（隣の画面へ跳び移っている途中を含む）は着地か Step 側の救済に任せる
        if (Terrain.Screens.Count > 0 && Terrain.ScreenContaining(Position) == null && Motion is not (Motion.Dragged or Motion.Falling))
            Rescue();
    }

    /// <summary>足場が動いた・消えたときの追従。見つからなければ null（＝落下）。</summary>
    private Floor? Follow(Floor f, Terrain old)
    {
        if (!f.Owner.IsWindow)
        {
            var found = Terrain.Floors.Where(x => x.Owner == f.Owner).Select(x => (Floor?)x).FirstOrDefault();
            if (found is not { } nf) return null;
            Position = new PointD(Math.Clamp(Position.X, nf.MinX, nf.MaxX), nf.Y);
            return nf;
        }
        uint id = f.Owner.Id;
        if (!old.WindowFrames.TryGetValue(id, out var oldFrame) || !Terrain.WindowFrames.TryGetValue(id, out var newFrame))
        {
            // 初回など旧情報がない場合は同じ高さで探す
            return Terrain.FloorAt(Position.X, Position.Y, 2);
        }
        double dx = newFrame.MinX - oldFrame.MinX;
        double nx = Position.X + dx;
        var moved = Terrain.Floors.Where(x => x.Owner == f.Owner && x.Contains(nx)).Select(x => (Floor?)x).FirstOrDefault();
        if (moved is not { } mf) return null;
        Position = Motion is Motion.Hanging ? Position with { X = nx } : new PointD(nx, mf.Y);
        if (_targetX is { } tx) _targetX = tx + dx;
        return mf;
    }

    public void ObserveMouse(PointD? p) => _mouse = p;

    /// <summary>クリック</summary>
    public void Poke()
    {
        if (Activity is Activity.Dragged or Activity.Falling) return;
        if (Activity is Activity.Sleep or Activity.Doze)
        {
            React(Pose.Surprise, Emote.Surprise, 1.6);
            Energy = Math.Min(1, Energy + 0.15);
            return;
        }
        (Pose, Emote)[] options = [(Pose.Joy, Emote.Joy), (Pose.Surprise, Emote.Surprise), (Pose.Joy, Emote.Heart), (Pose.Troubled, Emote.Question)];
        var o = options[_rng.NextInt(options.Length)];
        React(o.Item1, o.Item2, 1.8);
    }

    public void React(Pose pose, Emote emote, double duration)
    {
        if (Motion is not Motion.Grounded) { ShowEmote(emote, duration); return; }
        _reactionPose = pose; _reactionEmote = emote;
        SetActivity(Activity.React, duration);
        Facing = Facing.Front;
    }

    public void ShowEmote(Emote e, double seconds) => _transientEmote = (e, seconds);

    /// <summary>ボールが投げられた・出てきたことに気付いて、遊び始める</summary>
    public void NoticeBall()
    {
        if (!Cfg.AllowBallPlay || Cfg.Paused || Ball == null || Motion is not Motion.Grounded) return;
        if (Activity is Activity.Sleep or Activity.PlayBall or Activity.Dragged or Activity.Falling) return;
        SetActivity(Activity.PlayBall, _rng.Range(30, 60));
        _kickCooldown = 0.4;
        ShowEmote(Emote.Joy, 1.5);
    }

    public void BeginDrag()
    {
        Motion = Motion.Dragged.Instance;
        Activity = Activity.Dragged;
        Facing = Facing.Front;
    }

    public void DragTo(PointD p)
    {
        if (Motion is not Motion.Dragged) return;
        Position = p;
    }

    public void EndDrag(PointD velocity)
    {
        if (Motion is not Motion.Dragged) return;
        double lim = 900 * S;
        StartFalling(new PointD(Math.Clamp(velocity.X, -lim, lim), Math.Clamp(velocity.Y, -lim, lim)));
    }

    // ---- 時間発展 ----

    public void Step(double dt)
    {
        dt = Math.Min(dt, 0.1);
        _clock += dt;
        if (_transientEmote is { } te) _transientEmote = te.Seconds - dt > 0 ? (te.Emote, te.Seconds - dt) : null;
        UpdateEnergy(dt);
        Ball?.Step(dt, Terrain, Cfg.Gravity, S);

        switch (Motion)
        {
            case Motion.Dragged:
                return;
            case Motion.Falling fall:
            {
                var v = fall.Velocity;
                double vy = Math.Max(v.Y - Cfg.Gravity * S * dt, -Cfg.MaxFallSpeed * S);
                double vx = v.X * Math.Pow(0.6, dt);
                var prev = Position;
                Position = new PointD(Position.X + vx * dt, Position.Y + vy * dt);
                ClampToScreensHorizontally();
                if (vy <= 0 && Landing(prev, Position) is { } f)
                {
                    Position = Position with { Y = f.Y };
                    Motion = new Motion.Grounded(f);
                    bool hard = vy < -1200 * S;
                    if (hard) React(Pose.Surprise, Emote.Surprise, 1.2); else SetActivity(Activity.Idle, 1.5);
                }
                else
                {
                    Motion = new Motion.Falling(new PointD(vx, vy));
                    if (Terrain.Floors.Count > 0 && Position.Y < Terrain.Floors.Min(x => x.Y) - 400 * S) Rescue();
                }
                return;
            }
            case Motion.Climbing c:
                if (Position.Y < c.TargetY)
                {
                    Position = Position with { Y = Math.Min(c.TargetY, Position.Y + Cfg.ClimbSpeed * S * dt) };
                    _walkDistance += Cfg.ClimbSpeed * dt;
                }
                _timeLeft -= dt;
                if (_timeLeft <= 0) StartFalling(new PointD((c.Wall.Side == WallSide.Left ? 120 : -120) * S, 0));
                return;
            case Motion.Hanging h:
                _timeLeft -= dt;
                if (_timeLeft <= 0)
                {
                    if (_rng.NextBool())
                    {
                        // よじ登って戻る
                        Position = new PointD(h.Side == WallSide.Left ? h.Floor.MinX + Cfg.BodyWidth / 2 : h.Floor.MaxX - Cfg.BodyWidth / 2, h.Floor.Y);
                        Motion = new Motion.Grounded(h.Floor);
                        SetActivity(Activity.Idle, 2);
                    }
                    else StartFalling(PointD.Zero);
                }
                return;
            case Motion.Grounded g:
                StepGrounded(g.Floor, dt);
                return;
        }
    }

    private void StepGrounded(Floor floor, double dt)
    {
        if (Activity is Activity.Falling or Activity.Dragged) SetActivity(Activity.Idle, 1);
        _timeLeft -= dt;

        switch (Activity)
        {
            case Activity.Walk or Activity.GoToEdge or Activity.JumpDown or Activity.Climb or Activity.Hang or Activity.CrossScreen or Activity.JumpUp:
            {
                if (_targetX is not { } tx) { FinishActivity(); return; }
                double dir = tx > Position.X ? 1 : -1;
                Facing = dir > 0 ? Facing.Right : Facing.Left;
                double stepLen = Cfg.WalkSpeed * S * dt;
                double nx = Position.X + dir * stepLen;
                _walkDistance += Cfg.WalkSpeed * dt;
                // 床の乗り継ぎ（隣接ディスプレイ・同じ高さの足場）
                if (!floor.Contains(nx) && Activity != Activity.JumpDown)
                {
                    if (Terrain.FloorAt(nx, floor.Y) is { } next) Motion = new Motion.Grounded(next);
                    else if (Activity == Activity.CrossScreen && HopToNeighborScreen(floor, dir)) return;
                    else { Position = Position with { X = Math.Clamp(nx, floor.MinX, floor.MaxX) }; Arrive(); return; }
                }
                if (Activity == Activity.JumpDown && !floor.Contains(nx))
                {
                    Position = Position with { X = nx };
                    StartFalling(new PointD(dir * 140 * S, 260 * S));
                    return;
                }
                Position = Position with { X = nx };
                if (Math.Abs(tx - Position.X) <= stepLen) { Position = Position with { X = tx }; Arrive(); }
                // 散歩だけは時間切れで途中終了（目的地へ瞬間移動はしない）
                else if (Activity == Activity.Walk && _timeLeft <= 0) FinishActivity();
                break;
            }
            case Activity.PlayBall:
                StepPlayBall(floor, dt);
                break;
            case Activity.LookAround:
                _lookTimer -= dt;
                if (_lookTimer <= 0)
                {
                    var options = new[] { Facing.Front, Facing.Right, Facing.Left, Facing.Back }
                        .Where(f => Catalog.Has(Pose.Stand, f) && f != Facing).ToArray();
                    if (options.Length > 0) Facing = options[_rng.NextInt(options.Length)];
                    _lookTimer = _rng.Range(1.0, 2.2);
                }
                if (_timeLeft <= 0) FinishActivity();
                break;
            default:
                if (Activity == Activity.Idle) TrackMouse(dt);
                if (_timeLeft <= 0) FinishActivity();
                break;
        }
    }

    /// <summary>ボールを追いかけ、届いたら蹴る。時間が来たら、ボールが止まったところで終わる。</summary>
    private void StepPlayBall(Floor floor, double dt)
    {
        _running = false;
        _kickCooldown -= dt;
        bool timeUp = _timeLeft <= 0;
        if (Ball is not { } b)
        {
            // App 層がボールを出すのを待つ（出てこなければ時間で終わる）
            if (timeUp) FinishActivity();
            return;
        }
        double dx = b.Position.X - Position.X;
        var toward = Math.Abs(dx) < Cfg.BodyWidth * 0.3 ? Facing.Front : (dx > 0 ? Facing.Right : Facing.Left);
        if (b.Held)
        {
            // つかまれている間は見上げて待つ
            Facing = Catalog.Has(Pose.Stand, toward) ? toward : Facing.Front;
            if (timeUp) FinishActivity();
            return;
        }
        double bottom = b.Position.Y - b.Radius;
        bool sameLevel = bottom >= Position.Y - 20 * S && bottom <= Position.Y + Cfg.BodyHeight * 0.5;
        double reach = Cfg.BodyWidth * 0.45 + b.Radius;
        var (lo, hi) = ReachableRange(floor);
        double margin = Cfg.BodyWidth / 2;
        double target = Math.Clamp(b.Position.X, lo + margin, Math.Max(lo + margin, hi - margin));

        if (!timeUp && sameLevel && Math.Abs(dx) <= reach && _kickCooldown <= 0)
        {
            KickBall(b, lo, hi);
        }
        else if (!timeUp && Math.Abs(target - Position.X) > reach * 0.6)
        {
            // 小走りで追いかける
            double dir = target > Position.X ? 1 : -1;
            Facing = dir > 0 ? Facing.Right : Facing.Left;
            double stepLen = Math.Min(Cfg.WalkSpeed * 1.8 * S * dt, Math.Abs(target - Position.X));
            double nx = Position.X + dir * stepLen;
            _walkDistance += stepLen / S;
            if (!floor.Contains(nx))
            {
                if (Terrain.FloorAt(nx, floor.Y) is { } next) Motion = new Motion.Grounded(next);
                else nx = Math.Clamp(nx, floor.MinX, floor.MaxX);
            }
            Position = Position with { X = nx };
            _running = true;
        }
        else
        {
            Facing = Catalog.Has(Pose.Stand, toward) ? toward : Facing.Front;
        }
        // 時間が来たら、ボールが止まるのを見届けて終わる（弾み続けても 10 秒で切り上げる）
        if (timeUp && (b.IsResting(S) || _timeLeft < -10)) FinishActivity();
    }

    private void KickBall(Ball b, double lo, double hi)
    {
        // 広い方へ蹴ることが多い（壁際で蹴り続けないように）
        double dir = _rng.NextDouble() < 0.7 ? (Position.X < (lo + hi) / 2 ? 1 : -1) : (_rng.NextBool() ? 1 : -1);
        b.Kick(new PointD(dir * _rng.Range(220, 560) * S, _rng.Range(380, 820) * S));
        Facing = dir > 0 ? Facing.Right : Facing.Left;
        _kickCooldown = 0.8;
        Kicks++;
        if (_rng.NextDouble() < 0.35) ShowEmote(_rng.NextBool() ? Emote.Joy : Emote.Heart, 1.2);
    }

    /// <summary>歩行の目的地に到着</summary>
    private void Arrive()
    {
        if (Motion is not Motion.Grounded { Floor: var f }) return;
        switch (Activity)
        {
            case Activity.Climb:
            {
                double edge = Position.X < (f.MinX + f.MaxX) / 2 ? f.MinX : f.MaxX;
                var found = Terrain.Walls.Where(w => Math.Abs(w.X - edge) < 1 && w.MinY <= f.Y + 1).Select(w => (Wall?)w).FirstOrDefault();
                if (found is not { } wall) { FinishActivity(); return; }
                double top = wall.MaxY - Cfg.BodyHeight;
                double ty = f.Y + _rng.Range(0.3, 0.8) * Math.Max(0, top - f.Y);
                Facing = wall.Side == WallSide.Left ? Facing.Left : Facing.Right;
                Motion = new Motion.Climbing(wall, ty);
                _timeLeft = Math.Max(0, ty - f.Y) / (Cfg.ClimbSpeed * S) + _rng.Range(2, 6);
                break;
            }
            case Activity.Hang:
            {
                if (!f.Owner.IsWindow) { FinishActivity(); return; }
                var side = Position.X - f.MinX < f.MaxX - Position.X ? WallSide.Left : WallSide.Right;
                Position = new PointD(side == WallSide.Left ? f.MinX : f.MaxX, f.Y - Cfg.BodyHeight * 0.85);
                Motion = new Motion.Hanging(f, side);
                Facing = Facing.Front;
                break;
            }
            case Activity.JumpUp:
                JumpUpOntoWindow(f);
                break;
            default:
                FinishActivity();
                break;
        }
    }

    /// <summary>高さの違う隣の画面の床へ跳び移る（下なら飛び降り、上ならジャンプ）。隣がなければ false。</summary>
    private bool HopToNeighborScreen(Floor floor, double dir)
    {
        if (floor.Owner.IsWindow || Terrain.ScreenById(floor.Owner.Id) is not { } s) return false;
        if (Terrain.AdjacentScreen(s, dir > 0 ? WallSide.Right : WallSide.Left) is not { } n) return false;
        if (Terrain.ScreenFloor(n) is not { } nf || nf.Width <= Cfg.BodyWidth * 2) return false;
        double inset = Cfg.BodyWidth / 2 + 40 * S;
        double landX = dir > 0 ? nf.MinX + inset : nf.MaxX - inset;
        Jump(floor, nf.Y, landX);
        Facing = dir > 0 ? Facing.Right : Facing.Left;
        return true;
    }

    /// <summary>跳び乗れる窓（今の床より上にあり、歩いて真下まで行けるもの）</summary>
    private List<(Floor Floor, double Lo, double Hi)> JumpUpCandidates(Floor f)
    {
        var (lo0, hi0) = ReachableRange(f);
        double margin = Cfg.BodyWidth / 2;
        var list = new List<(Floor, double, double)>();
        foreach (var o in Terrain.Floors)
        {
            if (!o.Owner.IsWindow || o.Y <= f.Y + 20 * S || o.Y - f.Y > Cfg.BodyHeight * 6 || o.Width < Cfg.BodyWidth * 1.5) continue;
            double lo = Math.Max(o.MinX + margin, lo0 + margin), hi = Math.Min(o.MaxX - margin, hi0 - margin);
            if (lo < hi) list.Add((o, lo, hi));
        }
        return list;
    }

    /// <summary>窓へ跳び乗る（Arrive から）。窓が動いた・消えたときは跳ばずに終わる。</summary>
    private void JumpUpOntoWindow(Floor f)
    {
        if (_jumpTarget is not { } t) { FinishActivity(); return; }
        _jumpTarget = null;
        var dest = Terrain.Floors.Where(x => x.Owner == t.Floor.Owner && x.Contains(t.LandX) && x.Y > f.Y + 20 * S)
                                 .Select(x => (Floor?)x).FirstOrDefault();
        if (dest is not { } d) { FinishActivity(); return; }
        Facing = t.LandX >= Position.X ? Facing.Right : Facing.Left;
        Jump(f, d.Y, t.LandX);
    }

    /// <summary>床から跳んで、高さ toY の床の landX に降りる</summary>
    private void Jump(Floor floor, double toY, double landX)
    {
        double g = Cfg.Gravity * S;
        double rise = toY - floor.Y;
        double peak = Math.Max(rise, 0) + 40 * S;   // 床より少し高く跳んでから降りる
        // 画面の上端より上へは行けない（頭がつかえる）ので、跳ぶ高さを抑える
        if (Terrain.ScreenInColumn(Position) is { } s)
            peak = Math.Min(peak, Math.Max(rise + 5 * S, s.VisibleFrame.MaxY - Cfg.BodyHeight - floor.Y));
        // 1 コマずつ進める計算では頂点が理論より少し低くなる（30fps で約 g/60 ぶん）ので、初速を少し足す
        double vy = Math.Sqrt(2 * g * peak) + g / 60;
        double flight = vy / g + Math.Sqrt(2 * (peak - rise) / g);
        // 空中では横速度が 0.6^t で減衰するので、その分を見込んで初速を決める
        double decay = 1 - Math.Pow(0.6, flight);
        double vx = (landX - Position.X) * -Math.Log(0.6) / Math.Max(decay, 0.05);
        StartFalling(new PointD(vx, vy));
    }

    /// <summary>スクリーンの床にいるとき、隣の画面がつながっている側</summary>
    private List<WallSide> NeighborSides(Floor f)
    {
        var sides = new List<WallSide>();
        if (f.Owner.IsWindow || Terrain.ScreenById(f.Owner.Id) is not { } s) return sides;
        foreach (var side in new[] { WallSide.Left, WallSide.Right })
            if (Terrain.AdjacentScreen(s, side) is { } n && Terrain.ScreenFloor(n) is { } nf && nf.Width > Cfg.BodyWidth * 2)
                sides.Add(side);
        return sides;
    }

    private void TrackMouse(double dt)
    {
        if (_mouse is not { } m) { _hoverTime = 0; return; }
        double dx = m.X - Position.X, dy = m.Y - Position.Y;
        bool near = Math.Abs(dx) < 260 * S && dy > -40 * S && dy < Cfg.BodyHeight + 160 * S;
        if (!near) { _hoverTime = 0; return; }
        // 鈴の上にカーソルが留まっていたら気付く
        if (Math.Abs(dx) < Cfg.BodyWidth / 2 && dy > 0 && dy < Cfg.BodyHeight)
        {
            _hoverTime += dt;
            if (_hoverTime > 1.5 && _transientEmote == null) { ShowEmote(Energy > 0.4 ? Emote.Heart : Emote.Question, 2); _hoverTime = -6; }
        }
        var want = Math.Abs(dx) < Cfg.BodyWidth ? Facing.Front : (dx > 0 ? Facing.Right : Facing.Left);
        if (Catalog.Has(Pose.Stand, want)) Facing = want;
    }

    private void FinishActivity()
    {
        _lastActivity = Activity;
        // ボール遊びは（投げてもらって始めた場合も）終わったらしばらく休む
        if (Activity == Activity.PlayBall && _planner.Spec(Activity.PlayBall) is { } ps)
            _planner.CooldownUntil[Activity.PlayBall] = _clock + ps.Cooldown;
        if (Cfg.Paused) { SetActivity(Activity.Idle, 3); return; }
        if (Motion is not Motion.Grounded { Floor: var f }) return;
        var ctx = new BehaviorContext(Energy, HourProvider(), f.Owner.IsWindow, _lastActivity,
            s => Cfg.AllowPlaceholders || Catalog.Has(s.Pose), NeighborSides(f).Count > 0, Cfg.AllowBallPlay,
            JumpUpCandidates(f).Count > 0);
        var (a, d) = _planner.Choose(ctx, _clock, _rng);
        SetActivity(a, d);
        Prepare(a, f);
    }

    private void Prepare(Activity a, Floor f)
    {
        var (lo0, hi0) = ReachableRange(f);
        double margin = Cfg.BodyWidth / 2;
        switch (a)
        {
            case Activity.Walk:
            {
                double maxDist = _rng.Range(80, 420) * S;
                double lo = Math.Max(lo0 + margin, Position.X - maxDist);
                double hi = Math.Min(hi0 - margin, Position.X + maxDist);
                _targetX = lo < hi ? _rng.Range(lo, hi) : null;
                break;
            }
            case Activity.GoToEdge:
                _targetX = _rng.NextBool() ? lo0 + margin : hi0 - margin;
                break;
            case Activity.Climb:
            {
                // 近い方の壁へ（隣の画面とつながっている辺には壁がないので、その場合は反対側へ）
                bool HasWall(double x) => Terrain.Walls.Any(w => Math.Abs(w.X - x) < 1 && w.MinY <= f.Y + 1);
                bool toLeft = Position.X - lo0 < hi0 - Position.X;
                if (!HasWall(toLeft ? lo0 : hi0)) toLeft = !toLeft;
                _targetX = HasWall(toLeft ? lo0 : hi0) ? (toLeft ? lo0 + margin : hi0 - margin) : null;
                break;
            }
            case Activity.CrossScreen:
            {
                _targetX = null;
                var sides = NeighborSides(f);
                if (sides.Count == 0 || Terrain.ScreenById(f.Owner.Id) is not { } s) break;
                var side = sides[_rng.NextInt(sides.Count)];
                if (Terrain.AdjacentScreen(s, side) is not { } n || Terrain.ScreenFloor(n) is not { } nf) break;
                double depth = _rng.Range(160, 480) * S;
                _targetX = side == WallSide.Right ? Math.Min(nf.MinX + depth, nf.MaxX - margin) : Math.Max(nf.MaxX - depth, nf.MinX + margin);
                break;
            }
            case Activity.JumpDown:
            {
                bool toLeft = Position.X - f.MinX < f.MaxX - Position.X;
                _targetX = toLeft ? f.MinX - Cfg.BodyWidth : f.MaxX + Cfg.BodyWidth;
                break;
            }
            case Activity.Hang:
            {
                bool toLeft = Position.X - f.MinX < f.MaxX - Position.X;
                _targetX = toLeft ? f.MinX + margin : f.MaxX - margin;
                break;
            }
            case Activity.TurnAway:
                if (Catalog.Has(Pose.Stand, Facing.Back)) Facing = Facing.Back;
                break;
            case Activity.JumpUp:
            {
                // 窓を 1 つ選び、その真下（少し手前）まで歩いてから跳ぶ
                _targetX = null;
                _jumpTarget = null;
                var cands = JumpUpCandidates(f);
                if (cands.Count == 0) break;
                var c = cands[_rng.NextInt(cands.Count)];
                double landX = _rng.Range(c.Lo, c.Hi);
                double side = Position.X < landX ? -1 : 1;
                _targetX = Math.Clamp(landX + side * Cfg.BodyWidth * 0.6, lo0 + margin, Math.Max(lo0 + margin, hi0 - margin));
                _jumpTarget = (c.Floor, landX);
                break;
            }
            case Activity.PlayBall:
                _targetX = null;
                _kickCooldown = 0.6;
                ShowEmote(Emote.Joy, 1.5);
                break;
            case Activity.Idle or Activity.Sit or Activity.Sleep or Activity.Doze or Activity.Stretch or Activity.Joy:
                Facing = Facing.Front;
                break;
            default:
                _targetX = null;
                break;
        }
        if (a == Activity.LookAround) _lookTimer = 0;
    }

    /// <summary>同じ高さで連続している床をまとめた歩行可能範囲</summary>
    private (double Lo, double Hi) ReachableRange(Floor f)
    {
        double lo = f.MinX, hi = f.MaxX;
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var o in Terrain.Floors)
            {
                if (Math.Abs(o.Y - f.Y) >= 1) continue;
                if (o.MaxX >= lo - 1 && o.MinX < lo) { lo = o.MinX; changed = true; }
                if (o.MinX <= hi + 1 && o.MaxX > hi) { hi = o.MaxX; changed = true; }
            }
        }
        return (lo, hi);
    }

    private void SetActivity(Activity a, double duration)
    {
        Activity = a;
        _timeLeft = duration;
        if (a is not (Activity.Walk or Activity.GoToEdge or Activity.JumpDown or Activity.Climb or Activity.Hang or Activity.CrossScreen or Activity.JumpUp)) _targetX = null;
    }

    private void StartFalling(PointD v)
    {
        Motion = new Motion.Falling(v);
        Activity = Activity.Falling;
        _targetX = null;
    }

    private Floor? Landing(PointD a, PointD b)
    {
        Floor? best = null;
        foreach (var f in Terrain.Floors)
            if (f.Contains(b.X) && f.Y <= a.Y + 0.5 && f.Y >= b.Y && (best is null || f.Y > best.Value.Y)) best = f;
        return best;
    }

    private void ClampToScreensHorizontally()
    {
        // 隣の画面とつながっている辺は素通りできる
        if (Terrain.ScreenInColumn(Position) is not { } screen) return;
        double half = Cfg.BodyWidth / 2;
        var v = screen.VisibleFrame;
        double lo = Terrain.AdjacentScreen(screen, WallSide.Left) == null ? v.MinX + half : double.NegativeInfinity;
        double hi = Terrain.AdjacentScreen(screen, WallSide.Right) == null ? v.MaxX - half : double.PositiveInfinity;
        double x = Math.Min(Math.Max(Position.X, lo), hi);
        double y = Position.Y > v.MaxY - Cfg.BodyHeight ? v.MaxY - Cfg.BodyHeight : Position.Y;
        Position = new PointD(x, y);
    }

    /// <summary>画面外に出てしまったときに最寄りの画面の床に戻す</summary>
    private void Rescue()
    {
        if (Terrain.NearestScreen(Position) is not { } s) return;
        var found = Terrain.Floors.Where(x => x.Owner == SurfaceOwner.Screen(s.Id)).Select(x => (Floor?)x).FirstOrDefault();
        if (found is not { } f) return;
        Position = new PointD(Math.Min(Math.Max(Position.X, f.MinX + Cfg.BodyWidth), f.MaxX - Cfg.BodyWidth), f.Y);
        Motion = new Motion.Grounded(f);
        SetActivity(Activity.Idle, 2);
    }

    private void UpdateEnergy(double dt)
    {
        switch (Activity)
        {
            case Activity.Sleep:
                Energy = Math.Min(1, Energy + dt / 120);
                if (Energy >= 1 && _timeLeft > 3) _timeLeft = 3;
                break;
            case Activity.Doze or Activity.Sit:
                Energy = Math.Min(1, Energy + dt / 900);
                break;
            default:
                Energy = Math.Max(0, Energy - dt / 2700);
                break;
        }
    }

    // ---- 表示用 ----

    public Snapshot GetSnapshot()
    {
        Pose pose;
        var emote = Emote.None;
        switch (Motion)
        {
            case Motion.Falling: pose = Pose.Fall; break;
            case Motion.Dragged: pose = Pose.Dragged; emote = Emote.Surprise; break;
            case Motion.Climbing: pose = Pose.Climb; break;
            case Motion.Hanging: pose = Pose.Hang; break;
            default:
                switch (Activity)
                {
                    case Activity.Walk or Activity.GoToEdge or Activity.JumpDown or Activity.Climb or Activity.Hang or Activity.CrossScreen or Activity.JumpUp:
                        pose = _targetX == null ? Pose.Stand : Pose.Walk; break;
                    case Activity.Sit: pose = Pose.Sit; break;
                    case Activity.Sleep: pose = Pose.Sleep; emote = Emote.Sleepy; break;
                    case Activity.Doze: pose = Pose.Stand; emote = Emote.Sleepy; break;
                    case Activity.Stretch: pose = Pose.Stretch; emote = Emote.Stretch; break;
                    case Activity.Joy: pose = Pose.Joy; emote = Emote.Joy; break;
                    case Activity.React: pose = _reactionPose; emote = _reactionEmote; break;
                    case Activity.PlayBall: pose = _running ? Pose.Walk : Pose.Stand; break;
                    default: pose = Pose.Stand; break;
                }
                break;
        }
        if (_transientEmote is { } te) emote = te.Emote;
        var resolved = Catalog.Resolve(pose, Facing);
        // 揺れは基準サイズ（180pt 相当）で 2.5、実寸に合わせて拡大する
        double bob = resolved.Bob && pose == Pose.Walk ? Math.Abs(Math.Sin(_walkDistance / 14 * Math.PI)) * 2.5 * S : 0;
        return new Snapshot(Position, pose, Facing, emote, Activity, bob);
    }

    /// <summary>テスト用：現在の活動を強制する</summary>
    public void Force(Activity a, double duration = 10)
    {
        if (Motion is not Motion.Grounded { Floor: var f }) return;
        SetActivity(a, duration);
        Prepare(a, f);
    }
}
