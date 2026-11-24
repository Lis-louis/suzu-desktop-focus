using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Suzu.Core;

namespace Suzu.App;

internal enum UpdateState
{
    /// <summary>何もしていない（最新か、まだ確認していない）</summary>
    Idle,
    /// <summary>手動で確認中</summary>
    Checking,
    /// <summary>手動で確認した結果、最新だった</summary>
    UpToDate,
    /// <summary>新しい版があるが、この場所では自動で入れ替えられない（インストールせずに動かしている）</summary>
    Available,
    Downloading,
    /// <summary>ダウンロード済み。「再起動して更新」を押すか、アプリを終了すると入れ替わる</summary>
    Ready,
    /// <summary>手動で確認したが失敗した</summary>
    Failed,
}

/// <summary>
/// アプリの自動更新。
/// GitHub の Releases（Lis-louis/suzu-desktop-focus）に新しい版が出たら裏でダウンロードし、
/// 鈴が「新しいバージョンに更新します」と知らせてから、10 秒後に自動で入れ替えて起動し直す（AppController）。
/// 自動で入れる設定をオフにしたときは、コントロールパネル・通知領域のメニューの「再起動して更新」か、終了するときに入れ替わる。
/// 入れ替えはセットアップウィザード（Inno Setup）を画面なしで動かして行う（設定はそのまま引き継がれる）。
/// Release はリポジトリ直下の VERSION を上げて main に push すると GitHub Actions（.github/workflows/installers.yml）が作る。
/// </summary>
internal sealed class Updater : IDisposable
{
    private const string Repo = "Lis-louis/suzu-desktop-focus";
    private const string LatestApi = $"https://api.github.com/repos/{Repo}/releases/latest";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10); // 起動直後は鈴の表示を優先する
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(3);      // 起動しっぱなしでも 3 時間ごとに確認

    private readonly HttpClient _http;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly SynchronizationContext _ui;
    private readonly string _updatesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Suzu", "Updates");
    private bool _busy;
    private bool _installStarted;

    public AppVersion Current { get; }
    public UpdateState State { get; private set; } = UpdateState.Idle;
    /// <summary>ダウンロードの進み具合（0〜100）</summary>
    public int Percent { get; private set; }
    /// <summary>見つかった新しい版（Available / Downloading / Ready のとき）</summary>
    public ReleaseInfo? Release { get; private set; }
    private string? _installerPath;

    /// <summary>セットアップウィザードで入れたもの（隣に Inno Setup のアンインストーラーがある）なら自動で入れ替えられる</summary>
    public bool IsInstalled { get; } = Directory.EnumerateFiles(AppContext.BaseDirectory, "unins*.exe").Any();

    /// <summary>
    /// 確認の画面（UAC）なしで入れ替えられるか。自分のユーザー用（既定の %LOCALAPPDATA%\Programs）なら可。
    /// 「すべてのユーザー」用に Program Files へ入れたときは管理者の確認が要るので、勝手には入れ替えない。
    /// </summary>
    public bool CanInstallSilently => IsInstalled && !NeedsAdmin;

    private static bool NeedsAdmin
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(Environment.GetFolderPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Any(p => dir.StartsWith(Path.TrimEndingDirectorySeparator(p) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
    }

    public event Action? Changed;
    /// <summary>新しい版の準備ができたとき（通知を出す）</summary>
    public event Action? BecameReady;

    public Updater()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var v = typeof(Updater).Assembly.GetName().Version;
        Current = v == null ? new AppVersion(0, 0, 0) : new AppVersion(v.Major, v.Minor, Math.Max(0, v.Build));
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Suzu-Updater", Current.ToString()));
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = (int)CheckInterval.TotalMilliseconds;
            Check();
        };
    }

    public void Start()
    {
        _timer.Interval = (int)FirstCheckDelay.TotalMilliseconds;
        _timer.Start();
    }

    /// <summary>新しい版があるか確認する。manual のときは結果（最新・失敗）も画面に出す。</summary>
    public async void Check(bool manual = false)
    {
        if (_busy || _installStarted || State == UpdateState.Ready) return;
        _busy = true;
        if (manual) Set(UpdateState.Checking);
        try
        {
            var release = await FetchLatest();
            if (release == null || release.Version <= Current)
            {
                CleanUp(keep: null);
                Set(manual ? UpdateState.UpToDate : UpdateState.Idle);
                return;
            }
            Release = release;
            if (!IsInstalled)
            {
                // インストールせずに動かしているときは、知らせるだけにする（ダウンロードページを開ける）
                Set(UpdateState.Available);
                return;
            }
            CleanUp(keep: release.AssetName);
            Percent = 0;
            Set(UpdateState.Downloading);
            _installerPath = await Download(release);
            Set(UpdateState.Ready);
            BecameReady?.Invoke();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[Suzu] アップデートの確認・ダウンロードに失敗: {e.Message}");
            Release = null;
            Set(manual ? UpdateState.Failed : UpdateState.Idle);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<ReleaseInfo?> FetchLatest()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await _http.SendAsync(req);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // まだ Release が 1 つもない
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        return ReleaseInfo.FromGitHubJson(json, IsWindowsInstaller);
    }

    /// <summary>windows\build.ps1 が作るセットアップウィザード（SuzuSetup-&lt;version&gt;-x64.exe）</summary>
    private static bool IsWindowsInstaller(string name) =>
        name.StartsWith("SuzuSetup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith("-x64.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>%LOCALAPPDATA%\Suzu\Updates に保存し、大きさと SHA-256 を確かめる。前回のダウンロードが残っていれば使う。</summary>
    private async Task<string> Download(ReleaseInfo release)
    {
        Directory.CreateDirectory(_updatesDir);
        var path = Path.Combine(_updatesDir, Path.GetFileName(release.AssetName));
        if (File.Exists(path) && await Verify(path, release)) return path;

        var part = path + ".part";
        using (var res = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? release.Size;
            await using var src = await res.Content.ReadAsStreamAsync();
            await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            long done = 0;
            int lastPercent = -1;
            int n;
            while ((n = await src.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false);
                done += n;
                int percent = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    _ui.Post(_ => { if (State == UpdateState.Downloading) { Percent = percent; Changed?.Invoke(); } }, null);
                }
            }
        }
        if (!await Verify(part, release))
        {
            TryDelete(part);
            throw new InvalidDataException("ダウンロードしたファイルが壊れています");
        }
        File.Move(part, path, overwrite: true);
        return path;
    }

    private static async Task<bool> Verify(string path, ReleaseInfo release)
    {
        var info = new FileInfo(path);
        if (release.Size > 0 && info.Length != release.Size) return false;
        if (release.Sha256 == null) return info.Length > 0;
        await using var s = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(s)).ToLowerInvariant();
        return hash == release.Sha256;
    }

    /// <summary>使い終わった（または古い）セットアップウィザードを消す</summary>
    private void CleanUp(string? keep)
    {
        try
        {
            if (!Directory.Exists(_updatesDir)) return;
            foreach (var f in Directory.EnumerateFiles(_updatesDir))
                if (keep == null || !Path.GetFileName(f).Equals(keep, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// セットアップウィザードを画面なしで動かす。呼んだ側はこのあとすぐアプリを終了すること。
    /// relaunch のときは入れ替えが終わったら鈴を起動し直す。
    /// 「すべてのユーザー」に入れていた場合は管理者の確認（UAC）が出る。断られたら false。
    /// </summary>
    public bool Install(bool relaunch)
    {
        if (_installStarted) return true;
        if (State != UpdateState.Ready || _installerPath == null || !File.Exists(_installerPath)) return false;
        var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS" + (relaunch ? " /RELAUNCH=1" : "");
        try
        {
            Process.Start(new ProcessStartInfo(_installerPath, args) { UseShellExecute = true });
            _installStarted = true;
            return true;
        }
        catch (Win32Exception) { return false; } // UAC で「いいえ」を選んだ
    }

    /// <summary>ダウンロードページをブラウザーで開く（インストールせずに動かしているとき）</summary>
    public void OpenReleasePage()
    {
        var url = string.IsNullOrEmpty(Release?.PageUrl) ? ReleasesPage : Release!.PageUrl;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Win32Exception) { }
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _http.Dispose();
    }
}

internal static class UpdateText
{
    /// <summary>設定画面・コントロールパネルに出す一文。出すものがなければ空。</summary>
    public static string Describe(this Updater u, AppLanguage L) => u.State switch
    {
        UpdateState.Checking => L.Pick("アップデートを確認しています…", "Checking for updates…"),
        UpdateState.UpToDate => L.Pick($"最新のバージョン（{u.Current}）です", $"You're up to date ({u.Current})"),
        UpdateState.Available => L.Pick($"新しいバージョン {u.Release?.Version} があります", $"Version {u.Release?.Version} is available"),
        UpdateState.Downloading => L.Pick($"新しいバージョン {u.Release?.Version} をダウンロード中…（{u.Percent}%）",
                                          $"Downloading version {u.Release?.Version}… ({u.Percent}%)"),
        UpdateState.Ready => L.Pick($"新しいバージョン {u.Release?.Version} の準備ができました",
                                    $"Version {u.Release?.Version} is ready to install"),
        UpdateState.Failed => L.Pick("アップデートを確認できませんでした（インターネット接続を確認してください）",
                                     "Couldn't check for updates (check your internet connection)"),
        _ => "",
    };
}
