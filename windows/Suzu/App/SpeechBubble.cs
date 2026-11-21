using Suzu.Platform;

namespace Suzu.App;

/// <summary>鈴の頭上に一時的に表示するセリフの吹き出し（通知・会話用）。クリックは背面へ通す。</summary>
internal sealed class SpeechBubble : LayeredWindow
{
    protected override bool ClickThrough => true;

    private LayeredSurface? _surface;
    private readonly System.Windows.Forms.Timer _hideTimer = new();
    public bool IsShowing { get; private set; }

    public SpeechBubble()
    {
        _hideTimer.Tick += (_, _) => Dismiss();
    }

    public void ShowText(string text, Rectangle anchor, double seconds, double dpi)
    {
        float pad = (float)(4 * dpi);
        using var font = Theme.PixelFont((float)(13 * dpi));
        SizeF textSize;
        using (var measure = Graphics.FromHwnd(IntPtr.Zero))
            textSize = measure.MeasureString(text, font, (int)(236 * dpi));
        int width = (int)Math.Ceiling(textSize.Width + 34 * dpi);
        int height = (int)Math.Ceiling(textSize.Height + 26 * dpi);

        _surface?.Dispose();
        _surface = new LayeredSurface(width, height);
        using (var g = _surface.BeginDraw())
        {
            Theme.PrepareHighQuality(g);
            var rect = new RectangleF(pad, pad, width - pad * 2, height - pad * 2);
            using var path = Theme.RoundedRect(rect, (float)(12 * dpi));
            using var fill = new SolidBrush(Color.FromArgb(248, 255, 255, 255));
            using var pen = new Pen(Theme.Accent, (float)(1.5 * dpi));
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            using var brush = new SolidBrush(Theme.Navy);
            var textRect = new RectangleF(rect.X + (float)(12 * dpi), rect.Y + (float)(8 * dpi),
                                          rect.Width - (float)(24 * dpi) + 2, rect.Height - (float)(16 * dpi) + 2);
            g.DrawString(text, font, brush, textRect);
        }
        if (!Visible) Show();
        IsShowing = true;
        Reposition(anchor);
        _hideTimer.Stop();
        _hideTimer.Interval = (int)Math.Max(500, seconds * 1000);
        _hideTimer.Start();
    }

    /// <summary>anchor（鈴の体の範囲・画面座標）の上に置く</summary>
    public void Reposition(Rectangle anchor)
    {
        if (_surface == null || !IsShowing) return;
        int left = anchor.Left + anchor.Width / 2 - _surface.Width / 2;
        int top = anchor.Top - _surface.Height - 4;
        var screen = Screen.FromPoint(new Point(anchor.Left + anchor.Width / 2, anchor.Bottom - 1)).WorkingArea;
        left = Math.Min(Math.Max(left, screen.Left), screen.Right - _surface.Width);
        top = Math.Max(top, screen.Top);
        Present(_surface, left, top);
    }

    public void Dismiss()
    {
        _hideTimer.Stop();
        IsShowing = false;
        if (Visible) Hide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _hideTimer.Dispose(); _surface?.Dispose(); }
        base.Dispose(disposing);
    }
}
