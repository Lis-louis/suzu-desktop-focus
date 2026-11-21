using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Suzu.Platform;

/// <summary>
/// 1 ピクセルごとの透明度を持つ描画先（DIB セクション）。
/// GDI+ でそのまま描き込み、UpdateLayeredWindow にそのまま渡せる（乗算済みアルファ）。
/// </summary>
internal sealed class LayeredSurface : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public IntPtr Hdc { get; }
    private readonly IntPtr _hBitmap;
    private readonly IntPtr _oldBitmap;
    private readonly Bitmap _bitmap;

    public LayeredSurface(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = Width,
            biHeight = -Height, // 上から下
            biPlanes = 1,
            biBitCount = 32,
        };
        var screen = Native.GetDC(IntPtr.Zero);
        try
        {
            Hdc = Native.CreateCompatibleDC(screen);
            _hBitmap = Native.CreateDIBSection(screen, ref bmi, 0, out var bits, IntPtr.Zero, 0);
            if (_hBitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
            _oldBitmap = Native.SelectObject(Hdc, _hBitmap);
            _bitmap = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, bits);
        }
        finally { Native.ReleaseDC(IntPtr.Zero, screen); }
    }

    /// <summary>透明でクリアした Graphics を返す（使い終わったら Dispose すること）</summary>
    public Graphics BeginDraw()
    {
        var g = Graphics.FromImage(_bitmap);
        g.Clear(Color.Transparent);
        return g;
    }

    public void Dispose()
    {
        _bitmap.Dispose();
        Native.SelectObject(Hdc, _oldBitmap);
        Native.DeleteObject(_hBitmap);
        Native.DeleteDC(Hdc);
    }
}

/// <summary>
/// 枠なし・背景透過・最前面・非アクティブ化のウィンドウ。
/// 完全に透明なピクセルのクリックは背面のアプリへ通る（レイヤードウィンドウの仕様）。
/// </summary>
internal class LayeredWindow : Form
{
    /// <summary>true にするとすべてのマウス操作を背面へ通す（吹き出し用）</summary>
    protected virtual bool ClickThrough => false;

    public LayeredWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            if (ClickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_MOUSEACTIVATE:
                // クリックしても作業中のアプリからフォーカスを奪わない
                m.Result = Native.MA_NOACTIVATE;
                return;
            case Native.WM_DPICHANGED:
                // 大きさと位置はこちらで管理するので、Windows の提案する再配置は使わない
                m.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>描画内容と位置（左上・物理ピクセル）をまとめて更新する</summary>
    public void Present(LayeredSurface surface, int left, int top)
    {
        if (!IsHandleCreated) return;
        var dst = new Native.POINT(left, top);
        var size = new Native.SIZE(surface.Width, surface.Height);
        var src = new Native.POINT(0, 0);
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA,
        };
        var screen = Native.GetDC(IntPtr.Zero);
        try { Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, surface.Hdc, ref src, 0, ref blend, Native.ULW_ALPHA); }
        finally { Native.ReleaseDC(IntPtr.Zero, screen); }
    }

    /// <summary>位置だけを動かす（描画し直さない）。最前面も維持する。</summary>
    public void MoveTo(int left, int top)
    {
        if (!IsHandleCreated) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, left, top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
