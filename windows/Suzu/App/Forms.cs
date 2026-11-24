using Suzu.Core;

namespace Suzu.App;

/// <summary>コントロールパネルに表示する状態</summary>
internal sealed class MascotStatus
{
    public bool Running { get; set; } = true;
    public string ActivityText { get; set; } = "";
    public bool BridgeConnected { get; set; }
    /// <summary>鈴のいるモニターのリフレッシュレート（Hz）</summary>
    public int RefreshRate { get; set; } = 60;
    /// <summary>つながっているモニターの中で最も高いリフレッシュレート（Hz）</summary>
    public int MaxRefreshRate { get; set; } = 60;
    /// <summary>実際に描画できている回数（1 秒あたり）</summary>
    public int MeasuredFps { get; set; }
    /// <summary>ボールが出ている</summary>
    public bool BallOut { get; set; }
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}

/// <summary>共通の見た目（フォント・余白・アイコン）</summary>
internal class SuzuForm : Form
{
    public SuzuForm()
    {
        Font = Theme.UiFont(9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = Theme.AppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
    }

    protected static Label MakeLabel(float size = 9.5f, FontStyle style = FontStyle.Regular, Color? color = null) => new()
    {
        AutoSize = true,
        Font = Theme.UiFont(size, style),
        ForeColor = color ?? Color.FromArgb(90, 90, 90),
        Margin = new Padding(0, 2, 0, 2),
    };

    protected static CheckBox MakeCheck() => new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };

    protected static TableLayoutPanel MakeColumn() => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(20),
    };

    protected static Control Divider() => new Label
    {
        AutoSize = false, Height = 1, Dock = DockStyle.Fill, BackColor = Color.FromArgb(225, 225, 225), Margin = new Padding(0, 8, 0, 8),
    };
}

/// <summary>大きさの行（ラベル・－・スライダー・＋・％）。設定画面とコントロールパネルで共通。</summary>
internal sealed class ScaleRow : FlowLayoutPanel
{
    private readonly AppSettings _settings;
    private readonly Label _label = new() { AutoSize = true, ForeColor = Color.FromArgb(90, 90, 90), Margin = new Padding(0, 8, 6, 0) };
    private readonly Button _smaller = new() { Text = "−", Width = 28, Height = 26, Margin = new Padding(0, 3, 0, 0) };
    private readonly TrackBar _track = new()
    {
        Minimum = (int)Math.Round(AppSettings.MinScale / AppSettings.ScaleStep),
        Maximum = (int)Math.Round(AppSettings.MaxScale / AppSettings.ScaleStep),
        TickFrequency = 4, SmallChange = 1, LargeChange = 5, AutoSize = false, Height = 32,
    };
    private readonly Button _larger = new() { Text = "+", Width = 28, Height = 26, Margin = new Padding(0, 3, 0, 0) };
    private readonly Label _value = new() { AutoSize = true, ForeColor = Color.FromArgb(90, 90, 90), Margin = new Padding(6, 8, 0, 0) };
    private readonly ToolTip _tip = new();
    private bool _updating;

    public ScaleRow(AppSettings settings, int trackWidth)
    {
        _settings = settings;
        AutoSize = true;
        WrapContents = false;
        Margin = Padding.Empty;
        _track.Width = trackWidth;
        Controls.AddRange(new Control[] {_label, _smaller, _track, _larger, _value});
        _track.ValueChanged += (_, _) => { if (!_updating) _settings.Scale = _track.Value * AppSettings.ScaleStep; };
        _smaller.Click += (_, _) => _settings.AdjustScale(-1);
        _larger.Click += (_, _) => _settings.AdjustScale(1);
        _value.DoubleClick += (_, _) => _settings.Scale = 1.0;
        _settings.Changed += OnSettingsChanged;
        UpdateTexts();
    }

    private void OnSettingsChanged(string _) { if (!IsDisposed) UpdateTexts(); }

    public void UpdateTexts()
    {
        var L = _settings.Language;
        _updating = true;
        _label.Text = L.Pick("大きさ", "Size");
        _track.Value = Math.Clamp((int)Math.Round(_settings.Scale / AppSettings.ScaleStep), _track.Minimum, _track.Maximum);
        _value.Text = $"{(int)Math.Round(_settings.Scale * 100)}%";
        _smaller.Enabled = _settings.Scale > AppSettings.MinScale;
        _larger.Enabled = _settings.Scale < AppSettings.MaxScale;
        _tip.SetToolTip(_smaller, L.Pick("小さくする", "Smaller"));
        _tip.SetToolTip(_larger, L.Pick("大きくする", "Larger"));
        _tip.SetToolTip(_value, L.Pick("ダブルクリックで標準の大きさ（100%）に戻す", "Double-click to reset to the standard size (100%)"));
        _updating = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _settings.Changed -= OnSettingsChanged; _tip.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>鈴の起動・停止などをまとめた操作画面</summary>
internal sealed class ControlPanelForm : SuzuForm
{
    private readonly AppSettings _settings;
    private readonly MascotStatus _status;
    private readonly Label _title = MakeLabel(16f, FontStyle.Bold, Theme.Navy);
    private readonly Label _state = MakeLabel();
    private readonly Panel _dot = new() { Size = new Size(10, 10), Margin = new Padding(0, 6, 6, 0) };
    private readonly Label _now = MakeLabel();
    private readonly Button _toggle = new() { Height = 40, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Margin = new Padding(0, 10, 0, 6) };
    private readonly Button _summon = new() { AutoSize = true };
    private readonly Button _chat = new() { AutoSize = true };
    private readonly Button _settingsButton = new() { AutoSize = true };
    private readonly Button _ball = new() { AutoSize = true };
    private readonly CheckBox _pause = MakeCheck();
    private readonly CheckBox _login = MakeCheck();
    private readonly CheckBox _showOnLaunch = MakeCheck();
    private readonly Label _bridge = MakeLabel(8.5f);
    private readonly Button _quit = new() { AutoSize = true, ForeColor = Color.FromArgb(200, 40, 40) };
    private readonly Updater _updater;
    private readonly UpdateBanner _updateBanner;
    private bool _updating;

    public ControlPanelForm(AppSettings settings, MascotStatus status, Updater updater,
                            Action onToggleRunning, Action onSummon, Action onConversation, Action onSettings, Action onQuit,
                            Action onInstallUpdate, Action onToggleBall)
    {
        _settings = settings;
        _status = status;
        _updater = updater;
        _updateBanner = new UpdateBanner(settings, updater, onInstallUpdate);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var col = MakeColumn();
        col.MinimumSize = new Size(380, 0);

        var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        var icon = new PictureBox { Image = Theme.AppIcon().ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44), Margin = new Padding(0, 0, 10, 0) };
        var titleBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        var stateRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        stateRow.Controls.AddRange(new Control[] {_dot, _state});
        titleBox.Controls.AddRange(new Control[] {_title, stateRow});
        header.Controls.AddRange(new Control[] {icon, titleBox});

        _toggle.FlatAppearance.BorderSize = 0;
        _toggle.Font = Theme.UiFont(11f, FontStyle.Bold);
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        buttons.Controls.AddRange(new Control[] {_summon, _ball, _chat, _settingsButton});

        var footer = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bridge.Anchor = AnchorStyles.Left;
        footer.Controls.Add(_bridge, 0, 0);
        footer.Controls.Add(_quit, 1, 0);

        var scaleRow = new ScaleRow(settings, 200);
        col.Controls.AddRange(new Control[] {header, _updateBanner, _now, _toggle, buttons, Divider(), scaleRow, _pause, _login, _showOnLaunch, Divider(), footer});
        Controls.Add(col);

        _toggle.Click += (_, _) => onToggleRunning();
        _summon.Click += (_, _) => onSummon();
        _ball.Click += (_, _) => onToggleBall();
        _chat.Click += (_, _) => onConversation();
        _settingsButton.Click += (_, _) => onSettings();
        _quit.Click += (_, _) => onQuit();
        _pause.CheckedChanged += (_, _) => { if (!_updating) _settings.Paused = _pause.Checked; };
        _login.CheckedChanged += (_, _) => { if (!_updating) _settings.LaunchAtLogin = _login.Checked; };
        _showOnLaunch.CheckedChanged += (_, _) => { if (!_updating) _settings.ShowControlPanelOnLaunch = _showOnLaunch.Checked; };
        AcceptButton = _toggle;

        _status.Changed += UpdateTexts;
        _settings.Changed += OnSettingsChanged;
        _updater.Changed += OnUpdateChanged;
        UpdateTexts();
    }

    private void OnSettingsChanged(string _) { if (!IsDisposed) UpdateTexts(); }

    private void OnUpdateChanged()
    {
        if (IsDisposed) return;
        SuspendLayout();
        _updateBanner.UpdateTexts();
        ResumeLayout(true);
    }

    /// <summary>状態・言語を画面に反映する</summary>
    public void UpdateTexts()
    {
        if (IsDisposed) return;
        var L = _settings.Language;
        _updating = true;
        SuspendLayout();
        Text = L.Pick("鈴 コントロールパネル", "Suzu Control Panel");
        _title.Text = L.Pick("鈴", "Suzu");
        _dot.BackColor = _status.Running ? Color.FromArgb(52, 199, 89) : Color.Gray;
        _state.Text = _status.Running
            ? (_settings.Paused ? L.Pick("表示中（一時停止）", "Visible (paused)") : L.Pick("デスクトップで活動中", "Active on the desktop"))
            : L.Pick("停止中", "Stopped");
        _now.Text = _status.Running && _status.ActivityText.Length > 0 ? L.Pick("いまの様子：", "Now: ") + _status.ActivityText : "";
        _now.Visible = _now.Text.Length > 0;
        _toggle.Text = _status.Running ? L.Pick("■  鈴を止める", "■  Stop Suzu") : L.Pick("▶  鈴を起動する", "▶  Start Suzu");
        _toggle.BackColor = _status.Running ? Color.FromArgb(142, 142, 147) : Theme.Accent;
        _summon.Text = L.Pick("呼ぶ", "Call");
        _summon.Enabled = _status.Running;
        _ball.Text = _status.BallOut ? L.Pick("ボールをしまう", "Put Ball Away") : L.Pick("ボール", "Ball");
        _ball.Enabled = _status.Running;
        _chat.Text = L.Pick("話す", "Chat");
        _settingsButton.Text = L.Pick("設定", "Settings");
        _pause.Text = L.Pick("自律行動を一時停止（その場でじっとする）", "Pause autonomous behavior (stay still)");
        _pause.Checked = _settings.Paused;
        _pause.Enabled = _status.Running;
        _login.Text = L.Pick("Windows へのサインイン時に起動", "Launch when you sign in to Windows");
        _login.Checked = _settings.LaunchAtLogin;
        _showOnLaunch.Text = L.Pick("アプリ起動時にこの画面を表示", "Show this panel when the app opens");
        _showOnLaunch.Checked = _settings.ShowControlPanelOnLaunch;
        _bridge.Text = _status.BridgeConnected ? L.Pick("鈴秘書：接続中", "Suzu Secretary: connected") : L.Pick("鈴秘書：未接続", "Suzu Secretary: not connected");
        _quit.Text = L.Pick("アプリを終了", "Quit app");
        _updateBanner.UpdateTexts();
        ResumeLayout(true);
        _updating = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 閉じてもアプリは終了しない（通知領域のアイコンから再び開ける）
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _status.Changed -= UpdateTexts; _settings.Changed -= OnSettingsChanged; _updater.Changed -= OnUpdateChanged; }
        base.Dispose(disposing);
    }
}

/// <summary>コントロールパネル上部の「新しいバージョン」のお知らせ（何もなければ隠れる）</summary>
internal sealed class UpdateBanner : TableLayoutPanel
{
    private readonly AppSettings _settings;
    private readonly Updater _updater;
    private readonly Label _text = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Navy, Margin = new Padding(0, 0, 8, 0) };
    private readonly Button _action = new() { AutoSize = true, Anchor = AnchorStyles.Right, Margin = Padding.Empty };

    public UpdateBanner(AppSettings settings, Updater updater, Action onInstall)
    {
        _settings = settings;
        _updater = updater;
        AutoSize = true;
        ColumnCount = 2;
        Dock = DockStyle.Fill;
        BackColor = Theme.SuzuBubble;
        Padding = new Padding(10, 8, 10, 8);
        Margin = new Padding(0, 10, 0, 0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _text.MaximumSize = new Size(240, 0);
        Controls.Add(_text, 0, 0);
        Controls.Add(_action, 1, 0);
        _action.Click += (_, _) =>
        {
            if (_updater.State == UpdateState.Ready) onInstall();
            else if (_updater.State == UpdateState.Available) _updater.OpenReleasePage();
        };
        UpdateTexts();
    }

    public void UpdateTexts()
    {
        var L = _settings.Language;
        var st = _updater.State;
        Visible = st is UpdateState.Downloading or UpdateState.Ready or UpdateState.Available;
        _text.Text = _updater.Describe(L);
        _action.Visible = st is UpdateState.Ready or UpdateState.Available;
        _action.Text = st == UpdateState.Ready ? L.Pick("再起動して更新", "Restart to update") : L.Pick("ダウンロード…", "Download…");
    }
}

/// <summary>設定画面</summary>
internal sealed class SettingsForm : SuzuForm
{
    private readonly AppSettings _settings;
    private readonly MascotStatus _status;
    private readonly Label _fpsLabel = MakeLabel();
    private readonly ComboBox _fps = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _fpsNow = MakeLabel();
    private List<int> _fpsValues = new();
    private readonly List<string> _missingAssets;
    private readonly Label _langHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Label _appearanceHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly Label _opacityLabel = MakeLabel();
    private readonly TrackBar _opacity = new() { Minimum = 6, Maximum = 20, TickFrequency = 2, SmallChange = 1, LargeChange = 2, Width = 240, AutoSize = false, Height = 32 };
    private readonly Label _opacityValue = MakeLabel();
    private readonly CheckBox _onTop = MakeCheck();
    private readonly Label _sizeNote = MakeLabel(8.5f);
    private readonly Label _speedLabel = MakeLabel();
    private readonly TrackBar _speed = new() { Minimum = 5, Maximum = 20, TickFrequency = 5, SmallChange = 1, LargeChange = 5, Width = 240, AutoSize = false, Height = 32 };
    private readonly Label _speedValue = MakeLabel();
    private readonly Button _reset = new() { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 4, 0, 0) };
    private readonly Label _behaviorHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly CheckBox _interact = MakeCheck();
    private readonly CheckBox _pause = MakeCheck();
    private readonly CheckBox _ballPlay = MakeCheck();
    private readonly Label _tempHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly CheckBox _mirror = MakeCheck();
    private readonly CheckBox _placeholders = MakeCheck();
    private readonly Label _tempNote = MakeLabel(8.5f);
    private readonly Label _startupHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly CheckBox _login = MakeCheck();
    private readonly Label _missingHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly TextBox _missing = new() { ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Width = 420, Height = 48, ForeColor = Color.Gray };
    private readonly Label _appHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly Label _version = MakeLabel();
    private readonly Label _updateState = MakeLabel(8.5f);
    private readonly Button _updateButton = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
    private readonly CheckBox _autoUpdate = MakeCheck();
    private readonly Updater _updater;
    private bool _updating;

    public SettingsForm(AppSettings settings, MascotStatus status, Updater updater, List<string> missingAssets, Action onInstallUpdate)
    {
        _settings = settings;
        _status = status;
        _updater = updater;
        _missingAssets = missingAssets;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _language.Items.AddRange(new object[] { AppLanguage.Ja.DisplayName(), AppLanguage.En.DisplayName() });
        _tempNote.MaximumSize = new Size(420, 0);

        var sizeRow = new ScaleRow(settings, 240);
        _sizeNote.MaximumSize = new Size(420, 0);
        var opacityRow = SliderRow(_opacityLabel, _opacity, _opacityValue);
        var speedRow = SliderRow(_speedLabel, _speed, _speedValue);
        var fpsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        _fpsLabel.Margin = new Padding(0, 6, 6, 0);
        _fpsNow.Margin = new Padding(8, 6, 0, 0);
        fpsRow.Controls.AddRange(new Control[] {_fpsLabel, _fps, _fpsNow});

        var col = MakeColumn();
        col.MinimumSize = new Size(460, 0);
        col.Controls.AddRange(new Control[] {
            _langHeader, _language, Divider(),
            _appearanceHeader, sizeRow, opacityRow, _onTop, fpsRow, _sizeNote, Divider(),
            _behaviorHeader, _interact, _ballPlay, _pause, speedRow, Divider(),
            _tempHeader, _mirror, _placeholders, _tempNote, Divider(),
            _startupHeader, _login, Divider(),
            _appHeader, _version, _autoUpdate, _updateState, _updateButton, Divider(),
            _reset,
        });
        if (_missingAssets.Count > 0) col.Controls.AddRange(new Control[] {Divider(), _missingHeader, _missing});
        Controls.Add(col);

        _language.SelectedIndexChanged += (_, _) => { if (!_updating) _settings.Language = _language.SelectedIndex == 1 ? AppLanguage.En : AppLanguage.Ja; };
        _opacity.ValueChanged += (_, _) => { if (!_updating) _settings.Opacity = _opacity.Value / 20.0; };
        _onTop.CheckedChanged += (_, _) => { if (!_updating) _settings.AlwaysOnTop = _onTop.Checked; };
        _speed.ValueChanged += (_, _) => { if (!_updating) _settings.MoveSpeed = _speed.Value / 10.0; };
        _reset.Click += (_, _) => _settings.ResetToDefaults();
        _fps.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && _fps.SelectedIndex >= 0 && _fps.SelectedIndex < _fpsValues.Count) _settings.FrameRate = _fpsValues[_fps.SelectedIndex];
        };
        _status.Changed += OnStatusChanged;
        _interact.CheckedChanged += (_, _) => { if (!_updating) _settings.InteractWithWindows = _interact.Checked; };
        _ballPlay.CheckedChanged += (_, _) => { if (!_updating) _settings.AllowBallPlay = _ballPlay.Checked; };
        _pause.CheckedChanged += (_, _) => { if (!_updating) _settings.Paused = _pause.Checked; };
        _mirror.CheckedChanged += (_, _) => { if (!_updating) _settings.AllowMirroring = _mirror.Checked; };
        _placeholders.CheckedChanged += (_, _) => { if (!_updating) _settings.AllowPlaceholders = _placeholders.Checked; };
        _login.CheckedChanged += (_, _) => { if (!_updating) _settings.LaunchAtLogin = _login.Checked; };
        _updateState.MaximumSize = new Size(420, 0);
        _autoUpdate.CheckedChanged += (_, _) => { if (!_updating) _settings.AutoInstallUpdates = _autoUpdate.Checked; };
        _updateButton.Click += (_, _) =>
        {
            switch (_updater.State)
            {
                case UpdateState.Ready: onInstallUpdate(); break;
                case UpdateState.Available: _updater.OpenReleasePage(); break;
                default: _updater.Check(manual: true); break;
            }
        };

        _settings.Changed += OnSettingsChanged;
        _updater.Changed += OnUpdateChanged;
        UpdateTexts();
    }

    private void OnUpdateChanged()
    {
        if (IsDisposed) return;
        SuspendLayout();
        UpdateAppSection();
        ResumeLayout(true);
    }

    /// <summary>「アプリ」の欄（バージョン・アップデート）</summary>
    private void UpdateAppSection()
    {
        var L = _settings.Language;
        var st = _updater.State;
        _appHeader.Text = L.Pick("アプリ", "App");
        _version.Text = L.Pick($"バージョン {_updater.Current}", $"Version {_updater.Current}");
        _autoUpdate.Text = L.Pick("新しいバージョンを自動で入れる（鈴が知らせてから再起動します）",
                                  "Install new versions automatically (Suzu lets you know, then restarts)");
        _autoUpdate.Checked = _settings.AutoInstallUpdates;
        var note = _updater.Describe(L);
        if (note.Length == 0)
            note = L.Pick("新しいバージョンは自動で確認し、ダウンロードします。", "New versions are checked for and downloaded automatically.");
        _updateState.Text = note;
        _updateButton.Text = st switch
        {
            UpdateState.Ready => L.Pick("再起動して更新", "Restart to update"),
            UpdateState.Available => L.Pick("ダウンロードページを開く", "Open download page"),
            _ => L.Pick("アップデートを確認", "Check for updates"),
        };
        _updateButton.Enabled = st is not (UpdateState.Checking or UpdateState.Downloading);
    }

    private static FlowLayoutPanel SliderRow(Label label, TrackBar track, Label value)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        label.Margin = new Padding(0, 8, 6, 0);
        value.Margin = new Padding(6, 8, 0, 0);
        row.Controls.AddRange(new Control[] {label, track, value});
        return row;
    }

    private void OnSettingsChanged(string _) { if (!IsDisposed) UpdateTexts(); }

    private void OnStatusChanged()
    {
        if (IsDisposed || !Visible) return;
        var values = FrameRateValues();
        if (!values.SequenceEqual(_fpsValues)) { UpdateTexts(); return; }
        UpdateFpsNow();
    }

    /// <summary>0（モニターに合わせる）＋モニターの上限までの選択肢。保存済みの値は上限を超えていても残す。</summary>
    private List<int> FrameRateValues()
    {
        int limit = Math.Max(60, _status.MaxRefreshRate);
        var list = AppSettings.FrameRateChoices.Where(v => v <= limit).ToList();
        if (_settings.FrameRate > 0 && !list.Contains(_settings.FrameRate)) { list.Add(_settings.FrameRate); list.Sort(); }
        list.Insert(0, 0);
        return list;
    }

    private void UpdateFpsNow()
    {
        var L = _settings.Language;
        _fpsNow.Text = _status.Running
            ? L.Pick($"いまの表示：{_status.MeasuredFps} fps", $"Currently: {_status.MeasuredFps} fps")
            : L.Pick("いまの表示：停止中", "Currently: stopped");
    }

    public void UpdateTexts()
    {
        var L = _settings.Language;
        _updating = true;
        SuspendLayout();
        Text = L.Pick("鈴の設定", "Suzu Settings");
        _fpsLabel.Text = L.Pick("フレームレート", "Frame rate");
        _fpsValues = FrameRateValues();
        _fps.Items.Clear();
        foreach (var v in _fpsValues)
            _fps.Items.Add(v == 0 ? L.Pick($"モニターに合わせる（{_status.RefreshRate}Hz）", $"Match monitor ({_status.RefreshRate} Hz)") : $"{v} fps");
        _fps.SelectedIndex = Math.Max(0, _fpsValues.IndexOf(_settings.FrameRate));
        UpdateFpsNow();
        _langHeader.Text = L.Pick("言語", "Language");
        _language.SelectedIndex = _settings.Language == AppLanguage.En ? 1 : 0;
        _appearanceHeader.Text = L.Pick("表示", "Appearance");
        _opacityLabel.Text = L.Pick("不透明度", "Opacity");
        _opacity.Value = Math.Clamp((int)Math.Round(_settings.Opacity * 20), _opacity.Minimum, _opacity.Maximum);
        _opacityValue.Text = $"{(int)Math.Round(_settings.Opacity * 100)}%";
        _onTop.Text = L.Pick("ほかのウィンドウより手前に表示", "Keep above other windows");
        _onTop.Checked = _settings.AlwaysOnTop;
        _sizeNote.Text = L.Pick("鈴の上で Ctrl キーを押しながらホイールを回しても大きさを変えられます。フレームレートは高いほど動きがなめらかになりますが、電力を少し多く使います。",
                                "You can also resize Suzu by turning the mouse wheel over Suzu while holding Ctrl. A higher frame rate makes movement smoother but uses a little more power.");
        _speedLabel.Text = L.Pick("動く速さ", "Movement speed");
        _speed.Value = Math.Clamp((int)Math.Round(_settings.MoveSpeed * 10), _speed.Minimum, _speed.Maximum);
        _speedValue.Text = "×" + _settings.MoveSpeed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        _reset.Text = L.Pick("見た目と行動の設定を初期値に戻す", "Reset appearance and behavior to defaults");
        _behaviorHeader.Text = L.Pick("行動", "Behavior");
        _interact.Text = L.Pick("ウィンドウの上に乗る・飛び降りる", "Climb onto and jump off windows");
        _interact.Checked = _settings.InteractWithWindows;
        _ballPlay.Text = L.Pick("ボール遊びをする（ときどき自分でボールを出して遊びます）", "Play with a ball (sometimes brings one out on her own)");
        _ballPlay.Checked = _settings.AllowBallPlay;
        _pause.Text = L.Pick("自律行動を一時停止", "Pause autonomous behavior");
        _pause.Checked = _settings.Paused;
        _tempHeader.Text = L.Pick("素材が揃うまでの暫定設定", "Temporary options until the assets are ready");
        _mirror.Text = L.Pick("左向きに右向き画像を反転して使う（仮）", "Mirror the right-facing image for the left (temporary)");
        _mirror.Checked = _settings.AllowMirroring;
        _placeholders.Text = L.Pick("素材が未完成の行動も試す（立ち姿＋吹き出しで代用）", "Try behaviors with unfinished assets (standing pose + bubble)");
        _placeholders.Checked = _settings.AllowPlaceholders;
        _tempNote.Text = L.Pick("反転すると髪飾りが反対側に移るため、デザイン資料とは異なります。既定ではオフです。",
                                "Mirroring moves the hair ornament to the other side, which differs from the character sheets. Off by default.");
        _startupHeader.Text = L.Pick("起動", "Startup");
        _login.Text = L.Pick("Windows へのサインイン時に起動", "Launch when you sign in to Windows");
        _login.Checked = _settings.LaunchAtLogin;
        _missingHeader.Text = L.Pick("不足している画像素材", "Missing image assets") + $" ({_missingAssets.Count})";
        _missing.Text = string.Join(L.Pick("、", ", "), _missingAssets);
        UpdateAppSection();
        ResumeLayout(true);
        _updating = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _settings.Changed -= OnSettingsChanged; _status.Changed -= OnStatusChanged; _updater.Changed -= OnUpdateChanged; }
        base.Dispose(disposing);
    }
}

/// <summary>会話画面。実際の応答は鈴秘書側が担う。</summary>
internal sealed class ConversationForm : SuzuForm
{
    private readonly AppSettings _settings;
    private readonly Label _banner = new()
    {
        Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(8),
        BackColor = Color.FromArgb(240, 244, 255), ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = true };
    private readonly TextBox _input = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
    private readonly Button _send = new() { AutoSize = true, Margin = Padding.Empty };
    private bool _connected;

    public Action<string>? OnSend { get; set; }

    public ConversationForm(AppSettings settings)
    {
        _settings = settings;
        ClientSize = new Size(360, 420);
        MinimumSize = new Size(340, 320);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 2, AutoSize = true, Padding = new Padding(10) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_input, 0, 0);
        bottom.Controls.Add(_send, 1, 0);
        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        logHost.Controls.Add(_log);

        Controls.Add(logHost);
        Controls.Add(bottom);
        Controls.Add(_banner);

        _send.Click += (_, _) => Send();
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Send(); }
        };
        _input.TextChanged += (_, _) => _send.Enabled = _input.Text.Trim().Length > 0;
        _settings.Changed += OnSettingsChanged;
        UpdateTexts();
    }

    public bool Connected
    {
        get => _connected;
        set { _connected = value; UpdateTexts(); }
    }

    private void OnSettingsChanged(string _) { if (!IsDisposed) UpdateTexts(); }

    public void UpdateTexts()
    {
        var L = _settings.Language;
        Text = L.Pick("鈴と話す", "Chat with Suzu");
        _banner.Text = L.Pick("鈴秘書はまだ接続されていません。接続すると、ここで鈴とお話しできます。",
                              "Suzu Secretary is not connected yet. Once it is, you can chat with Suzu here.");
        _banner.Visible = !_connected;
        _send.Text = L.Pick("送信", "Send");
        _send.Enabled = _input.Text.Trim().Length > 0;
        _input.PlaceholderText = L.Pick("鈴に話しかける…", "Say something to Suzu…");
    }

    public void Append(bool fromSuzu, string text)
    {
        var L = _settings.Language;
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionAlignment = fromSuzu ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _log.SelectionColor = fromSuzu ? Theme.Navy : Color.DimGray;
        _log.SelectionFont = Theme.UiFont(8.5f, FontStyle.Bold);
        _log.AppendText((fromSuzu ? L.Pick("鈴", "Suzu") : L.Pick("あなた", "You")) + "\n");
        _log.SelectionColor = Color.Black;
        _log.SelectionFont = Theme.UiFont(10f);
        _log.SelectionBackColor = fromSuzu ? Theme.SuzuBubble : Theme.UserBubble;
        _log.AppendText(text);
        _log.SelectionBackColor = Color.White;
        _log.AppendText("\n\n");
        _log.ScrollToCaret();
    }

    private void Send()
    {
        var t = _input.Text.Trim();
        if (t.Length == 0) return;
        Append(false, t);
        _input.Clear();
        OnSend?.Invoke(t);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _settings.Changed -= OnSettingsChanged;
        base.Dispose(disposing);
    }
}
