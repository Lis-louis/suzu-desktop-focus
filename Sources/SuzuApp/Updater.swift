import AppKit
import CryptoKit
import Foundation
import SuzuCore

/// アプリの自動更新（Mac 版）。
/// GitHub の Releases（Lis-louis/suzu-desktop-focus）に新しい版が出たら裏でインストーラー（.pkg）をダウンロードし、
/// コントロールパネル・メニューに「アップデートをインストール」を出す。押すとインストーラーが開き、
/// インストーラーが起動中の鈴を終了 → 入れ替え → 起動し直す（packaging/macos/scripts の preinstall / postinstall）。
/// /Applications への書き込みには管理者のパスワードが要るため、Windows 版と違って画面なしでは入れ替えない。
/// Release はリポジトリ直下の VERSION を上げて main に push すると GitHub Actions（.github/workflows/installers.yml）が作る。
final class Updater: ObservableObject {
    enum State: Equatable {
        /// 何もしていない（最新か、まだ確認していない）
        case idle
        /// 手動で確認中
        case checking
        /// 手動で確認した結果、最新だった
        case upToDate
        case downloading
        /// ダウンロード済み。「アップデートをインストール」でインストーラーが開く
        case ready
        /// 手動で確認したが失敗した
        case failed
    }

    private static let repo = "Lis-louis/suzu-desktop-focus"
    private static let latestAPI = URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!
    static let releasesPage = URL(string: "https://github.com/\(repo)/releases/latest")!
    private static let firstCheckDelay: TimeInterval = 10          // 起動直後は鈴の表示を優先する
    private static let checkInterval: TimeInterval = 3 * 60 * 60   // 起動しっぱなしでも 3 時間ごとに確認

    @Published private(set) var state: State = .idle
    /// ダウンロードの進み具合（0〜100）
    @Published private(set) var percent = 0
    /// 見つかった新しい版（downloading / ready のとき）
    @Published private(set) var release: ReleaseInfo?
    let current: AppVersion
    /// .app として起動しているときだけ確認する（swift run の開発中は確認しない）
    let enabled: Bool
    /// 新しい版の準備ができたとき
    var onReady: (() -> Void)?

    private let session: URLSession
    private var installerURL: URL?
    private var timer: Timer?
    private var progressObservation: NSKeyValueObservation?
    private var busy = false
    private let updatesDir: URL = {
        let caches = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first ?? FileManager.default.temporaryDirectory
        return caches.appendingPathComponent("jp.suzu.desktop/Updates", isDirectory: true)
    }()

    init() {
        current = AppVersion(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? AppVersion(0, 0, 0)
        enabled = Bundle.main.bundleURL.pathExtension == "app"
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 60
        config.httpAdditionalHeaders = ["User-Agent": "Suzu-Updater/\(current)"]
        session = URLSession(configuration: config)
    }

    func start() {
        guard enabled, timer == nil else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.firstCheckDelay) { [weak self] in self?.check() }
        let t = Timer(timeInterval: Self.checkInterval, repeats: true) { [weak self] _ in self?.check() }
        t.tolerance = 60
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    /// 新しい版があるか確認する。manual のときは結果（最新・失敗）も画面に出す。メインスレッドから呼ぶこと。
    func check(manual: Bool = false) {
        guard !busy, state != .ready else { return }
        guard enabled else {
            if manual { state = .upToDate }
            return
        }
        busy = true
        if manual { state = .checking }
        var request = URLRequest(url: Self.latestAPI)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        session.dataTask(with: request) { [weak self] data, response, error in
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            DispatchQueue.main.async {
                guard let self else { return }
                if status == 404 { self.finish(upToDate: manual); return } // まだ Release が 1 つもない
                guard error == nil, status == 200, let data else { self.fail(manual, error?.localizedDescription ?? "HTTP \(status)"); return }
                let found = ReleaseInfo.fromGitHubJSON(data) { $0.hasPrefix("Suzu-") && $0.hasSuffix(".pkg") }
                guard let found, found.version > self.current else { self.finish(upToDate: manual); return }
                self.download(found, manual: manual)
            }
        }.resume()
    }

    private func finish(upToDate manual: Bool) {
        cleanUp(keep: nil)
        release = nil
        state = manual ? .upToDate : .idle
        busy = false
    }

    private func fail(_ manual: Bool, _ message: String) {
        NSLog("[Suzu] アップデートの確認・ダウンロードに失敗: \(message)")
        release = nil
        state = manual ? .failed : .idle
        busy = false
    }

    /// ~/Library/Caches/jp.suzu.desktop/Updates に保存し、大きさと SHA-256 を確かめる。前回のダウンロードが残っていれば使う。
    private func download(_ r: ReleaseInfo, manual: Bool) {
        cleanUp(keep: r.assetName)
        release = r
        let dest = updatesDir.appendingPathComponent((r.assetName as NSString).lastPathComponent)
        if Self.verify(dest, r) { ready(dest); return }
        percent = 0
        state = .downloading
        let task = session.downloadTask(with: r.downloadURL) { [weak self] tmp, response, error in
            // tmp はこの中でしか使えないので、先に移しておく
            var moved: URL?
            var problem = error?.localizedDescription
            if problem == nil, let tmp, (response as? HTTPURLResponse)?.statusCode == 200 {
                do {
                    try FileManager.default.createDirectory(at: dest.deletingLastPathComponent(), withIntermediateDirectories: true)
                    try? FileManager.default.removeItem(at: dest)
                    try FileManager.default.moveItem(at: tmp, to: dest)
                    if Self.verify(dest, r) { moved = dest } else {
                        try? FileManager.default.removeItem(at: dest)
                        problem = "ダウンロードしたファイルが壊れています"
                    }
                } catch { problem = error.localizedDescription }
            } else if problem == nil {
                problem = "HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)"
            }
            DispatchQueue.main.async {
                guard let self else { return }
                self.progressObservation = nil
                if let moved { self.ready(moved) } else { self.fail(manual, problem ?? "unknown") }
            }
        }
        progressObservation = task.progress.observe(\.fractionCompleted) { [weak self] p, _ in
            let value = Int((p.fractionCompleted * 100).rounded(.down))
            DispatchQueue.main.async {
                guard let self, self.state == .downloading, self.percent != value else { return }
                self.percent = value
            }
        }
        task.resume()
    }

    private func ready(_ url: URL) {
        installerURL = url
        state = .ready
        busy = false
        onReady?()
    }

    private static func verify(_ url: URL, _ r: ReleaseInfo) -> Bool {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: url.path),
              let size = (attrs[.size] as? NSNumber)?.int64Value, size > 0 else { return false }
        if r.size > 0, size != r.size { return false }
        guard let expected = r.sha256 else { return true }
        guard let data = try? Data(contentsOf: url, options: .mappedIfSafe) else { return false }
        let hash = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
        return hash == expected
    }

    /// 使い終わった（または古い）インストーラーを消す
    private func cleanUp(keep: String?) {
        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(at: updatesDir, includingPropertiesForKeys: nil) else { return }
        for f in files where f.lastPathComponent != keep { try? fm.removeItem(at: f) }
    }

    /// インストーラーを開く（入れ替えの途中で鈴は終了し、終わると起動し直す）
    func install() {
        guard state == .ready, let url = installerURL, FileManager.default.fileExists(atPath: url.path) else { return }
        NSWorkspace.shared.open(url)
    }

    func openReleasePage() {
        NSWorkspace.shared.open(release?.pageURL ?? Self.releasesPage)
    }

    /// 設定画面・コントロールパネルに出す一文。出すものがなければ空。
    func describe(_ L: AppLanguage) -> String {
        let v = release?.version.description ?? ""
        switch state {
        case .idle: return ""
        case .checking: return L.pick("アップデートを確認しています…", "Checking for updates…")
        case .upToDate: return L.pick("最新のバージョン（\(current)）です", "You're up to date (\(current))")
        case .downloading: return L.pick("新しいバージョン \(v) をダウンロード中…（\(percent)%）", "Downloading version \(v)… (\(percent)%)")
        case .ready: return L.pick("新しいバージョン \(v) の準備ができました", "Version \(v) is ready to install")
        case .failed: return L.pick("アップデートを確認できませんでした（インターネット接続を確認してください）",
                                    "Couldn't check for updates (check your internet connection)")
        }
    }
}
