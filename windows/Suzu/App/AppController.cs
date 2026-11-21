using System.Diagnostics;
using Microsoft.Win32;
using Suzu.Core;
using Suzu.Platform;

namespace Suzu.App;

/// <summary>
/// アプリ全体の制御（macOS 版 AppDelegate に相当）。
/// ループ(約 30fps)、通知領域のアイコンとメニュー、設定、各部の接続を受け持つ。
/// </summary>
internal sealed class AppController : ApplicationContext
{
    private readonly AppSettings _settings = new();
    private readonly EnvironmentSensor _sensor = new();
    private readonly SpriteStore _sprites;
    private readonly MascotSimulation _sim;
    private readonly MascotWindow _window = new();
    private readonly SpeechBubble _bubble = new();
    private readonly NamedPipeBridge _bridge = new();
    private readonly MascotStatus _status = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();

    private readonly System.Windows.Forms.Timer _frameTimer = new() { Interval = 30 };
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

    public AppController(bool autostart)
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

        _tray = new NotifyIcon
        {
            Icon = Theme.AppIcon(),
            Text = L.Pick("鈴", "Suzu"),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenControlPanel(); };
        _menu.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };

        _frameTimer.Tick += (_, _) => Tick();
        _terrainTimer.Tick += (_, _) => RefreshTerrain();
        _settings.Changed += OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SetupBridge();

        if (_settings.MascotRunning) StartMascot(); else _status.Running = false;
        // サインイン時の自動起動では、止めてある場合だけパネルを出す
        if ((!autostart && _settings.ShowControlPanelOnLaunch) || !_settings.MascotRunning) OpenControlPanel();
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

    private void ApplyBodySize()
    {
        double h = BodyPx;
        _sim.Cfg.BodyHeight = h;
        _sim.Cfg.BodyWidth = h * 0.38;
        _sim.Cfg.SpeedScale = _dpi;
        _sensor.Headroom = h + 20 * _dpi;
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
        if (_frameTimer.Enabled) return;
        _lastTick = _stopwatch.Elapsed.TotalSeconds;
        _frameTimer.Start();
        _terrainTimer.Start();
    }

    private void StopTimers()
    {
        _frameTimer.Stop();
        _terrainTimer.Stop();
    }

    private void Tick()
    {
        double now = _stopwatch.Elapsed.TotalSeconds;
        double dt = now - _lastTick;
        _lastTick = now;
        _animationClock += dt;
        _sim.ObserveMouse(EnvironmentSensor.ToSim(Cursor.Position));
        _sim.Step(dt);
        Render();
        if (now - _lastSave > 30) { _settings.SavedPosition = _sim.Position; _lastSave = now; }
    }

    private void RefreshTerrain()
    {
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
            case nameof(AppSettings.Paused):
                _sim.Cfg.Paused = _settings.Paused;
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
        _menu.Items.Add(_status.Running ? L.Pick("鈴を止める", "Stop Suzu") : L.Pick("鈴を起動する", "Start Suzu"), null, (_, _) => ToggleRunning());
        _menu.Items.Add(L.Pick("コントロールパネル…", "Control Panel…"), null, (_, _) => OpenControlPanel());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(L.Pick("鈴を呼ぶ", "Call Suzu"), null, (_, _) => Summon());
        var pause = new ToolStripMenuItem(L.Pick("自律行動を一時停止", "Pause Autonomous Behavior"), null, (_, _) => _settings.Paused = !_settings.Paused)
        {
            Checked = _settings.Paused,
        };
        _menu.Items.Add(pause);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(L.Pick("鈴と話す…", "Chat with Suzu…"), null, (_, _) => OpenConversation());
        _menu.Items.Add(L.Pick("設定…", "Settings…"), null, (_, _) => OpenSettings());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(L.Pick("終了", "Quit"), null, (_, _) => Quit());
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
        _controlPanel ??= new ControlPanelForm(_settings, _status, ToggleRunning, Summon, OpenConversation, OpenSettings, Quit);
        UpdateStatusText(force: true);
        BringUp(_controlPanel);
    }

    private void OpenSettings()
    {
        _settingsForm ??= new SettingsForm(_settings, _sim.Catalog.MissingPoses().Select(m => $"{m.Pose.RawName()}/{m.Facing.RawName()}").ToList());
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

    private void Quit()
    {
        _settings.SavedPosition = _sim.Position;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        StopTimers();
        _bridge.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _tray.Visible = false;
        _tray.Dispose();
        _bubble.Dispose();
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
