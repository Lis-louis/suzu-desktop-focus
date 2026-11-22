using System.Diagnostics;

namespace Suzu.Platform;

/// <summary>
/// モニターのリフレッシュに合わせて UI スレッドで Tick を呼ぶ。
/// 別スレッドで DwmFlush（次の画面合成まで待つ）を繰り返し、そのたびに UI スレッドへ 1 回だけ投げる。
/// WinForms のタイマーは 1 回あたり約 15ms が下限のため、60Hz を超える表示には使えない。
/// </summary>
internal sealed class FrameClock : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly Action _tick;
    private Thread? _thread;
    private volatile bool _running;
    private int _pending;
    private long _minIntervalTicks;

    public FrameClock(Action tick)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _tick = tick;
    }

    public bool Enabled => _running;

    /// <summary>1 秒あたりの上限（0 = モニターのリフレッシュレートのまま）</summary>
    public int MaxFps
    {
        set
        {
            // 少しだけ余裕を持たせ、180Hz で 90fps を指定したとき 1 回おきにきちんと描けるようにする
            long ticks = value > 0 ? (long)(Stopwatch.Frequency / (double)value * 0.9) : 0;
            Interlocked.Exchange(ref _minIntervalTicks, ticks);
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        var t = new Thread(Loop) { IsBackground = true, Name = "Suzu frame clock", Priority = ThreadPriority.AboveNormal };
        _thread = t;
        t.Start();
    }

    /// <summary>止める（スレッドは次の待ちが終わったところで抜ける）</summary>
    public void Stop() => _running = false;

    private void Loop()
    {
        var self = Thread.CurrentThread;
        long last = 0;
        while (_running && ReferenceEquals(_thread, self))
        {
            if (Native.DwmFlush() != 0) Thread.Sleep(16); // 画面合成が使えないとき（リモート接続など）は約 60fps
            long now = Stopwatch.GetTimestamp();
            long min = Interlocked.Read(ref _minIntervalTicks);
            if (min > 0 && now - last < min) continue;
            // UI スレッドが前の Tick を処理し終えていなければ、溜めずに飛ばす
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) continue;
            last = now;
            _ui.Post(_ =>
            {
                Interlocked.Exchange(ref _pending, 0);
                if (_running) _tick();
            }, null);
        }
    }

    public void Dispose() => Stop();
}
