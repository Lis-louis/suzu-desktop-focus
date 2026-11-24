using System.Diagnostics;
using System.Drawing.Drawing2D;
using Suzu.Core;
using Suzu.Platform;

namespace Suzu.App;

/// <summary>
/// 鈴と遊ぶボールのウィンドウ。転がりに合わせて模様が回る。
/// つかんで投げる・クリックで軽く弾ませることができる。
/// </summary>
internal sealed class BallWindow : LayeredWindow
{
    /// <summary>つかんだ</summary>
    public event Action? Grabbed;
    /// <summary>つかんで動かしている（ボールの中心・シミュレーション座標）</summary>
    public event Action<PointD>? Moved;
    /// <summary>離した（速度・シミュレーション座標の px/秒）</summary>
    public event Action<PointD>? Thrown;
    /// <summary>クリックした（動かさずに離した）</summary>
    public event Action? Tapped;

    private LayeredSurface? _surface;
    private (int Size, int AngleStep) _drawn = (-1, -1);
    private Point _lastOrigin = new(int.MinValue, int.MinValue);

    private Point? _downAt;
    private PointD _grabOffset;
    private bool _dragging;
    private readonly List<(double T, PointD P)> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>ボールの状態に合わせて描き、中心が ball.Position に来るよう置く</summary>
    public void Render(Ball ball)
    {
        int size = Math.Max(8, (int)Math.Ceiling(ball.Radius * 2)) + 4;
        if (_surface == null || _surface.Width != size)
        {
            _surface?.Dispose();
            _surface = new LayeredSurface(size, size);
            _drawn = (-1, -1);
        }
        // 回転は 5° 刻みで描き直す（毎フレーム描かなくてよいように）
        int angleStep = (int)Math.Round(ball.Angle * 180 / Math.PI / 5);
        bool redraw = _drawn != (size, angleStep);
        if (redraw)
        {
            Draw(angleStep * 5f);
            _drawn = (size, angleStep);
        }
        int left = (int)Math.Round(ball.Position.X - size / 2.0);
        int top = (int)Math.Round(-ball.Position.Y - size / 2.0);
        if (redraw || _lastOrigin == new Point(int.MinValue, int.MinValue)) Present(_surface, left, top);
        else if (_lastOrigin != new Point(left, top)) MoveTo(left, top);
        _lastOrigin = new Point(left, top);
    }

    public void ForceRedraw() => _drawn = (-1, -1);

    private void Draw(float angleDeg)
    {
        if (_surface == null) return;
        using var g = _surface.BeginDraw();
        Theme.PrepareHighQuality(g);
        float size = _surface.Width - 4f;
        var rect = new RectangleF(2, 2, size, size);
        float cx = rect.X + size / 2, cy = rect.Y + size / 2;

        // 白地に鈴の色（水色）の模様。模様だけ回して転がって見せる
        using var clip = new GraphicsPath();
        clip.AddEllipse(rect);
        g.SetClip(clip);
        using (var baseBrush = new SolidBrush(Color.White)) g.FillEllipse(baseBrush, rect);
        var state = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(angleDeg);
        using (var band = new SolidBrush(Theme.Accent))
        {
            float w = size * 0.22f;
            g.FillRectangle(band, -size, -w / 2, size * 2, w);           // 横の帯
            g.FillRectangle(band, -w / 2, -size, w, size * 2);           // 縦の帯
        }
        using (var dot = new SolidBrush(Theme.SuzuBubble))
        using (var mark = new SolidBrush(Theme.Navy))
        {
            // 1 つだけ濃い色の点にして、回る向きがはっきりわかるようにする（全部同じだと速いときに逆回りに見える）
            float d = size * 0.18f, o = size * 0.28f;
            foreach (var (x, y) in new[] { (o, o), (-o, o), (-o, -o) })
                g.FillEllipse(dot, x - d / 2, y - d / 2, d, d);
            g.FillEllipse(mark, o - d * 0.6f, -o - d * 0.6f, d * 1.2f, d * 1.2f);
        }
        g.Restore(state);
        // 立体感：右下に影、左上につや
        using (var shade = new PathGradientBrush(clip)
        {
            CenterPoint = new PointF(cx - size * 0.2f, cy - size * 0.2f),
            CenterColor = Color.FromArgb(0, 0, 0, 0),
            SurroundColors = [Color.FromArgb(70, 40, 50, 110)],
        })
            g.FillEllipse(shade, rect);
        using (var gloss = new SolidBrush(Color.FromArgb(170, 255, 255, 255)))
            g.FillEllipse(gloss, cx - size * 0.3f, cy - size * 0.33f, size * 0.22f, size * 0.14f);
        g.ResetClip();
        using var pen = new Pen(Theme.Navy, Math.Max(1f, size / 28f));
        g.DrawEllipse(pen, rect);
    }

    // ---- マウス ----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var p = Cursor.Position;
        _downAt = p;
        _dragging = false;
        var center = new PointD(_lastOrigin.X + Width / 2.0, -(_lastOrigin.Y + Height / 2.0));
        if (_surface != null) center = new PointD(_lastOrigin.X + _surface.Width / 2.0, -(_lastOrigin.Y + _surface.Height / 2.0));
        var c = EnvironmentSensor.ToSim(p);
        _grabOffset = new PointD(center.X - c.X, center.Y - c.Y);
        _samples.Clear();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_downAt is not { } start || (MouseButtons & MouseButtons.Left) == 0) return;
        var p = Cursor.Position;
        if (!_dragging && Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) > 3)
        {
            _dragging = true;
            Grabbed?.Invoke();
        }
        if (!_dragging) return;
        var c = EnvironmentSensor.ToSim(p);
        _samples.Add((_clock.Elapsed.TotalSeconds, c));
        if (_samples.Count > 6) _samples.RemoveAt(0);
        Moved?.Invoke(new PointD(c.X + _grabOffset.X, c.Y + _grabOffset.Y));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _downAt == null) return;
        try
        {
            if (!_dragging) { Tapped?.Invoke(); return; }
            var v = PointD.Zero;
            if (_samples.Count >= 2)
            {
                var (t0, p0) = _samples[0];
                var (t1, p1) = _samples[^1];
                double dt = t1 - t0;
                if (dt > 0.005) v = new PointD((p1.X - p0.X) / dt, (p1.Y - p0.Y) / dt);
            }
            Thrown?.Invoke(v);
        }
        finally
        {
            _downAt = null;
            _dragging = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _surface?.Dispose();
        base.Dispose(disposing);
    }
}
