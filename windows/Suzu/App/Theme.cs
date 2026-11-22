using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Suzu.App;

/// <summary>鈴のデザイン資料のパレットに合わせた色・フォント</summary>
internal static class Theme
{
    public static readonly Color Accent = Color.FromArgb(124, 148, 217);   // #7C94D9
    public static readonly Color Navy = Color.FromArgb(61, 74, 143);       // #3D4A8F
    public static readonly Color SuzuBubble = Color.FromArgb(219, 230, 255);
    public static readonly Color UserBubble = Color.FromArgb(237, 237, 237);

    private static string? _family;

    /// <summary>日本語が表示できる UI フォント（インストールされている最初のもの）</summary>
    public static string FontFamilyName
    {
        get
        {
            if (_family != null) return _family;
            using var installed = new InstalledFontCollection();
            var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _family = new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic", "Segoe UI" }
                .FirstOrDefault(names.Contains) ?? SystemFonts.MessageBoxFont?.FontFamily.Name ?? "Segoe UI";
            return _family;
        }
    }

    public static Font UiFont(float pt = 9f, FontStyle style = FontStyle.Regular) => new(FontFamilyName, pt, style, GraphicsUnit.Point);

    /// <summary>ピクセル単位のフォント（レイヤードウィンドウへの描画用）</summary>
    public static Font PixelFont(float px, FontStyle style = FontStyle.Regular) => new(FontFamilyName, px, style, GraphicsUnit.Pixel);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PrepareHighQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    public static Icon AppIcon()
    {
        using var s = typeof(Theme).Assembly.GetManifestResourceStream("Suzu.ico");
        return s != null ? new Icon(s) : (Icon)SystemIcons.Application.Clone();
    }
}
