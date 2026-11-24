using System.Diagnostics;
using System.Globalization;
using Suzu.Core;

namespace Suzu.App;

/// <summary>
/// 自動更新でアプリを入れ替えている間、鈴が消えてしまわないよう「更新しています…」の吹き出しと一緒に表示しておく。
/// インストール先の Suzu.exe は入れ替えられるため、自分（exe と Sprites）を %LOCALAPPDATA%\Suzu\Splash に複製して、
/// そこから <c>--update-splash</c> で起動する。新しい鈴が起動したら消える。
/// 新しい鈴が起動しないまま入れ替えが終わったら（失敗したとき）、インストール先の鈴を起動し直す。
/// </summary>
internal static class UpdateSplash
{
    public const string Arg = "--update-splash";
    private const string InstallerPidFile = "installer.pid";
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(3);

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Suzu", "Splash");

    /// <summary>（更新する側）自分の複製を起動する。出せなくても更新は続ける。</summary>
    public static bool Launch(PointD feet, double bodyPx, double dpi, AppVersion version)
    {
        try
        {
            var dir = Directory;
            System.IO.Directory.CreateDirectory(dir);
            TryDelete(Path.Combine(dir, InstallerPidFile));
            var exe = Path.Combine(dir, "Suzu.exe");
            File.Copy(Environment.ProcessPath!, exe, overwrite: true);
            var spritesFrom = Path.Combine(AppContext.BaseDirectory, "Sprites");
            var spritesTo = Path.Combine(dir, "Sprites");
            System.IO.Directory.CreateDirectory(spritesTo);
            foreach (var f in System.IO.Directory.EnumerateFiles(spritesFrom))
                File.Copy(f, Path.Combine(spritesTo, Path.GetFileName(f)), overwrite: true);

            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir };
            foreach (var a in new[]
                     {
                         Arg, F(feet.X), F(feet.Y), F(bodyPx), F(dpi), version.ToString(),
                         Environment.ProcessId.ToString(CultureInfo.InvariantCulture), AppContext.BaseDirectory,
                     })
                psi.ArgumentList.Add(a);
            Process.Start(psi);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[Suzu] 更新中の表示を出せませんでした: {e.Message}");
            return false;
        }
    }

    /// <summary>（更新する側）インストーラーのプロセス番号を知らせる（入れ替えが終わったかを見るため）</summary>
    public static void NotifyInstallerStarted(int pid)
    {
        try { File.WriteAllText(Path.Combine(Directory, InstallerPidFile), pid.ToString(CultureInfo.InvariantCulture)); }
        catch (Exception e) { Debug.WriteLine($"[Suzu] {e.Message}"); }
    }

    /// <summary>（起動し直した鈴が）前回の複製を片付ける</summary>
    public static void CleanUp()
    {
        try { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static double P(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>（複製の側）<c>--update-splash x y bodyPx dpi version oldPid appDir</c></summary>
    public static int Run(string[] args)
    {
        if (args.Length < 8) return 1;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            using var ctx = new SplashContext(new PointD(P(args[1]), P(args[2])), P(args[3]), P(args[4]), args[5],
                                              int.Parse(args[6], CultureInfo.InvariantCulture), args[7]);
            Application.Run(ctx);
            return 0;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[Suzu] 更新中の表示に失敗: {e.Message}");
            return 1;
        }
    }

    private sealed class SplashContext : ApplicationContext
    {
        private readonly AppSettings _settings = new();
        private readonly SpriteStore _sprites = new();
        private readonly MascotWindow _window = new();
        private readonly SpeechBubble _bubble = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly double _body, _dpi;
        private readonly string _version, _appDir;
        private readonly int _oldPid;
        private int _installerPid;
        private double? _oldGoneAt, _installerDoneAt, _newSeenAt;
        private bool _relaunched;
        private int _dots;

        private AppLanguage L => _settings.Language;

        public SplashContext(PointD feet, double body, double dpi, string version, int oldPid, string appDir)
        {
            (_body, _dpi, _version, _oldPid, _appDir) = (body, dpi, version, oldPid, appDir);
            _window.KeepOnTop = _settings.AlwaysOnTop;
            _window.Alpha = (byte)Math.Round(_settings.Opacity * 255);
            var front = new SpriteCatalog(_sprites.Manifest).Resolve(Pose.Stand, Facing.Front).Frames[0];
            _window.Show();
            _window.Render(_sprites, front, false, 0, null, body, dpi, _sprites.MaxAspect);
            _window.PlaceFeet(feet);
            ShowBubble();
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        private void ShowBubble()
        {
            var dots = new string('.', _dots % 3 + 1);
            _dots++;
            var text = L.Pick($"新しいバージョン（{_version}）に更新しています{dots}\nすぐ戻りますね！",
                              $"Updating to version {_version}{dots}\nI'll be right back!");
            _bubble.ShowText(text, _window.SpriteRect(_body, _dpi), 5, _dpi);
        }

        private void Tick()
        {
            double now = _clock.Elapsed.TotalSeconds;
            if ((int)(now / 0.75) != (int)((now - 0.25) / 0.75)) ShowBubble();

            // 1. 元の鈴が終わるのを待つ（その鈴の印は「新しい鈴」と見分けられないため）
            if (_oldGoneAt == null)
            {
                if (!IsAlive(_oldPid)) _oldGoneAt = now;
                else if (now > 30) _oldGoneAt = now; // 念のため
                else return;
            }
            // 2. インストーラーが終わったか
            if (_installerPid == 0) _installerPid = ReadInstallerPid();
            if (_installerDoneAt == null && (_installerPid == 0 ? now - _oldGoneAt > 10 : !IsAlive(_installerPid))) _installerDoneAt = now;
            // 3. 新しい鈴が起動したら、ウィンドウが出るのを少し待ってから消える
            if (Mutex.TryOpenExisting(Program.MutexName, out var m))
            {
                m.Dispose();
                _newSeenAt ??= now;
                if (now - _newSeenAt > 1.2) ExitThread();
                return;
            }
            // 4. 入れ替えが終わっても新しい鈴が来ないとき（失敗）は、インストール先の鈴を起動する
            if (_installerDoneAt is { } done && now - done > 15 && !_relaunched)
            {
                _relaunched = true;
                var exe = Path.Combine(_appDir, "Suzu.exe");
                try { if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
                catch (Exception e) { Debug.WriteLine($"[Suzu] {e.Message}"); }
            }
            if (_clock.Elapsed > MaxWait) ExitThread();
        }

        private static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using var p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static int ReadInstallerPid()
        {
            try
            {
                var path = Path.Combine(Directory, InstallerPidFile);
                return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var pid) ? pid : 0;
            }
            catch (IOException) { return 0; }
        }

        protected override void ExitThreadCore()
        {
            _timer.Stop();
            _timer.Dispose();
            _bubble.Dispose();
            _window.Dispose();
            _sprites.Dispose();
            base.ExitThreadCore();
        }
    }
}
