namespace Suzu.Core;

/// <summary>
/// 座標系はすべて「左下原点・y 上向き」（macOS 版の Cocoa 座標と同じ向き）。
/// Windows の画面座標（左上原点・y 下向き）からの変換は App 層で y を反転して行う。
/// 単位は Windows 版では物理ピクセル。
/// </summary>
public readonly record struct PointD(double X, double Y)
{
    public static readonly PointD Zero = new(0, 0);
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;

    public static RectD FromEdges(double minX, double minY, double maxX, double maxY) =>
        new(minX, minY, maxX - minX, maxY - minY);

    public bool Contains(PointD p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public RectD Inset(double dx, double dy) => new(X + dx, Y + dy, Width - dx * 2, Height - dy * 2);
}
