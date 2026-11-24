using System.Text.Json;

namespace Suzu.Core;

/// <summary>アプリのバージョン（"0.2.0" や タグの "v0.2.0"）。自動更新で新しいかどうかを比べるのに使う。</summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    /// <summary>"v1.2.3" / "1.2" / "1.2.3-beta" / "1.2.3.0" などを読む。読めなければ null。</summary>
    public static AppVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];
        var parts = s.Split('.');
        if (parts.Length is < 1 or > 4) return null;
        var n = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int v)) return null;
            if (i < 3) n[i] = v;
        }
        return new AppVersion(n[0], n[1], n[2]);
    }

    public int CompareTo(AppVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>GitHub Releases の 1 つの版と、そこに添付された配布ファイル。</summary>
public sealed record ReleaseInfo(
    AppVersion Version,
    string Tag,
    string PageUrl,
    string AssetName,
    string DownloadUrl,
    long Size,
    string? Sha256) // GitHub が計算した SHA-256（16 進・小文字）。古い Release では無いことがある
{
    /// <summary>
    /// GitHub API（GET /repos/{owner}/{repo}/releases/latest）の応答から、条件に合う配布ファイルを探す。
    /// 下書き・プレリリース・版が読めないもの・該当ファイルがないものは null。
    /// </summary>
    public static ReleaseInfo? FromGitHubJson(string json, Func<string, bool> assetMatches)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
        var tag = Str(root, "tag_name");
        if (AppVersion.Parse(tag) is not { } version) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = Str(a, "name");
            var url = Str(a, "browser_download_url");
            if (name == null || url == null || !assetMatches(name)) continue;
            if (Str(a, "state") is { } state && state != "uploaded") continue;
            long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var v) ? v : 0;
            string? sha = Str(a, "digest") is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? d["sha256:".Length..].ToLowerInvariant()
                : null;
            return new ReleaseInfo(version, tag!, Str(root, "html_url") ?? "", name, url, size, sha);
        }
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
