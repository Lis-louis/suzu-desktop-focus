import Foundation

/// アプリのバージョン（"0.2.0" や タグの "v0.2.0"）。自動更新で新しいかどうかを比べるのに使う。
public struct AppVersion: Comparable, Hashable, CustomStringConvertible, Sendable {
    public var major: Int
    public var minor: Int
    public var patch: Int

    public init(_ major: Int, _ minor: Int, _ patch: Int) {
        self.major = major; self.minor = minor; self.patch = patch
    }

    /// "v1.2.3" / "1.2" / "1.2.3-beta" / "1.2.3.0" などを読む。読めなければ nil。
    public init?(_ text: String?) {
        guard var s = text?.trimmingCharacters(in: .whitespacesAndNewlines), !s.isEmpty else { return nil }
        if s.hasPrefix("v") || s.hasPrefix("V") { s.removeFirst() }
        if let cut = s.firstIndex(where: { $0 == "-" || $0 == "+" || $0 == " " }) { s = String(s[..<cut]) }
        let parts = s.split(separator: ".", omittingEmptySubsequences: false)
        guard (1...4).contains(parts.count) else { return nil }
        var n = [0, 0, 0]
        for (i, p) in parts.enumerated() {
            guard !p.isEmpty, p.allSatisfy(\.isASCII), p.allSatisfy(\.isNumber), let v = Int(p) else { return nil }
            if i < 3 { n[i] = v }
        }
        self.init(n[0], n[1], n[2])
    }

    public static func < (a: AppVersion, b: AppVersion) -> Bool {
        (a.major, a.minor, a.patch) < (b.major, b.minor, b.patch)
    }

    public var description: String { "\(major).\(minor).\(patch)" }
}

/// GitHub Releases の 1 つの版と、そこに添付された配布ファイル。
public struct ReleaseInfo: Equatable, Sendable {
    public var version: AppVersion
    public var tag: String
    public var pageURL: URL?
    public var assetName: String
    public var downloadURL: URL
    public var size: Int64
    /// GitHub が計算した SHA-256（16 進・小文字）。古い Release では無いことがある。
    public var sha256: String?

    private struct Release: Decodable {
        var tag_name: String?
        var html_url: String?
        var draft: Bool?
        var prerelease: Bool?
        var assets: [Asset]?
    }

    private struct Asset: Decodable {
        var name: String?
        var state: String?
        var size: Int64?
        var browser_download_url: String?
        var digest: String?
    }

    /// GitHub API（GET /repos/{owner}/{repo}/releases/latest）の応答から、条件に合う配布ファイルを探す。
    /// 下書き・プレリリース・版が読めないもの・該当ファイルがないものは nil。
    public static func fromGitHubJSON(_ data: Data, assetMatches: (String) -> Bool) -> ReleaseInfo? {
        guard let r = try? JSONDecoder().decode(Release.self, from: data) else { return nil }
        if r.draft == true || r.prerelease == true { return nil }
        guard let tag = r.tag_name, let version = AppVersion(tag) else { return nil }
        for a in r.assets ?? [] {
            guard let name = a.name, assetMatches(name),
                  let s = a.browser_download_url, let url = URL(string: s) else { continue }
            if let state = a.state, state != "uploaded" { continue }
            var sha: String?
            if let d = a.digest, d.lowercased().hasPrefix("sha256:") { sha = String(d.dropFirst("sha256:".count)).lowercased() }
            return ReleaseInfo(version: version, tag: tag, pageURL: r.html_url.flatMap(URL.init(string:)),
                               assetName: name, downloadURL: url, size: a.size ?? 0, sha256: sha)
        }
        return nil
    }
}
