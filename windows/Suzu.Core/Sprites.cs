using System.Text.Json;
using System.Text.Json.Serialization;

namespace Suzu.Core;

/// <summary>鈴の体の姿勢。画像素材はこの単位で用意する（docs/ASSETS.md）。</summary>
public enum Pose { Stand, Walk, Sit, Sleep, Stretch, Joy, Surprise, Troubled, Fall, Dragged, Climb, Hang }

public enum Facing { Front, Left, Right, Back }

/// <summary>頭上に出す吹き出し記号。キャラクター画像は変えずに感情を補足する。</summary>
public enum Emote { None, Sleepy, Joy, Surprise, Question, Heart, Dots, Stretch }

public static class SpriteEnumExtensions
{
    public static Facing Opposite(this Facing f) => f switch
    {
        Facing.Left => Facing.Right,
        Facing.Right => Facing.Left,
        _ => f,
    };

    /// <summary>manifest.json や連携 JSON で使う名前（"stand" など）</summary>
    public static string RawName(this Pose p) => JsonNamingPolicy.CamelCase.ConvertName(p.ToString());
    public static string RawName(this Facing f) => JsonNamingPolicy.CamelCase.ConvertName(f.ToString());

    public static bool TryParsePose(string? s, out Pose pose)
    {
        pose = Pose.Stand;
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var p in Enum.GetValues<Pose>())
            if (p.RawName() == s) { pose = p; return true; }
        return false;
    }

    /// <summary>記号の既定の表示文字列（言語によって変えるものは App 層で上書き）</summary>
    public static string? RawText(this Emote e) => e switch
    {
        Emote.None => null,
        Emote.Sleepy => "Zzz",
        Emote.Joy => "♪",
        Emote.Surprise => "!",
        Emote.Question => "?",
        Emote.Heart => "♡",
        Emote.Dots => "…",
        Emote.Stretch => "ん〜",
        _ => null,
    };
}

public sealed class SpriteManifest
{
    public sealed class Entry
    {
        public Pose Pose { get; set; }
        public Facing Facing { get; set; }
        public List<string> Frames { get; set; } = [];
        /// <summary>0 または省略で静止画</summary>
        public double? Fps { get; set; }
        /// <summary>歩行時の上下の揺れ（画像は変形せず、位置のみ動かす）</summary>
        public bool? Bob { get; set; }

        public Entry() { }
        public Entry(Pose pose, Facing facing, IEnumerable<string> frames, double? fps = null, bool? bob = null)
        {
            Pose = pose; Facing = facing; Frames = frames.ToList(); Fps = fps; Bob = bob;
        }
    }

    public int Version { get; set; } = 1;
    public List<Entry> Entries { get; set; } = [];

    public SpriteManifest() { }
    public SpriteManifest(IEnumerable<Entry> entries, int version = 1) { Entries = entries.ToList(); Version = version; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static SpriteManifest Parse(string json) =>
        JsonSerializer.Deserialize<SpriteManifest>(json, Options) ?? throw new InvalidDataException("manifest.json が空です");

    public static SpriteManifest Load(string path) => Parse(File.ReadAllText(path));
}

public readonly record struct ResolvedSprite(
    IReadOnlyList<string> Frames,
    double Fps,
    bool Bob,
    // Mirrored: 左右反転して表示する（髪飾りの位置が逆になるため既定では使わない）
    bool Mirrored,
    // IsFallback: 要求されたポーズの素材がなく、立ち姿などで代用している
    bool IsFallback);

public sealed class SpriteCatalog
{
    public SpriteManifest Manifest { get; }
    public bool AllowMirroring { get; set; }

    public SpriteCatalog(SpriteManifest manifest, bool allowMirroring = false)
    {
        Manifest = manifest; AllowMirroring = allowMirroring;
    }

    private SpriteManifest.Entry? Find(Pose pose, Facing facing) =>
        Manifest.Entries.FirstOrDefault(e => e.Pose == pose && e.Facing == facing && e.Frames.Count > 0);

    /// <summary>本物の素材があるか</summary>
    public bool Has(Pose pose) => Manifest.Entries.Any(e => e.Pose == pose && e.Frames.Count > 0);

    /// <summary>その向きの素材があるか（反転許可時は反転で作れるものも含む）</summary>
    public bool Has(Pose pose, Facing facing) =>
        Find(pose, facing) != null || (AllowMirroring && facing != facing.Opposite() && Find(pose, facing.Opposite()) != null);

    public ResolvedSprite Resolve(Pose pose, Facing facing)
    {
        static ResolvedSprite Make(SpriteManifest.Entry e, bool mirrored, bool fallback) =>
            new(e.Frames, e.Fps ?? 0, e.Bob ?? false, mirrored, fallback);

        ResolvedSprite? Lookup(Pose p)
        {
            if (Find(p, facing) is { } e) return Make(e, false, p != pose);
            // 歩くときは、正面のまま横に滑るより自然なので、設定に関係なく反対向きの絵を反転して使う
            if ((AllowMirroring || p == Pose.Walk) && facing.Opposite() != facing && Find(p, facing.Opposite()) is { } m)
                return Make(m, true, p != pose);
            return null;
        }

        if (Lookup(pose) is { } r) return r;
        // 同じポーズの正面
        if (Find(pose, Facing.Front) is { } front) return Make(front, false, facing != Facing.Front);
        if (pose != Pose.Stand && Lookup(Pose.Stand) is { } st)
            return st with { Bob = false, IsFallback = true };
        if (Find(Pose.Stand, Facing.Front) is { } sf) return Make(sf, false, true);
        return Make(Manifest.Entries[0], false, true);
    }

    /// <summary>manifest が参照しているが存在しない画像名</summary>
    public List<string> MissingFiles(ISet<string> existing) =>
        Manifest.Entries.SelectMany(e => e.Frames).Distinct().Where(n => !existing.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>設計上必要だが未登録のポーズ×向き（ASSETS.md の自動レポート用）</summary>
    public static readonly (Pose Pose, Facing[] Facings)[] Required =
    {
        (Pose.Stand, new[] { Facing.Front, Facing.Left, Facing.Right, Facing.Back }), (Pose.Walk, new[] { Facing.Left, Facing.Right }),
        (Pose.Sit, new[] { Facing.Front }), (Pose.Sleep, new[] { Facing.Front }), (Pose.Stretch, new[] { Facing.Front }), (Pose.Joy, new[] { Facing.Front }),
        (Pose.Surprise, new[] { Facing.Front }), (Pose.Troubled, new[] { Facing.Front }), (Pose.Fall, new[] { Facing.Front }), (Pose.Dragged, new[] { Facing.Front }),
        (Pose.Climb, new[] { Facing.Left, Facing.Right }), (Pose.Hang, new[] { Facing.Front }),
    };

    public List<(Pose Pose, Facing Facing)> MissingPoses() =>
        Required.SelectMany(r => r.Facings.Where(f => Find(r.Pose, f) == null).Select(f => (r.Pose, f))).ToList();
}
