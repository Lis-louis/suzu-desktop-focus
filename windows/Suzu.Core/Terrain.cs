namespace Suzu.Core;

/// <param name="Frame">画面全体（隣の画面との接し方を調べるのに使う）。省略時は VisibleFrame と同じ。</param>
public readonly record struct ScreenInfo(uint Id, RectD VisibleFrame, RectD? Frame = null)
{
    public RectD FullFrame => Frame ?? VisibleFrame;
}

public readonly record struct WindowInfo(uint Id, RectD Frame);

/// <summary>足場・壁の持ち主（画面またはウィンドウ）</summary>
public readonly record struct SurfaceOwner(bool IsWindow, uint Id)
{
    public static SurfaceOwner Screen(uint id) => new(false, id);
    public static SurfaceOwner Window(uint id) => new(true, id);
}

/// <summary>立つことのできる水平な面（スクリーン下端・ウィンドウ上端）</summary>
public readonly record struct Floor(SurfaceOwner Owner, double Y, double MinX, double MaxX)
{
    public bool Contains(double x) => x >= MinX && x <= MaxX;
    public double Width => MaxX - MinX;
}

public enum WallSide { Left, Right }

/// <summary>登ることのできる垂直な面（スクリーン左右端）</summary>
public readonly record struct Wall(SurfaceOwner Owner, WallSide Side, double X, double MinY, double MaxY);

public sealed class Terrain
{
    public IReadOnlyList<ScreenInfo> Screens { get; }
    public IReadOnlyList<Floor> Floors { get; }
    public IReadOnlyList<Wall> Walls { get; }
    /// <summary>窓に乗っている鈴を窓の移動に追従させるための位置記録</summary>
    public IReadOnlyDictionary<uint, RectD> WindowFrames { get; }

    public static readonly Terrain Empty = new([], [], [], new Dictionary<uint, RectD>());

    public Terrain(IReadOnlyList<ScreenInfo> screens, IReadOnlyList<Floor> floors, IReadOnlyList<Wall> walls,
                   IReadOnlyDictionary<uint, RectD> windowFrames)
    {
        Screens = screens; Floors = floors; Walls = walls; WindowFrames = windowFrames;
    }

    /// <param name="windows">前面から背面の順（Z オーダー順）</param>
    /// <param name="headroom">ウィンドウ上に立ったときに頭が画面外に出ないための高さ</param>
    /// <param name="minFloorWidth">これより狭い足場は無視する</param>
    public static Terrain Build(IReadOnlyList<ScreenInfo> screens, IReadOnlyList<WindowInfo> windows,
                                double headroom, double minFloorWidth = 60)
    {
        var floors = new List<Floor>();
        var walls = new List<Wall>();
        foreach (var s in screens)
        {
            var f = s.VisibleFrame;
            floors.Add(new Floor(SurfaceOwner.Screen(s.Id), f.MinY, f.MinX, f.MaxX));
            // 隣の画面と接している辺は「壁」ではなく通り道
            if (Adjacent(s, WallSide.Left, screens) == null)
                walls.Add(new Wall(SurfaceOwner.Screen(s.Id), WallSide.Left, f.MinX, f.MinY, f.MaxY));
            if (Adjacent(s, WallSide.Right, screens) == null)
                walls.Add(new Wall(SurfaceOwner.Screen(s.Id), WallSide.Right, f.MaxX, f.MinY, f.MaxY));
        }
        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            double top = w.Frame.MaxY;
            // 上端がどの画面の可視領域内にあり、頭上に余裕があるか
            ScreenInfo? screen = null;
            foreach (var s in screens)
            {
                var v = s.VisibleFrame;
                if (v.MinY < top && top + headroom <= v.MaxY && w.Frame.MaxX > v.MinX && w.Frame.MinX < v.MaxX)
                {
                    screen = s; break;
                }
            }
            if (screen is not { } sc) continue;
            var segments = new List<(double A, double B)>
            {
                (Math.Max(w.Frame.MinX, sc.VisibleFrame.MinX), Math.Min(w.Frame.MaxX, sc.VisibleFrame.MaxX))
            };
            // 手前のウィンドウに覆われている部分を取り除く
            for (int j = 0; j < i; j++)
            {
                var front = windows[j];
                if (front.Frame.MinY <= top && front.Frame.MaxY >= top)
                    segments = Subtract(segments, front.Frame.MinX, front.Frame.MaxX);
            }
            foreach (var (a, b) in segments)
                if (b - a >= minFloorWidth) floors.Add(new Floor(SurfaceOwner.Window(w.Id), top, a, b));
        }
        var frames = new Dictionary<uint, RectD>();
        foreach (var w in windows) frames.TryAdd(w.Id, w.Frame);
        return new Terrain(screens.ToList(), floors, walls, frames);
    }

    internal static List<(double A, double B)> Subtract(List<(double A, double B)> segs, double a, double b)
    {
        var result = new List<(double, double)>();
        foreach (var s in segs)
        {
            if (b <= s.A || a >= s.B) { result.Add(s); continue; }
            if (a > s.A) result.Add((s.A, a));
            if (b < s.B) result.Add((b, s.B));
        }
        return result;
    }

    /// <summary>点 p の真下（同じ高さを含む）で最も高い床</summary>
    public Floor? FloorBelow(PointD p, double tolerance = 1)
    {
        Floor? best = null;
        foreach (var f in Floors)
            if (f.Contains(p.X) && f.Y <= p.Y + tolerance && (best is null || f.Y > best.Value.Y)) best = f;
        return best;
    }

    /// <summary>同じ高さで x を含む床（隣接ディスプレイや分断された足場の乗り継ぎ用）</summary>
    public Floor? FloorAt(double x, double y, double tolerance = 1)
    {
        foreach (var f in Floors)
            if (f.Contains(x) && Math.Abs(f.Y - y) <= tolerance) return f;
        return null;
    }

    public ScreenInfo? ScreenContaining(PointD p)
    {
        foreach (var s in Screens)
            if (s.VisibleFrame.Inset(-1, -1).Contains(p)) return s;
        return null;
    }

    public ScreenInfo? ScreenById(uint id)
    {
        foreach (var s in Screens) if (s.Id == id) return s;
        return null;
    }

    /// <summary>s の左右どちらかの辺に接している画面（マルチディスプレイの乗り継ぎ用）</summary>
    public ScreenInfo? AdjacentScreen(ScreenInfo s, WallSide side) => Adjacent(s, side, Screens);

    internal static ScreenInfo? Adjacent(ScreenInfo s, WallSide side, IReadOnlyList<ScreenInfo> screens)
    {
        ScreenInfo? best = null;
        double bestD = double.MaxValue;
        var sf = s.FullFrame;
        foreach (var o in screens)
        {
            if (o.Id == s.Id) continue;
            var of = o.FullFrame;
            // 物理ピクセル単位なので少し余裕を持たせる
            bool touches = side == WallSide.Right ? Math.Abs(of.MinX - sf.MaxX) <= 2 : Math.Abs(of.MaxX - sf.MinX) <= 2;
            if (!touches || of.MaxY <= sf.MinY || of.MinY >= sf.MaxY) continue;
            // 複数あるときは床の高さが近い方
            double d = Math.Abs(o.VisibleFrame.MinY - s.VisibleFrame.MinY);
            if (d < bestD) { bestD = d; best = o; }
        }
        return best;
    }

    /// <summary>画面の床（スクリーン下端）</summary>
    public Floor? ScreenFloor(ScreenInfo s)
    {
        foreach (var f in Floors) if (f.Owner == SurfaceOwner.Screen(s.Id)) return f;
        return null;
    }

    /// <summary>x を横方向に含む画面のうち、p に縦方向で最も近いもの（なければ最寄りの画面）</summary>
    public ScreenInfo? ScreenInColumn(PointD p)
    {
        ScreenInfo? best = null;
        double bestD = double.MaxValue;
        foreach (var s in Screens)
        {
            var f = s.FullFrame;
            if (p.X < f.MinX || p.X > f.MaxX) continue;
            double d = Dist(s.VisibleFrame, p);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best ?? NearestScreen(p);
    }

    /// <summary>p に最も近いスクリーン（画面外に出てしまった場合の救済用）</summary>
    public ScreenInfo? NearestScreen(PointD p)
    {
        ScreenInfo? best = null;
        double bestD = double.MaxValue;
        foreach (var s in Screens)
        {
            double d = Dist(s.VisibleFrame, p);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    private static double Dist(RectD r, PointD p)
    {
        double dx = Math.Max(Math.Max(r.MinX - p.X, 0), p.X - r.MaxX);
        double dy = Math.Max(Math.Max(r.MinY - p.Y, 0), p.Y - r.MaxY);
        return dx * dx + dy * dy;
    }
}
