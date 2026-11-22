using System.Diagnostics;
using System.Text;
using Suzu.Core;

namespace Suzu.Platform;

/// <summary>
/// 画面とウィンドウの配置を取得して Terrain を作る。
/// 取得するのはウィンドウの位置と大きさだけで、タイトルや中身は読み取らない。
/// 座標は Windows の画面座標（左上原点・y 下向き・物理ピクセル）から、y を反転した座標に変換する。
/// </summary>
internal sealed class EnvironmentSensor
{
    public bool InteractWithWindows { get; set; } = true;
    public double Headroom { get; set; } = 200;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    /// <summary>Windows の画面座標の矩形 → シミュレーション座標（y 上向き）</summary>
    public static RectD ToSim(Rectangle r) => RectD.FromEdges(r.Left, -r.Bottom, r.Right, -r.Top);
    public static RectD ToSim(Native.RECT r) => RectD.FromEdges(r.Left, -r.Bottom, r.Right, -r.Top);
    public static PointD ToSim(Point p) => new(p.X, -p.Y);
    public static Point ToScreen(PointD p) => new((int)Math.Round(p.X), (int)Math.Round(-p.Y));

    public List<ScreenInfo> Screens()
    {
        var list = new List<ScreenInfo>();
        foreach (var s in Screen.AllScreens)
            list.Add(new ScreenInfo(StableId(s.DeviceName), ToSim(s.WorkingArea), ToSim(s.Bounds)));
        return list;
    }

    private static uint StableId(string name)
    {
        // プロセスをまたいで変わらないハッシュ（FNV-1a）
        uint h = 2166136261;
        foreach (char c in name) { h ^= c; h *= 16777619; }
        return h;
    }

    /// <summary>前面から背面の順で、通常のアプリのウィンドウのみ</summary>
    public List<WindowInfo> Windows()
    {
        var result = new List<WindowInfo>();
        if (!InteractWithWindows) return result;
        var cls = new StringBuilder(256);
        Native.EnumWindows((h, _) =>
        {
            try
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                Native.GetWindowThreadProcessId(h, out var pid);
                if (pid == _ownPid) return true;
                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_TOPMOST) != 0) return true;
                // 別の仮想デスクトップや非表示の UWP ウィンドウ
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                cls.Clear();
                Native.GetClassName(h, cls, cls.Capacity);
                if (IgnoredClasses.Contains(cls.ToString())) return true;
                // 影や透明な枠を除いた、見た目どおりの範囲
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0
                    && !Native.GetWindowRect(h, out r)) return true;
                if (r.Right - r.Left < 160 || r.Bottom - r.Top < 100) return true;
                result.Add(new WindowInfo((uint)(h.ToInt64() & 0xFFFFFFFF), ToSim(r)));
            }
            catch (Exception e) { Debug.WriteLine(e); }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public Terrain BuildTerrain() => Terrain.Build(Screens(), Windows(), Headroom);
}
