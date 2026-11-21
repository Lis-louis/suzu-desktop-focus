using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Suzu.Core;

namespace Suzu.App;

/// <summary>
/// 画像素材の読み込み。exe の隣の Sprites\、環境変数 SUZU_ASSETS、開発時のリポジトリ内の順に探す。
/// 表示サイズに縮小した画像をキャッシュして、毎フレームの拡大縮小処理を避ける。
/// </summary>
internal sealed class SpriteStore : IDisposable
{
    public string DirectoryPath { get; }
    public SpriteManifest Manifest { get; }
    private readonly Dictionary<string, Bitmap> _images = new();
    private readonly Dictionary<(string Name, int Height), Bitmap> _scaled = new();

    public SpriteStore()
    {
        DirectoryPath = Locate();
        Manifest = SpriteManifest.Load(Path.Combine(DirectoryPath, "manifest.json"));
        foreach (var name in Manifest.Entries.SelectMany(e => e.Frames).Distinct())
        {
            var path = Path.Combine(DirectoryPath, name + ".png");
            try
            {
                // ファイルをロックしないよう、メモリに複製してから使う
                using var src = new Bitmap(path);
                var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(copy)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                _images[name] = copy;
            }
            catch (Exception e) { Debug.WriteLine($"[Suzu] 画像が見つかりません: {path} ({e.Message})"); }
        }
        if (_images.Count == 0) throw new FileNotFoundException("Sprites の画像を 1 枚も読み込めませんでした", DirectoryPath);
    }

    public ISet<string> LoadedNames => _images.Keys.ToHashSet();

    /// <summary>全スプライト中の最大の幅/高さ比（ウィンドウ幅の決定用）</summary>
    public double MaxAspect => _images.Values.Select(i => (double)i.Width / i.Height).DefaultIfEmpty(0.5).Max();

    public double Aspect(string name) => _images.TryGetValue(name, out var i) ? (double)i.Width / i.Height : 0.5;

    /// <summary>高さ height ピクセルに縮小した画像</summary>
    public Bitmap? Scaled(string name, int height)
    {
        if (_scaled.TryGetValue((name, height), out var cached)) return cached;
        if (!_images.TryGetValue(name, out var img) || height <= 0) return null;
        int width = Math.Max(1, (int)Math.Round(img.Width * (double)height / img.Height));
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY); // 縁のにじみ防止
            g.DrawImage(img, new Rectangle(0, 0, width, height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
        }
        // 大きさを変えたときに古いキャッシュが溜まらないようにする
        if (_scaled.Count > 64) ClearScaled();
        _scaled[(name, height)] = bmp;
        return bmp;
    }

    private void ClearScaled()
    {
        foreach (var b in _scaled.Values) b.Dispose();
        _scaled.Clear();
    }

    private static string Locate()
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "Sprites") };
        if (Environment.GetEnvironmentVariable("SUZU_ASSETS") is { Length: > 0 } env) candidates.Add(env);
        // 開発時: 出力フォルダから上へたどってリポジトリの Resources\Sprites を探す
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "Resources", "Sprites"));
        foreach (var c in candidates)
            if (File.Exists(Path.Combine(c, "manifest.json"))) return c;
        throw new FileNotFoundException("Sprites\\manifest.json が見つかりません");
    }

    public void Dispose()
    {
        ClearScaled();
        foreach (var b in _images.Values) b.Dispose();
        _images.Clear();
    }
}
