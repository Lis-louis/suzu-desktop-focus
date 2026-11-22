using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using Suzu.Core;

namespace Suzu.App;

/// <summary>ユーザー設定（%APPDATA%\Suzu\settings.json に保存）</summary>
internal sealed class AppSettings
{
    private sealed class Data
    {
        public double Scale { get; set; } = 1.0;
        public double Opacity { get; set; } = 1.0;
        public bool AlwaysOnTop { get; set; } = true;
        public double MoveSpeed { get; set; } = 1.0;
        /// <summary>描画の上限フレームレート（0 = モニターに合わせる）</summary>
        public int FrameRate { get; set; }
        public bool InteractWithWindows { get; set; } = true;
        public bool Paused { get; set; }
        public bool AllowMirroring { get; set; }
        public bool AllowPlaceholders { get; set; }
        public bool MascotRunning { get; set; } = true;
        public bool ShowControlPanelOnLaunch { get; set; } = true;
        /// <summary>初回は Windows の表示言語に合わせる（日本語以外なら英語）</summary>
        public string Language { get; set; } =
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
        public double[]? Position { get; set; }
    }

    public const double BaseBodyHeight = 180;
    public const double MinScale = 0.4, MaxScale = 2.0, ScaleStep = 0.05;
    /// <summary>メニューから選べる大きさ</summary>
    public static readonly double[] ScalePresets = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];
    public const double MinOpacity = 0.3, MaxOpacity = 1.0;
    public const double MinSpeed = 0.5, MaxSpeed = 2.0;
    /// <summary>選べるフレームレート（モニターの上限を超えるものは画面に出さない）</summary>
    public static readonly int[] FrameRateChoices = [30, 60, 90, 120, 144, 165, 180, 240];

    public static double ClampScale(double v) => Math.Clamp(Math.Round(Math.Round(v / ScaleStep) * ScaleStep, 2), MinScale, MaxScale);
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Suzu";

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Suzu");
    private static string FilePath => Path.Combine(Directory, "settings.json");

    private readonly Data _d;

    /// <summary>設定が変わったとき（引数はプロパティ名）</summary>
    public event Action<string>? Changed;

    public AppSettings()
    {
        _d = Load();
    }

    private static Data Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch (Exception e) { Debug.WriteLine($"[Suzu] 設定の読み込みに失敗: {e.Message}"); }
        return new Data();
    }

    private void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_d, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) { Debug.WriteLine($"[Suzu] 設定の保存に失敗: {e.Message}"); }
    }

    private void Set<T>(T current, T value, Action<T> assign, string name)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        Save();
        Changed?.Invoke(name);
    }

    /// <summary>鈴の大きさ（1.0 = 標準）</summary>
    public double Scale { get => ClampScale(_d.Scale); set => Set(_d.Scale, ClampScale(value), v => _d.Scale = v, nameof(Scale)); }
    /// <summary>鈴の不透明度（1.0 = 不透明）</summary>
    public double Opacity { get => Math.Clamp(_d.Opacity, MinOpacity, MaxOpacity); set => Set(_d.Opacity, Math.Clamp(Math.Round(value * 20) / 20, MinOpacity, MaxOpacity), v => _d.Opacity = v, nameof(Opacity)); }
    /// <summary>ほかのウィンドウより手前に表示する</summary>
    public bool AlwaysOnTop { get => _d.AlwaysOnTop; set => Set(_d.AlwaysOnTop, value, v => _d.AlwaysOnTop = v, nameof(AlwaysOnTop)); }
    /// <summary>歩く・登る速さの倍率（1.0 = 標準）</summary>
    public double MoveSpeed { get => Math.Clamp(_d.MoveSpeed, MinSpeed, MaxSpeed); set => Set(_d.MoveSpeed, Math.Clamp(Math.Round(value, 1), MinSpeed, MaxSpeed), v => _d.MoveSpeed = v, nameof(MoveSpeed)); }

    /// <summary>描画の上限フレームレート（0 = モニターのリフレッシュレートに合わせる）</summary>
    public int FrameRate { get => Math.Max(0, _d.FrameRate); set => Set(_d.FrameRate, Math.Max(0, value), v => _d.FrameRate = v, nameof(FrameRate)); }

    /// <summary>大きさを 1 段階ずつ変える（steps が正なら大きく）</summary>
    public void AdjustScale(int steps) => Scale = Scale + steps * ScaleStep;

    /// <summary>見た目・行動の設定を初期値に戻す（言語・起動・位置はそのまま）</summary>
    public void ResetToDefaults()
    {
        Scale = 1.0;
        Opacity = 1.0;
        AlwaysOnTop = true;
        MoveSpeed = 1.0;
        FrameRate = 0;
        InteractWithWindows = true;
        Paused = false;
        AllowMirroring = false;
        AllowPlaceholders = false;
    }
    public bool InteractWithWindows { get => _d.InteractWithWindows; set => Set(_d.InteractWithWindows, value, v => _d.InteractWithWindows = v, nameof(InteractWithWindows)); }
    public bool Paused { get => _d.Paused; set => Set(_d.Paused, value, v => _d.Paused = v, nameof(Paused)); }
    /// <summary>左向きの素材がないため、右向きを反転して代用する（髪飾りの位置が逆になる）</summary>
    public bool AllowMirroring { get => _d.AllowMirroring; set => Set(_d.AllowMirroring, value, v => _d.AllowMirroring = v, nameof(AllowMirroring)); }
    /// <summary>素材が未完成の行動（座る・眠る・登る等）を立ち姿＋吹き出しで試す</summary>
    public bool AllowPlaceholders { get => _d.AllowPlaceholders; set => Set(_d.AllowPlaceholders, value, v => _d.AllowPlaceholders = v, nameof(AllowPlaceholders)); }
    /// <summary>鈴をデスクトップに表示して動かすか（コントロールパネルの起動/停止）</summary>
    public bool MascotRunning { get => _d.MascotRunning; set => Set(_d.MascotRunning, value, v => _d.MascotRunning = v, nameof(MascotRunning)); }
    public bool ShowControlPanelOnLaunch { get => _d.ShowControlPanelOnLaunch; set => Set(_d.ShowControlPanelOnLaunch, value, v => _d.ShowControlPanelOnLaunch = v, nameof(ShowControlPanelOnLaunch)); }
    /// <summary>表示言語（既定は日本語）</summary>
    public AppLanguage Language { get => LanguageExtensions.FromCode(_d.Language); set => Set(_d.Language, value.Code(), v => _d.Language = v, nameof(Language)); }

    /// <summary>鈴の位置（シミュレーション座標）。頻繁に書くので Changed は発火しない。</summary>
    public PointD? SavedPosition
    {
        get => _d.Position is { Length: 2 } a ? new PointD(a[0], a[1]) : null;
        set { _d.Position = value is { } p ? [p.X, p.Y] : null; Save(); }
    }

    /// <summary>ログイン時に起動（HKCU の Run キー。インストーラーのタスクと同じ値を使う）</summary>
    public bool LaunchAtLogin
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) is string;
            }
            catch { return false; }
        }
        set
        {
            if (value == LaunchAtLogin) return;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --autostart");
                else key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            catch (Exception e) { Debug.WriteLine($"[Suzu] ログイン時起動の変更に失敗: {e.Message}"); }
            Changed?.Invoke(nameof(LaunchAtLogin));
        }
    }

    public double BodyHeight => BaseBodyHeight * Scale;
}
