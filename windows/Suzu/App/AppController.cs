using System.Diagnostics;
using Microsoft.Win32;
using Suzu.Core;
using Suzu.Platform;

namespace Suzu.App;

/// <summary>
/// アプリ全体の制御（macOS 版 AppDelegate に相当）。
/// ループ（モニターのリフレッシュレートに合わせる）、通知領域のアイコンとメニュー、設定、各部の接続を受け持つ。
/// </summary>
internal sealed class AppController : ApplicationContext
{
    private readonly AppSettings _settings = new();
    private readonly EnvironmentSensor _sensor = new();
    private readonly SpriteStore _sprites;
    private readonly MascotSimulation _sim;
    private readonly MascotWindow _window = new();
    private readonly BallWindow _ballWindow = new();
    /// <summary>鈴が自分で出したボール（遊び終わったら自分で片付ける）</summary>
    private bool _ballBySuzu;
    private double _ballIdle;
    private readonly SpeechBubble _bubble = new();
    private readonly NamedPipeBridge _bridge = new();
    private readonly MascotStatus _status = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly Updater _updater;
    /// <summary>自動更新の予告から入れ替えまでの待ち時間</summary>
    private readonly System.Windows.Forms.Timer _autoInstallTimer = new() { Interval = 10_000 };

    private readonly FrameClock _frameClock;
    private int _framesSinceMeasure;
    private double _lastMeasure;
    private readonly System.Windows.Forms.Timer _terrainTimer = new() { Interval = 1000 };
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _lastTick;
    private double _lastSave;
    private double _animationClock;
    private double _dpi = 1;

    private ControlPanelForm? _controlPanel;
    private SettingsForm? _settingsForm;
    private ConversationForm? _conversation;
    private readonly List<(bool FromSuzu, string Text)> _messages = new();

    private AppLanguage L => _settings.Language;

    /// <param name="autostart">Windows へのサインイン時の自動起動</param>
    /// <param name="updated">自動更新で入れ替わったあとの起動（インストーラーが --updated を付けて起動する）</param>
    public AppController(bool autostart, bool updated = false)
    {
        try { _sprites = new SpriteStore(); }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, L.Pick("鈴の画像素材を読み込めませんでした", "Couldn't load Suzu's image assets"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            throw;
        }

        // 最初は主ディスプレイの拡大率で大きさを決め、以後は鈴のいる画面に合わせる
        var primary = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        _dpi = Native.DpiScaleAt(primary.Left + primary.Width / 2, primary.Top + primary.Height / 2);
        var config = new MascotSimulation.Config
        {
            Paused = _settings.Paused,
            AllowPlaceholders = _settings.AllowPlaceholders,
            AllowBallPlay = _settings.AllowBallPlay,
        };
        _sim = new MascotSimulation(config, new SpriteCatalog(_sprites.Manifest, _settings.AllowMirroring));
        ApplyBodySize();

        _sensor.InteractWithWindows = _settings.InteractWithWindows;
        _sim.UpdateTerrain(_sensor.BuildTerrain());
        _sim.Place(InitialPosition());
        UpdateDpiForPosition();

        _window.Clicked += OnMascotClicked;
        _window.DoubleClicked += OnMascotDoubleClicked;
        _window.DragBegan += () => { _sim.BeginDrag(); _bubble.Dismiss(); };
        _window.Dragged += p => { _sim.DragTo(p); Render(); };
        _window.DragEnded += v => _sim.EndDrag(v);
        _window.MenuRequested += ShowMenuAt;
        _window.ResizeRequested += steps => _settings.AdjustScale(steps);
        _window.KeepOnTop = _settings.AlwaysOnTop;
        _window.Alpha = OpacityByte;
        _ballWindow.KeepOnTop = _settings.AlwaysOnTop;
        _ballWindow.Alpha = OpacityByte;
        _ballWindow.Grabbed += () => { if (_sim.Ball is { } b) b.Hold(b.Position); };
        _ballWindow.Moved += p => { _sim.Ball?.Hold(p); RenderBall(); };
        _ballWindow.Thrown += v => { _sim.Ball?.Release(v, _dpi); _ballIdle = 0; _sim.NoticeBall(); };
        _ballWindow.Tapped += () =>
        {
            if (_sim.Ball is not { } b) return;
            b.Kick(new PointD((Random.Shared.NextDouble() - 0.5) * 300 * _dpi, 550 * _dpi));
            _ballIdle = 0;
            _sim.NoticeBall();
        };

        _tray = new NotifyIcon
        {
            Icon = Theme.AppIcon(),
            Text = L.Pick("鈴", "Suzu"),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenControlPanel(); };
        _tray.BalloonTipClicked += (_, _) => OpenControlPanel();
        _menu.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };

        _updater = new Updater();
        _updater.BecameReady += OnUpdateReady;
        _autoInstallTimer.Tick += (_, _) =>
        {
            _autoInstallTimer.Stop();
            if (_settings.AutoInstallUpdates && _updater.State == UpdateState.Ready) InstallUpdate();
        };
        _updater.Start();

        _frameClock = new FrameClock(Tick);
        UpdateRefreshRate();
        _terrainTimer.Tick += (_, _) => RefreshTerrain();
        _settings.Changed += OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SetupBridge();

        if (_settings.MascotRunning) StartMascot(); else _status.Running = false;
        // サインイン時の自動起動・自動更新のあとの起動では、止めてある場合だけパネルを出す
        if ((!autostart && !updated && _settings.ShowControlPanelOnLaunch) || !_settings.MascotRunning) OpenControlPanel();
        if (updated) AnnounceUpdated();
        // 開発用：環境変数 SUZU_DEBUG_BALL=1 で起動すると、すぐにボールを出す（動作確認用）
        if (Environment.GetEnvironmentVariable("SUZU_DEBUG_BALL") == "1")
        {
            var t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); SpawnBall(bySuzu: false); };
            t.Start();
        }
    }

    // ---- 自動更新 ----

    /// <summary>新しい版のダウンロードが終わったとき</summary>
    private void OnUpdateReady()
    {
        var v = _updater.Release?.Version;
        if (_settings.AutoInstallUpdates && _updater.CanInstallSilently)
        {
            ScheduleAutoInstall();
            return;
        }
        _tray.ShowBalloonTip(8000, L.Pick("鈴", "Suzu"),
            L.Pick($"新しいバージョン {v} の準備ができました。コントロールパネルの「再起動して更新」で入れ替わります（アプリを終了したときにも入れ替わります）。",
                   $"Version {v} is ready. Choose \"Restart to update\" in the Control Panel (it is also installed when you quit)."),
            ToolTipIcon.Info);
    }

    /// <summary>鈴が「更新します」と知らせてから、10 秒後に入れ替えて起動し直す</summary>
    private void ScheduleAutoInstall()
    {
        if (_autoInstallTimer.Enabled) return;
        var v = _updater.Release?.Version;
        var text = L.Pick($"新しいバージョン（{v}）が届いたので、アプリを更新しますね。少しのあいだだけ消えます！",
                          $"Version {v} has arrived, so I'm updating the app. I'll be gone for just a moment!");
        if (_status.Running)
        {
            _sim.React(Pose.Joy, Emote.Joy, 3);
            ShowBubble(text, 10);
        }
        else
        {
            _tray.ShowBalloonTip(8000, L.Pick("鈴", "Suzu"), text, ToolTipIcon.Info);
        }
        _autoInstallTimer.Start();
    }

    /// <summary>自動更新で入れ替わったあと、少し待ってから知らせる</summary>
    private void AnnounceUpdated()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            var text = L.Pick($"バージョン {_updater.Current} になりました！", $"I'm now on version {_updater.Current}!");
            if (_status.Running)
            {
                _sim.React(Pose.Joy, Emote.Joy, 2);
                ShowBubble(text, 6);
            }
            else
            {
                _tray.ShowBalloonTip(5000, L.Pick("鈴", "Suzu"), text, ToolTipIcon.Info);
            }
        };
        timer.Start();
    }

    // ---- 起動・停止（アプリ自体は動いたまま、鈴だけを出し入れする） ----

    private void StartMascot()
    {
        RefreshTerrain();
        if (_sim.Terrain.ScreenContaining(_sim.Position) == null) _sim.Place(InitialPosition());
        if (!_window.Visible) _window.Show();
        _window.ForceRedraw();
        Render();
        StartTimers();
        _settings.MascotRunning = true;
        _status.Running = true;
        UpdateStatusText(force: true);
    }

    private void StopMascot()
    {
        _settings.SavedPosition = _sim.Position;
        StopTimers();
        _bubble.Dismiss();
        HideBall();
        _window.Hide();
        _settings.MascotRunning = false;
        _status.Running = false;
        _status.NotifyChanged();
    }

    private void ToggleRunning()
    {
        if (_status.Running) StopMascot(); else StartMascot();
    }

    private void UpdateStatusText(bool force = false)
    {
        var text = L.ActivityText(_sim.Activity);
        if (!force && _status.ActivityText == text && _status.BridgeConnected == _bridge.IsConnected) return;
        _status.ActivityText = text;
        _status.BridgeConnected = _bridge.IsConnected;
        _status.NotifyChanged();
    }

    private PointD InitialPosition()
    {
        if (_settings.SavedPosition is { } p && _sim.Terrain.ScreenContaining(p) != null) return p;
        var s = EnvironmentSensor.ToSim(Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720));
        return new PointD(s.MidX, s.MinY + s.Height * 0.6); // 少し上から降りてくる
    }

    // ---- 大きさ・表示倍率 ----

    private double BodyPx => _settings.BodyHeight * _dpi;
    private byte OpacityByte => (byte)Math.Round(_settings.Opacity * 255);

    private void ApplyBodySize()
    {
        double h = BodyPx;
        _sim.Cfg.BodyHeight = h;
        _sim.Cfg.BodyWidth = h * 0.38;
        _sim.Cfg.SpeedScale = _dpi;
        _sim.Cfg.WalkSpeed = 42 * _settings.MoveSpeed;
        _sim.Cfg.ClimbSpeed = 28 * _settings.MoveSpeed;
        _sensor.Headroom = h + 20 * _dpi;
        if (_sim.Ball is { } b) b.Radius = BallRadius;
    }

    // ---- ボール ----

    private double BallRadius => BodyPx * 0.1;

    /// <summary>ボールを出す。鈴が自分で出すときは手元に、メニューから出すときは鈴の頭上から落とす。</summary>
    private void SpawnBall(bool bySuzu)
    {
        if (_sim.Ball != null || !_status.Running) return;
        double side = _sim.Facing == Facing.Left ? -1 : 1;
        var p = _sim.Position;
        var start = bySuzu
            ? new PointD(p.X + side * _sim.Cfg.BodyWidth * 0.9, p.Y + BodyPx * 0.45)
            : new PointD(p.X + side * _sim.Cfg.BodyWidth * 1.5, p.Y + BodyPx * 1.4);
        var ball = new Ball(start, BallRadius);
        if (bySuzu) ball.Kick(new PointD(side * 120 * _dpi, 250 * _dpi));
        _sim.Ball = ball;
        _ballBySuzu = bySuzu;
        _ballIdle = 0;
        _ballWindow.ForceRedraw();
        if (!_ballWindow.Visible) _ballWindow.Show();
        RenderBall();
        if (!bySuzu) _sim.NoticeBall();
        SetBallOut(true);
    }

    private void HideBall()
    {
        _sim.Ball = null;
        _ballBySuzu = false;
        if (_ballWindow.Visible) _ballWindow.Hide();
        SetBallOut(false);
    }

    private void ToggleBall()
    {
        if (_sim.Ball != null) HideBall();
        else SpawnBall(bySuzu: false);
    }

    private void SetBallOut(bool value)
    {
        if (_status.BallOut == value) return;
        _status.BallOut = value;
        _status.NotifyChanged();
    }

    /// <summary>毎フレーム：鈴がボールを欲しがったら出し、自分で出したボールは遊び終わって少ししたら片付ける</summary>
    private void UpdateBall(double dt)
    {
        if (_sim.WantsBall) SpawnBall(bySuzu: true);
        if (_sim.Ball is not { } b) return;
        if (_ballBySuzu && _sim.Activity != Suzu.Core.Activity.PlayBall && !b.Held)
        {
            _ballIdle += dt;
            if (_ballIdle > 6 && b.IsResting(_dpi)) HideBall();
        }
        else _ballIdle = 0;
    }

    private void RenderBall()
    {
        if (_sim.Ball is { } b && _ballWindow.Visible) _ballWindow.Render(b);
    }

    /// <summary>鈴のいる画面の拡大率に合わせる（拡大率の違うディスプレイへ移ったとき）</summary>
    private void UpdateDpiForPosition()
    {
        var pt = EnvironmentSensor.ToScreen(_sim.Position);
        double dpi = Native.DpiScaleAt(pt.X, pt.Y - 1);
        if (Math.Abs(dpi - _dpi) < 0.01) return;
        _dpi = dpi;
        ApplyBodySize();
    }

    // ---- ループ ----

    private void StartTimers()
    {
        if (_frameClock.Enabled) return;
        _lastTick = _stopwatch.Elapsed.TotalSeconds;
        _lastMeasure = _lastTick;
        _framesSinceMeasure = 0;
        _frameClock.Start();
        _terrainTimer.Start();
    }

    private void StopTimers()
    {
        _frameClock.Stop();
        _terrainTimer.Stop();
    }

    private void Tick()
    {
        double now = _stopwatch.Elapsed.TotalSeconds;
        double dt = now - _lastTick;
        _lastTick = now;
        _framesSinceMeasure++;
        _animationClock += dt;
        _sim.ObserveMouse(EnvironmentSensor.ToSim(Cursor.Position));
        _sim.Step(dt);
        UpdateBall(dt);
        Render();
        if (now - _lastSave > 30) { _settings.SavedPosition = _sim.Position; _lastSave = now; }
    }

    /// <summary>鈴のいるモニターのリフレッシュレートを調べ、描画の上限を決める</summary>
    private void UpdateRefreshRate()
    {
        var pt = EnvironmentSensor.ToScreen(_sim.Position);
        int hz = Native.RefreshRateOf(Screen.FromPoint(new Point(pt.X, pt.Y - 1)));
        int max = Screen.AllScreens.Select(Native.RefreshRateOf).DefaultIfEmpty(hz).Max();
        int limit = _settings.FrameRate > 0 ? Math.Min(_settings.FrameRate, hz) : hz;
        _frameClock.MaxFps = limit;
        if (_status.RefreshRate != hz || _status.MaxRefreshRate != max)
        {
            _status.RefreshRate = hz;
            _status.MaxRefreshRate = max;
            _status.NotifyChanged();
        }
    }

    /// <summary>1 秒ごと：実際の fps を測る</summary>
    private void MeasureFrameRate()
    {
        double now = _stopwatch.Elapsed.TotalSeconds;
        double elapsed = now - _lastMeasure;
        if (elapsed <= 0) return;
        int fps = (int)Math.Round(_framesSinceMeasure / elapsed);
        _framesSinceMeasure = 0;
        _lastMeasure = now;
        if (fps == _status.MeasuredFps) return;
        _status.MeasuredFps = fps;
        if (_settingsForm is { Visible: true }) _status.NotifyChanged();
    }

    private void RefreshTerrain()
    {
        if (_frameClock.Enabled) { MeasureFrameRate(); UpdateRefreshRate(); }
        if (_controlPanel is { Visible: true }) UpdateStatusText();
        if (_sim.Motion is Motion.Dragged) return;
        UpdateDpiForPosition();
        _sensor.Headroom = BodyPx + 20 * _dpi;
        _sim.UpdateTerrain(_sensor.BuildTerrain());
    }

    private void Render()
    {
        var snap = _sim.GetSnapshot();
        var resolved = _sim.Catalog.Resolve(snap.Pose, snap.Facing);
        int index = resolved.Fps > 0 ? (int)(_animationClock * resolved.Fps) % resolved.Frames.Count : 0;
        var frame = resolved.Frames[index];
        _window.Render(_sprites, frame, resolved.Mirrored, snap.BobOffset, L.EmoteText(snap.Emote), BodyPx, _dpi, _sprites.MaxAspect);
        _window.PlaceFeet(snap.Position);
        RenderBall();
        if (_bubble.IsShowing) _bubble.Reposition(_window.SpriteRect(BodyPx, _dpi));
    }

    // ---- 設定・システム ----

    private void OnSettingsChanged(string name)
    {
        switch (name)
        {
            case nameof(AppSettings.Scale):
                ApplyBodySize();
                RefreshTerrain();
                if (_status.Running) Render();
                break;
            case nameof(AppSettings.Opacity):
                _window.Alpha = OpacityByte;
                _ballWindow.Alpha = OpacityByte;
                _ballWindow.ForceRedraw();
                _window.ForceRedraw();
                if (_status.Running) Render();
                break;
            case nameof(AppSettings.AlwaysOnTop):
                _window.KeepOnTop = _settings.AlwaysOnTop;
                _ballWindow.KeepOnTop = _settings.AlwaysOnTop;
                break;
            case nameof(AppSettings.FrameRate):
                UpdateRefreshRate();
                break;
            case nameof(AppSettings.MoveSpeed):
                ApplyBodySize();
                break;
            case nameof(AppSettings.Paused):
                _sim.Cfg.Paused = _settings.Paused;
                break;
            case nameof(AppSettings.AllowBallPlay):
                _sim.Cfg.AllowBallPlay = _settings.AllowBallPlay;
                if (!_settings.AllowBallPlay && _ballBySuzu) HideBall();
                break;
            case nameof(AppSettings.AllowPlaceholders):
                _sim.Cfg.AllowPlaceholders = _settings.AllowPlaceholders;
                break;
            case nameof(AppSettings.AllowMirroring):
                _sim.Catalog.AllowMirroring = _settings.AllowMirroring;
                break;
            case nameof(AppSettings.InteractWithWindows):
                _sensor.InteractWithWindows = _settings.InteractWithWindows;
                RefreshTerrain();
                break;
            case nameof(AppSettings.AutoInstallUpdates):
                if (!_settings.AutoInstallUpdates) _autoInstallTimer.Stop();
                else if (_updater.State == UpdateState.Ready && _updater.CanInstallSilently) ScheduleAutoInstall();
                break;
            case nameof(AppSettings.Language):
                _tray.Text = L.Pick("鈴", "Suzu");
                UpdateStatusText(force: true);
                if (_status.Running) Render();
                break;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_status.Running) RefreshTerrain();
    }

    /// <summary>スリープ中・ロック中は計算を止めて CPU を使わない</summary>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) StopTimers();
        else if (e.Mode == PowerModes.Resume && _status.Running) { RefreshTerrain(); StartTimers(); }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect) StopTimers();
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect && _status.Running) { RefreshTerrain(); StartTimers(); }
    }

    // ---- 鈴秘書連携 ----

    private void SetupBridge()
    {
        _bridge.OnEvent = Handle;
        _bridge.Start();
    }

    private void Handle(SecretaryEvent ev)
    {
        if (_conversation != null) _conversation.Connected = _bridge.IsConnected;
        switch (ev)
        {
            case SecretaryEvent.Notify n:
                _sim.React(Pose.Surprise, Emote.Surprise, 1.5);
                ShowBubble(string.IsNullOrEmpty(n.Title) ? n.Body : $"{n.Title}\n{n.Body}", 8);
                break;
            case SecretaryEvent.Speak s:
                _messages.Add((true, s.Text));
                _conversation?.Append(true, s.Text);
                ShowBubble(s.Text, Math.Max(4, s.Text.Length * 0.18));
                break;
            case SecretaryEvent.Expression x:
                var emote = x.Pose switch
                {
                    Pose.Joy => Emote.Joy, Pose.Surprise => Emote.Surprise, Pose.Troubled => Emote.Question, Pose.Sleep => Emote.Sleepy, _ => Emote.None,
                };
                _sim.React(x.Pose, emote, x.Duration);
                break;
            case SecretaryEvent.ActivityChanged a:
                // 集中中・会議中は静かに過ごす
                _sim.Cfg.Paused = _settings.Paused || a.Hint is ActivityHint.UserFocused or ActivityHint.Meeting;
                if (a.Hint == ActivityHint.BreakTime) _sim.React(Pose.Joy, Emote.Joy, 2);
                break;
        }
        UpdateStatusText();
    }

    private void ShowBubble(string text, double seconds)
    {
        if (!_status.Running) return;
        _bubble.ShowText(text, _window.SpriteRect(BodyPx, _dpi), seconds, _dpi);
    }

    // ---- マウス ----

    private void OnMascotClicked()
    {
        _sim.Poke();
        _bridge.Send(new MascotEvent(MascotEventKind.UserClicked));
    }

    private void OnMascotDoubleClicked()
    {
        _sim.React(Pose.Joy, Emote.Joy, 2);
        _bridge.Send(new MascotEvent(MascotEventKind.ConversationRequested));
        OpenConversation();
    }

    private void ShowMenuAt(Point p)
    {
        // 自分のアプリが前面でないとメニューが閉じなくなるため、いったん前面に出す
        Native.SetForegroundWindow(_window.Handle);
        _menu.Show(p);
    }

    // ---- メニュー・ウィンドウ ----

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Font = Theme.UiFont(9.5f);
        if (_updater.State == UpdateState.Ready)
        {
            _menu.Items.Add(new ToolStripMenuItem(L.Pick($"再起動して更新（{_updater.Release?.Version}）", $"Restart to Update ({_updater.Release?.Version})"), null,
                                                  (_, _) => InstallUpdate()) { Font = Theme.UiFont(9.5f, FontStyle.Bold) });
            _menu.Items.Add(new ToolStripSeparator());
        }
        else if (_updater.State == UpdateState.Available)
        {
            _menu.Items.Add(L.Pick($"新しいバージョン {_updater.Release?.Version} をダウンロード…", $"Download Version {_updater.Release?.Version}…"), null,
                            (_, _) => _updater.OpenReleasePage());
            _menu.Items.Add(new ToolStripSeparator());
        }
        _menu.Items.Add(_status.Running ? L.Pick("鈴を止める", "Stop Suzu") : L.Pick("鈴を起動する", "Start Suzu"), null, (_, _) => ToggleRunning());
        _menu.Items.Add(L.Pick("コントロールパネル…", "Control Panel…"), null, (_, _) => OpenControlPanel());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(L.Pick("鈴を呼ぶ", "Call Suzu"), null, (_, _) => Summon());
        _menu.Items.Add(new ToolStripMenuItem(_sim.Ball != null ? L.Pick("ボールをしまう", "Put the Ball Away") : L.Pick("ボールで遊ぶ", "Play with a Ball"),
                                              null, (_, _) => ToggleBall()) { Enabled = _status.Running });
        var pause = new ToolStripMenuItem(L.Pick("自律行動を一時停止", "Pause Autonomous Behavior"), null, (_, _) => _settings.Paused = !_settings.Paused)
        {
            Checked = _settings.Paused,
        };
        _menu.Items.Add(pause);
        _menu.Items.Add(BuildSizeMenu());
        _menu.Items.Add(new ToolStripMenuItem(L.Pick("ほかのウィンドウより手前に表示", "Keep Above Other Windows"), null,
                                              (_, _) => _settings.AlwaysOnTop = !_settings.AlwaysOnTop)
        {
            Checked = _settings.AlwaysOnTop,
        });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(L.Pick("鈴と話す…", "Chat with Suzu…"), null, (_, _) => OpenConversation());
        _menu.Items.Add(L.Pick("設定…", "Settings…"), null, (_, _) => OpenSettings());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(L.Pick("終了", "Quit"), null, (_, _) => Quit());
    }

    /// <summary>「大きさ」サブメニュー（よく使う大きさ＋1 段階ずつの調整）</summary>
    private ToolStripMenuItem BuildSizeMenu()
    {
        int percent = (int)Math.Round(_settings.Scale * 100);
        var size = new ToolStripMenuItem(L.Pick($"大きさ（{percent}%）", $"Size ({percent}%)"));
        size.DropDownItems.Add(new ToolStripMenuItem(L.Pick("大きくする", "Larger"), null, (_, _) => _settings.AdjustScale(1))
        {
            Enabled = _settings.Scale < AppSettings.MaxScale,
        });
        size.DropDownItems.Add(new ToolStripMenuItem(L.Pick("小さくする", "Smaller"), null, (_, _) => _settings.AdjustScale(-1))
        {
            Enabled = _settings.Scale > AppSettings.MinScale,
        });
        size.DropDownItems.Add(new ToolStripSeparator());
        foreach (var preset in AppSettings.ScalePresets)
        {
            var title = $"{(int)Math.Round(preset * 100)}%" + (preset == 1.0 ? L.Pick("（標準）", " (standard)") : "");
            size.DropDownItems.Add(new ToolStripMenuItem(title, null, (_, _) => _settings.Scale = preset)
            {
                Checked = Math.Abs(_settings.Scale - preset) < 0.001,
            });
        }
        return size;
    }

    /// <summary>カーソルのある画面に呼び寄せる</summary>
    private void Summon()
    {
        if (!_status.Running) { StartMascot(); return; }
        var mouse = Cursor.Position;
        var f = EnvironmentSensor.ToSim(Screen.FromPoint(mouse).WorkingArea);
        _dpi = Native.DpiScaleAt(mouse.X, mouse.Y);
        ApplyBodySize();
        RefreshTerrain();
        var m = EnvironmentSensor.ToSim(mouse);
        double margin = 60 * _dpi;
        _sim.Place(new PointD(Math.Min(Math.Max(m.X, f.MinX + margin), f.MaxX - margin), f.MaxY - BodyPx - 10 * _dpi));
        _sim.ShowEmote(Emote.Joy, 2);
    }

    /// <summary>別のプロセスから「もう一度開かれた」ときに呼ばれる</summary>
    public void OnReopened() => OpenControlPanel();

    private void OpenControlPanel()
    {
        _controlPanel ??= new ControlPanelForm(_settings, _status, _updater, ToggleRunning, Summon, OpenConversation, OpenSettings, Quit, InstallUpdate, ToggleBall);
        UpdateStatusText(force: true);
        BringUp(_controlPanel);
    }

    private void OpenSettings()
    {
        _settingsForm ??= new SettingsForm(_settings, _status, _updater,
                                           _sim.Catalog.MissingPoses().Select(m => $"{m.Pose.RawName()}/{m.Facing.RawName()}").ToList(), InstallUpdate);
        BringUp(_settingsForm);
    }

    private void OpenConversation()
    {
        if (_conversation == null)
        {
            _conversation = new ConversationForm(_settings)
            {
                OnSend = text =>
                {
                    _messages.Add((false, text));
                    _bridge.Send(new MascotEvent(MascotEventKind.UserMessage, text));
                    if (!_bridge.IsConnected) _sim.React(Pose.Troubled, Emote.Question, 2);
                },
            };
            foreach (var (fromSuzu, text) in _messages) _conversation.Append(fromSuzu, text);
        }
        _conversation.Connected = _bridge.IsConnected;
        if (!_conversation.Visible && _status.Running)
        {
            // 鈴の右上あたりに開く
            _conversation.StartPosition = FormStartPosition.Manual;
            var area = Screen.FromPoint(_window.Origin).WorkingArea;
            int x = Math.Min(_window.Origin.X + _window.WindowWidth + 8, area.Right - _conversation.Width);
            int y = Math.Max(area.Top, _window.Origin.Y - _conversation.Height / 2);
            _conversation.Location = new Point(Math.Max(area.Left, x), Math.Min(y, area.Bottom - _conversation.Height));
        }
        BringUp(_conversation);
    }

    private static void BringUp(Form f)
    {
        if (!f.Visible) f.Show();
        if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
        f.Activate();
        f.BringToFront();
    }

    /// <summary>「再起動して更新」：画面なしで新しい版に入れ替え、終わったら鈴を起動し直す</summary>
    private void InstallUpdate()
    {
        if (_updater.Install(relaunch: true)) Quit();
    }

    private void Quit()
    {
        _settings.SavedPosition = _sim.Position;
        // ダウンロード済みの新しい版があれば、終了するときに入れ替える（次に起動したときには新しい版になる）
        if (_updater.State == UpdateState.Ready) _updater.Install(relaunch: false);
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        StopTimers();
        _frameClock.Dispose();
        _bridge.Stop();
        _autoInstallTimer.Dispose();
        _updater.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _tray.Visible = false;
        _tray.Dispose();
        _bubble.Dispose();
        _ballWindow.Dispose();
        _window.Dispose();
        _controlPanel?.Dispose();
        _settingsForm?.Dispose();
        _conversation?.Dispose();
        _sprites.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>Windows のシャットダウン・サインアウト時にも位置を保存する</summary>
    public void SaveOnExit() => _settings.SavedPosition = _sim.Position;
}
