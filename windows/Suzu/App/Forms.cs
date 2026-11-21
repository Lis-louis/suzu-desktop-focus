using Suzu.Core;

namespace Suzu.App;

/// <summary>コントロールパネルに表示する状態</summary>
internal sealed class MascotStatus
{
    public bool Running { get; set; } = true;
    public string ActivityText { get; set; } = "";
    public bool BridgeConnected { get; set; }
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
    private readonly CheckBox _pause = MakeCheck();
    private readonly CheckBox _login = MakeCheck();
    private readonly CheckBox _showOnLaunch = MakeCheck();
    private readonly Label _bridge = MakeLabel(8.5f);
    private readonly Button _quit = new() { AutoSize = true, ForeColor = Color.FromArgb(200, 40, 40) };
    private bool _updating;

    public ControlPanelForm(AppSettings settings, MascotStatus status,
                            Action onToggleRunning, Action onSummon, Action onConversation, Action onSettings, Action onQuit)
    {
        _settings = settings;
        _status = status;
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
        buttons.Controls.AddRange(new Control[] {_summon, _chat, _settingsButton});

        var footer = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bridge.Anchor = AnchorStyles.Left;
        footer.Controls.Add(_bridge, 0, 0);
        footer.Controls.Add(_quit, 1, 0);

        col.Controls.AddRange(new Control[] {header, _now, _toggle, buttons, Divider(), _pause, _login, _showOnLaunch, Divider(), footer});
        Controls.Add(col);

        _toggle.Click += (_, _) => onToggleRunning();
        _summon.Click += (_, _) => onSummon();
        _chat.Click += (_, _) => onConversation();
        _settingsButton.Click += (_, _) => onSettings();
        _quit.Click += (_, _) => onQuit();
        _pause.CheckedChanged += (_, _) => { if (!_updating) _settings.Paused = _pause.Checked; };
        _login.CheckedChanged += (_, _) => { if (!_updating) _settings.LaunchAtLogin = _login.Checked; };
        _showOnLaunch.CheckedChanged += (_, _) => { if (!_updating) _settings.ShowControlPanelOnLaunch = _showOnLaunch.Checked; };
        AcceptButton = _toggle;

        _status.Changed += UpdateTexts;
        _settings.Changed += OnSettingsChanged;
        UpdateTexts();
    }

    private void OnSettingsChanged(string _) { if (!IsDisposed) UpdateTexts(); }

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
        if (disposing) { _status.Changed -= UpdateTexts; _settings.Changed -= OnSettingsChanged; }
        base.Dispose(disposing);
    }
}

/// <summary>設定画面</summary>
internal sealed class SettingsForm : SuzuForm
{
    private readonly AppSettings _settings;
    private readonly List<string> _missingAssets;
    private readonly Label _langHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Label _appearanceHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly Label _sizeLabel = MakeLabel();
    private readonly TrackBar _size = new() { Minimum = 6, Maximum = 16, TickFrequency = 1, SmallChange = 1, LargeChange = 2, Width = 240, AutoSize = false, Height = 32 };
    private readonly Label _sizeValue = MakeLabel();
    private readonly Label _behaviorHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly CheckBox _interact = MakeCheck();
    private readonly CheckBox _pause = MakeCheck();
    private readonly Label _tempHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly CheckBox _mirror = MakeCheck();
    private readonly CheckBox _placeholders = MakeCheck();
    private readonly Label _tempNote = MakeLabel(8.5f);
    private readonly Label _startupHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly CheckBox _login = MakeCheck();
    private readonly Label _missingHeader = MakeLabel(9.5f, FontStyle.Bold, Theme.Navy);
    private readonly TextBox _missing = new() { ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Width = 420, Height = 48, ForeColor = Color.Gray };
    private bool _updating;

    public SettingsForm(AppSettings settings, List<string> missingAssets)
    {
        _settings = settings;
        _missingAssets = missingAssets;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _language.Items.AddRange(new object[] { AppLanguage.Ja.DisplayName(), AppLanguage.En.DisplayName() });
        _tempNote.MaximumSize = new Size(420, 0);

        var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _sizeLabel.Margin = new Padding(0, 8, 6, 0);
        _sizeValue.Margin = new Padding(6, 8, 0, 0);
        sizeRow.Controls.AddRange(new Control[] {_sizeLabel, _size, _sizeValue});

        var col = MakeColumn();
        col.MinimumSize = new Size(460, 0);
        col.Controls.AddRange(new Control[] {
            _langHeader, _language, Divider(),
            _appearanceHeader, sizeRow, Divider(),
            _behaviorHeader, _interact, _pause, Divider(),
            _tempHeader, _mirror, _placeholders, _tempNote, Divider(),
            _startupHeader, _login,
        });
        if (_missingAssets.Count > 0) col.Controls.AddRange(new Control[] {Divider(), _missingHeader, _missing});
        Controls.Add(col);

        _language.SelectedIndexChanged += (_, _) => { if (!_updating) _settings.Language = _language.SelectedIndex == 1 ? AppLanguage.En : AppLanguage.Ja; };
        _size.ValueChanged += (_, _) => { if (!_updating) _settings.Scale = _size.Value / 10.0; };
        _interact.CheckedChanged += (_, _) => { if (!_updating) _settings.InteractWithWindows = _interact.Checked; };
        _pause.CheckedChanged += (_, _) => { if (!_updating) _settings.Paused = _pause.Checked; };
        _mirror.CheckedChanged += (_, _) => { if (!_updating) _settings.AllowMirroring = _mirror.Checked; };
        _placeholders.CheckedChanged += (_, _) => { if (!_updating) _settings.AllowPlaceholders = _placeholders.Checked; };
        _login.CheckedChanged += (_, _) => { if (!_updating) _settings.LaunchAtLogin = _login.Checked; };

        _settings.Changed += OnSettingsChanged;
        UpdateTexts();
    }

    private void OnSettingsChanged(string _) { if (!IsDisposed) UpdateTexts(); }

    public void UpdateTexts()
    {
        var L = _settings.Language;
        _updating = true;
        SuspendLayout();
        Text = L.Pick("鈴の設定", "Suzu Settings");
        _langHeader.Text = L.Pick("言語", "Language");
        _language.SelectedIndex = _settings.Language == AppLanguage.En ? 1 : 0;
        _appearanceHeader.Text = L.Pick("表示", "Appearance");
        _sizeLabel.Text = L.Pick("大きさ", "Size");
        _size.Value = Math.Clamp((int)Math.Round(_settings.Scale * 10), _size.Minimum, _size.Maximum);
        _sizeValue.Text = $"{(int)Math.Round(_settings.Scale * 100)}%";
        _behaviorHeader.Text = L.Pick("行動", "Behavior");
        _interact.Text = L.Pick("ウィンドウの上に乗る・飛び降りる", "Climb onto and jump off windows");
        _interact.Checked = _settings.InteractWithWindows;
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
        if (disposing) _settings.Changed -= OnSettingsChanged;
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
