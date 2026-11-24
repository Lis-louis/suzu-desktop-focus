namespace Suzu.Core;

/// <summary>
/// 鈴と遊ぶボール。床（画面の下・ウィンドウの上）で弾み、転がり、画面の端で跳ね返る。
/// 座標は鈴と同じ（左下原点・y 上向き）。Position はボールの中心。
/// </summary>
public sealed class Ball
{
    /// <summary>はね返りの強さ（1 で落とした高さまで戻る）</summary>
    public const double Restitution = 0.62;
    /// <summary>この速さより遅く床に当たったら、弾まずに転がる</summary>
    private const double RestSpeed = 70;

    public double Radius { get; set; }
    public PointD Position { get; private set; }
    public PointD Velocity { get; private set; }
    /// <summary>転がった分の回転（ラジアン）。描画用。</summary>
    public double Angle { get; private set; }
    /// <summary>床の上にいる（弾んでいない）</summary>
    public bool OnFloor { get; private set; }
    /// <summary>マウスでつかまれている</summary>
    public bool Held { get; private set; }

    public Ball(PointD center, double radius)
    {
        Position = center;
        Radius = radius;
    }

    /// <summary>止まっている（鈴が「もう一回蹴ろう」と思える）</summary>
    public bool IsResting(double speedScale = 1) => OnFloor && Math.Abs(Velocity.X) < 25 * speedScale;

    public void Kick(PointD velocity)
    {
        Held = false;
        OnFloor = false;
        Velocity = velocity;
    }

    public void Hold(PointD center)
    {
        Held = true;
        OnFloor = false;
        Position = center;
        Velocity = PointD.Zero;
    }

    public void Release(PointD velocity, double speedScale = 1)
    {
        double lim = 1600 * speedScale;
        Kick(new PointD(Math.Clamp(velocity.X, -lim, lim), Math.Clamp(velocity.Y, -lim, lim)));
    }

    public void Step(double dt, Terrain terrain, double gravity, double speedScale = 1)
    {
        dt = Math.Min(dt, 0.1);
        if (Held) return;
        double s = speedScale, r = Radius;
        var v = Velocity;
        var prev = Position;

        if (OnFloor)
        {
            // 転がる：少しずつ遅くなる。床の端から外れたら落ちる
            double vx = v.X * Math.Pow(0.45, dt);
            if (Math.Abs(vx) < 5 * s) vx = 0;
            var p = new PointD(Position.X + vx * dt, Position.Y);
            Velocity = new PointD(vx, 0);
            Position = p;
            if (terrain.FloorAt(p.X, p.Y - r, 2) is not { } f) OnFloor = false;
            else Position = p with { Y = f.Y + r };
        }
        else
        {
            double vy = Math.Max(v.Y - gravity * s * dt, -1800 * s);
            double vx = v.X * Math.Pow(0.85, dt);
            var p = new PointD(Position.X + vx * dt, Position.Y + vy * dt);
            Velocity = new PointD(vx, vy);
            Position = p;
            if (vy <= 0 && Landing(terrain, prev.Y - r, p.Y - r, p.X) is { } f)
            {
                Position = p with { Y = f.Y + r };
                if (-vy * Restitution > RestSpeed * s) Velocity = new PointD(vx * 0.85, -vy * Restitution);
                else { Velocity = new PointD(vx, 0); OnFloor = true; }
            }
        }

        BounceOffScreenEdges(terrain);
        Angle -= (Position.X - prev.X) / Math.Max(1, r);
        if (terrain.Floors.Count > 0 && Position.Y < terrain.Floors.Min(f => f.Y) - 400 * s) Rescue(terrain);
    }

    /// <summary>下に動いたとき、底が通り抜けた床のうち一番高いもの</summary>
    private static Floor? Landing(Terrain t, double fromBottom, double toBottom, double x)
    {
        Floor? best = null;
        foreach (var f in t.Floors)
            if (f.Contains(x) && f.Y <= fromBottom + 0.5 && f.Y >= toBottom && (best is null || f.Y > best.Value.Y)) best = f;
        return best;
    }

    /// <summary>画面の左右・上で跳ね返る（隣の画面とつながっている辺は素通り）</summary>
    private void BounceOffScreenEdges(Terrain t)
    {
        if (t.ScreenInColumn(Position) is not { } screen) return;
        var f = screen.VisibleFrame;
        double r = Radius;
        var (p, v) = (Position, Velocity);
        if (t.AdjacentScreen(screen, WallSide.Left) == null && p.X < f.MinX + r) { p = p with { X = f.MinX + r }; v = v with { X = Math.Abs(v.X) * 0.8 }; }
        if (t.AdjacentScreen(screen, WallSide.Right) == null && p.X > f.MaxX - r) { p = p with { X = f.MaxX - r }; v = v with { X = -Math.Abs(v.X) * 0.8 }; }
        if (p.Y > f.MaxY - r) { p = p with { Y = f.MaxY - r }; v = v with { Y = -Math.Abs(v.Y) * 0.5 }; }
        (Position, Velocity) = (p, v);
    }

    /// <summary>画面外に落ちてしまったら、最寄りの画面の上から落とし直す</summary>
    private void Rescue(Terrain t)
    {
        if (t.NearestScreen(Position) is not { } s) return;
        var f = s.VisibleFrame;
        Position = new PointD(Math.Clamp(Position.X, f.MinX + Radius, f.MaxX - Radius), f.MaxY - Radius * 2);
        Velocity = PointD.Zero;
        OnFloor = false;
    }
}
