using System.Diagnostics;
using Suzu.Core;
using Suzu.Platform;

namespace Suzu.App;

/// <summary>
/// 鈴本体のウィンドウ。スプライトと吹き出し記号を描き、マウス操作を受け取る。
/// 画像は変形せず、位置・左右反転（許可時のみ）だけを扱う。
/// </summary>
internal sealed class MascotWindow : LayeredWindow
{
    public event Action? Clicked;
    public event Action? DoubleClicked;
    public event Action? DragBegan;
    /// <summary>ドラッグ中の足元の位置（シミュレーション座標）</summary>
    public event Action<PointD>? Dragged;
    /// <summary>離したときの速度（シミュレーション座標・px/秒）</summary>
    public event Action<PointD>? DragEnded;
    public event Action<Point>? MenuRequested;
    /// <summary>Ctrl を押しながらのホイール操作（正なら大きく）</summary>
    public event Action<int>? ResizeRequested;
    private int _wheelAccumulator;

    private LayeredSurface? _surface;
    private (string? Frame, bool Mirrored, string? Emote, int Body, double Dpi) _drawn;
    private Point _lastOrigin = new(int.MinValue, int.MinValue);

    private Point? _dragStart;
    private PointD _grabOffset;
    private bool _isDragging;
    private readonly List<(double T, PointD P)> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastClickMs = -10_000;
    private Point _lastClickPos;

    public int WindowWidth => _surface?.Width ?? 1;
    public int WindowHeight => _surface?.Height ?? 1;
    /// <summary>今の左上位置（画面座標）</summary>
    public Point Origin => _lastOrigin;

    /// <summary>
    /// 描画と配置。内容が変わったときだけ描き直し、それ以外は位置だけ動かす。
    /// </summary>
    /// <param name="feet">両足の中心（シミュレーション座標）</param>
    public void Render(SpriteStore sprites, string frame, bool mirrored, double bob, string? emoteText,
                       double bodyHeightPx, double dpiScale, double maxAspect)
    {
        int body = Math.Max(20, (int)Math.Round(bodyHeightPx));
        int width = (int)Math.Ceiling(body * maxAspect + 80 * dpiScale);
        int height = (int)Math.Ceiling(body + 34 * dpiScale);
        bool resized = _surface == null || _surface.Width != width || _surface.Height != height;
        if (resized)
        {
            _surface?.Dispose();
            _surface = new LayeredSurface(width, height);
        }
        var key = (frame, mirrored, emoteText, body, dpiScale);
        if (resized || _drawn != key)
        {
            Draw(sprites, frame, mirrored, emoteText, body, dpiScale);
            _drawn = key;
            _needsPresent = true;
        }
        _bob = bob;
    }

    private bool _needsPresent;
    private double _bob;

    /// <summary>次の Render で必ず描き直す（再表示時など）</summary>
    public void ForceRedraw() => _drawn = default;

    /// <summary>両足の中心がシミュレーション座標 feet に来るよう配置する</summary>
    public void PlaceFeet(PointD feet)
    {
        if (_surface == null) return;
        int left = (int)Math.Round(feet.X - _surface.Width / 2.0);
        int top = (int)Math.Round(-feet.Y - _surface.Height - _bob);
        var origin = new Point(left, top);
        if (_needsPresent)
        {
            Present(_surface, left, top);
            _needsPresent = false;
            _lastOrigin = origin;
        }
        else if (origin != _lastOrigin)
        {
            MoveTo(left, top);
            _lastOrigin = origin;
        }
    }

    private void Draw(SpriteStore sprites, string frame, bool mirrored, string? emoteText, int body, double dpi)
    {
        if (_surface == null) return;
        using var g = _surface.BeginDraw();
        Theme.PrepareHighQuality(g);
        var img = sprites.Scaled(frame, body);
        int w = img?.Width ?? (int)(body * 0.4);
        int x = (_surface.Width - w) / 2;
        int y = _surface.Height - body;
        if (img != null)
        {
            if (mirrored)
            {
                var state = g.Save();
                g.TranslateTransform(x + w, y);
                g.ScaleTransform(-1, 1);
                g.DrawImage(img, 0, 0, img.Width, img.Height);
                g.Restore(state);
            }
            else
            {
                g.DrawImage(img, x, y, img.Width, img.Height);
            }
        }

        if (emoteText != null)
        {
            bool ascii = emoteText.All(c => c < 128);
            float textW = (float)(Math.Max(26, emoteText.Length * (ascii ? 10 : 15) + 12) * dpi);
            float bh = (float)(24 * dpi);
            float bx = (float)(_surface.Width / 2.0 + w * 0.15);
            bx = Math.Min(bx, _surface.Width - textW - 2);
            float by = (float)(_surface.Height - (body - 6 * dpi) - bh);
            by = Math.Max(1, by);
            var rect = new RectangleF(bx, by, textW, bh);
            using var path = Theme.RoundedRect(rect, (float)(12 * dpi));
            using var fill = new SolidBrush(Color.FromArgb(242, 255, 255, 255));
            using var pen = new Pen(Theme.Accent, (float)(1.5 * dpi));
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            using var font = Theme.PixelFont((float)(15 * dpi), FontStyle.Bold);
            using var brush = new SolidBrush(Theme.Navy);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(emoteText, font, brush, rect, fmt);
        }
    }

    /// <summary>鈴の体のおおよその範囲（画面座標）。吹き出しの位置決めに使う。</summary>
    public Rectangle SpriteRect(double bodyHeightPx, double dpi)
    {
        int w = (int)(60 * dpi);
        return new Rectangle(_lastOrigin.X + WindowWidth / 2 - w / 2, _lastOrigin.Y + WindowHeight - (int)bodyHeightPx, w, (int)bodyHeightPx);
    }

    // ---- マウス ----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var p = Cursor.Position;
        _dragStart = p;
        _isDragging = false;
        // 足元 = ウィンドウ下端の中央
        var feet = new PointD(_lastOrigin.X + WindowWidth / 2.0, -(_lastOrigin.Y + WindowHeight + _bob));
        var cursor = EnvironmentSensor.ToSim(p);
        _grabOffset = new PointD(feet.X - cursor.X, feet.Y - cursor.Y);
        _samples.Clear();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start || (MouseButtons & MouseButtons.Left) == 0) return;
        var p = Cursor.Position;
        if (!_isDragging && Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) > 3)
        {
            _isDragging = true;
            DragBegan?.Invoke();
        }
        if (!_isDragging) return;
        var c = EnvironmentSensor.ToSim(p);
        _samples.Add((_clock.Elapsed.TotalSeconds, c));
        if (_samples.Count > 6) _samples.RemoveAt(0);
        Dragged?.Invoke(new PointD(c.X + _grabOffset.X, c.Y + _grabOffset.Y));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            MenuRequested?.Invoke(Cursor.Position);
            return;
        }
        if (e.Button != MouseButtons.Left || _dragStart == null) return;
        try
        {
            if (_isDragging)
            {
                var v = PointD.Zero;
                if (_samples.Count >= 2)
                {
                    var a = _samples[0];
                    var b = _samples[^1];
                    double dt = b.T - a.T;
                    if (dt > 0.005) v = new PointD((b.P.X - a.P.X) / dt, (b.P.Y - a.P.Y) / dt);
                }
                DragEnded?.Invoke(v);
                return;
            }
            long now = _clock.ElapsedMilliseconds;
            var pos = Cursor.Position;
            var size = SystemInformation.DoubleClickSize;
            bool isDouble = now - _lastClickMs <= SystemInformation.DoubleClickTime
                            && Math.Abs(pos.X - _lastClickPos.X) <= size.Width && Math.Abs(pos.Y - _lastClickPos.Y) <= size.Height;
            if (isDouble)
            {
                _lastClickMs = -10_000;
                DoubleClicked?.Invoke();
            }
            else
            {
                _lastClickMs = now;
                _lastClickPos = pos;
                Clicked?.Invoke();
            }
        }
        finally
        {
            _dragStart = null;
            _isDragging = false;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) == 0) return;
        // 高精度タッチパッドは細かい値が続けて届くので、1 ノッチ分たまってから 1 段階変える
        _wheelAccumulator += e.Delta;
        int steps = _wheelAccumulator / SystemInformation.MouseWheelScrollDelta;
        if (steps == 0) return;
        _wheelAccumulator -= steps * SystemInformation.MouseWheelScrollDelta;
        ResizeRequested?.Invoke(steps);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _surface?.Dispose();
        base.Dispose(disposing);
    }
}
