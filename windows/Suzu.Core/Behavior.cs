namespace Suzu.Core;

/// <summary>鈴の「今やっていること」。自律行動として選ばれるものと、外部要因によるものがある。</summary>
public enum Activity
{
    // 自律行動
    Idle, Walk, LookAround, TurnAway, Doze, Sit, Sleep, Stretch, Joy, GoToEdge, JumpDown, Climb, Hang,
    // 外部要因（自律選択されない）
    React, Dragged, Falling,
}

public enum Placement { Anywhere, ScreenFloor, WindowTop }

/// <summary>行動の発生条件・確率・継続時間の定義。数値調整はここに集約する。</summary>
public readonly record struct BehaviorSpec(
    Activity Activity,
    // Pose: この行動に必要な画像素材のポーズ
    Pose Pose,
    double Weight,
    double MinDuration,
    double MaxDuration,
    double Cooldown,
    Placement Placement)
{
    public static readonly BehaviorSpec[] Defaults =
    [
        new(Activity.Idle,       Pose.Stand,   30, 3, 8,       0,   Placement.Anywhere),
        new(Activity.Walk,       Pose.Walk,    26, 4, 20,      0,   Placement.Anywhere),
        new(Activity.LookAround, Pose.Stand,   10, 4, 7,       10,  Placement.Anywhere),
        new(Activity.TurnAway,   Pose.Stand,   3,  3, 6,       40,  Placement.Anywhere),
        new(Activity.Doze,       Pose.Stand,   0,  6, 15,      30,  Placement.Anywhere),
        new(Activity.Sit,        Pose.Sit,     12, 15, 40,     20,  Placement.Anywhere),
        new(Activity.Sleep,      Pose.Sleep,   0,  40, 150,    300, Placement.Anywhere),
        new(Activity.Stretch,    Pose.Stretch, 4,  2.5, 3.5,   60,  Placement.Anywhere),
        new(Activity.Joy,        Pose.Joy,     3,  2, 3,       45,  Placement.Anywhere),
        new(Activity.GoToEdge,   Pose.Walk,    4,  30, 30,     60,  Placement.ScreenFloor),
        new(Activity.JumpDown,   Pose.Walk,    5,  30, 30,     30,  Placement.WindowTop),
        new(Activity.Climb,      Pose.Climb,   3,  8, 20,      120, Placement.ScreenFloor),
        new(Activity.Hang,       Pose.Hang,    4,  6, 15,      90,  Placement.WindowTop),
    ];
}

/// <summary>行動選択に使う状況</summary>
public sealed record BehaviorContext(double Energy, int Hour, bool OnWindow, Activity? Last, Func<BehaviorSpec, bool> Available);

/// <summary>重み付き抽選＋状況補正＋クールダウンで、完全なランダムにならない行動選択を行う。</summary>
public sealed class BehaviorPlanner
{
    public List<BehaviorSpec> Specs { get; }
    public Dictionary<Activity, double> CooldownUntil { get; } = new();

    public BehaviorPlanner(IEnumerable<BehaviorSpec>? specs = null) { Specs = (specs ?? BehaviorSpec.Defaults).ToList(); }

    public BehaviorSpec? Spec(Activity a) => Specs.Where(s => s.Activity == a).Select(s => (BehaviorSpec?)s).FirstOrDefault();

    public double Weight(BehaviorSpec s, BehaviorContext ctx, double now)
    {
        if (!ctx.Available(s)) return 0;
        if (CooldownUntil.TryGetValue(s.Activity, out var until) && until > now) return 0;
        if (s.Placement == Placement.ScreenFloor && ctx.OnWindow) return 0;
        if (s.Placement == Placement.WindowTop && !ctx.OnWindow) return 0;

        double tired = 1 - ctx.Energy;
        bool night = ctx.Hour < 6 || ctx.Hour >= 23;
        bool morning = ctx.Hour >= 6 && ctx.Hour < 10;
        double w = s.Weight;
        switch (s.Activity)
        {
            case Activity.Doze: w += 30 * tired * tired + (night ? 8 : 0); break;
            case Activity.Sleep: w += 50 * Math.Pow(tired, 3) + (night ? 15 : 0); break;
            case Activity.Walk:
            case Activity.GoToEdge: w *= (0.4 + ctx.Energy) * (night ? 0.6 : 1); break;
            case Activity.Sit: w *= ctx.OnWindow ? 2.5 : 1; w *= 1 + tired; break;
            case Activity.Stretch:
                if (morning) w *= 2;
                if (ctx.Last is Activity.Sleep or Activity.Sit or Activity.Doze) w *= 5;
                break;
            case Activity.Joy: w *= 0.5 + ctx.Energy; break;
        }
        // 同じ行動の連続や歩きっぱなしを避ける
        if (ctx.Last == s.Activity) w *= s.Activity == Activity.Idle ? 0.6 : 0.3;
        if (ctx.Last == Activity.Walk && s.Activity == Activity.Idle) w *= 1.5;
        return Math.Max(0, w);
    }

    public (Activity Activity, double Duration) Choose(BehaviorContext ctx, double now, SplitMix64 rng)
    {
        var weighted = Specs.Select(s => (Spec: s, W: Weight(s, ctx, now))).Where(t => t.W > 0).ToList();
        double total = weighted.Sum(t => t.W);
        if (total <= 0) return (Activity.Idle, 5);
        double r = rng.NextDouble() * total;
        foreach (var (s, w) in weighted)
        {
            if (r < w)
            {
                if (s.Cooldown > 0) CooldownUntil[s.Activity] = now + s.Cooldown;
                return (s.Activity, rng.Range(s.MinDuration, s.MaxDuration));
            }
            r -= w;
        }
        var last = weighted[^1].Spec;
        return (last.Activity, last.MinDuration);
    }
}

/// <summary>テスト可能な決定的乱数</summary>
public sealed class SplitMix64
{
    private ulong _state;
    public SplitMix64(ulong seed) { _state = seed; }

    public ulong Next()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }

    /// <summary>[0, 1)</summary>
    public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

    /// <summary>[lo, hi]（lo == hi なら lo）</summary>
    public double Range(double lo, double hi) => hi <= lo ? lo : lo + NextDouble() * (hi - lo);

    /// <summary>[0, count)</summary>
    public int NextInt(int count) => (int)(Next() % (ulong)count);

    public bool NextBool() => (Next() & 1) == 1;
}
