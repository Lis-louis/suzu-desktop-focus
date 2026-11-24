import SwiftUI

/// コントロールパネルに表示する状態
final class MascotStatus: ObservableObject {
    @Published var running = true
    @Published var activityText = ""
    @Published var bridgeConnected = false
    /// 鈴のいるモニターのリフレッシュレート（Hz）
    @Published var refreshRate = 60
    /// つながっているモニターの中で最も高いリフレッシュレート（Hz）
    @Published var maxRefreshRate = 60
    /// 実際に描画できている回数（1 秒あたり）
    @Published var measuredFPS = 0
    /// ボールが出ている
    @Published var ballOut = false
    /// 設定画面が開いているか（開いているときだけ fps を更新する）
    var settingsVisible = false
}

/// 鈴の起動・停止などをまとめた操作画面
struct ControlPanelView: View {
    @ObservedObject var status: MascotStatus
    @ObservedObject var settings: AppSettings
    @ObservedObject var updater: Updater
    var onToggleRunning: () -> Void
    var onSummon: () -> Void
    var onToggleBall: () -> Void
    var onConversation: () -> Void
    var onSettings: () -> Void
    var onQuit: () -> Void

    private let accent = Color(red: 0.49, green: 0.58, blue: 0.85)
    private let navy = Color(red: 0.24, green: 0.29, blue: 0.56)

    var body: some View {
        let L = settings.language
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                Image(systemName: "bell.fill").font(.title2).foregroundStyle(accent)
                VStack(alignment: .leading, spacing: 2) {
                    Text(L.pick("鈴", "Suzu")).font(.title2.bold()).foregroundStyle(navy)
                    HStack(spacing: 6) {
                        Circle().fill(status.running ? Color.green : Color.gray).frame(width: 8, height: 8)
                        Text(status.running ? (settings.paused ? L.pick("表示中（一時停止）", "Visible (paused)") : L.pick("デスクトップで活動中", "Active on the desktop")) : L.pick("停止中", "Stopped"))
                            .font(.callout).foregroundStyle(.secondary)
                    }
                }
                Spacer()
            }

            if updater.state == .downloading || updater.state == .ready {
                UpdateBanner(updater: updater, settings: settings)
            }

            if status.running && !status.activityText.isEmpty {
                Text(L.pick("いまの様子：", "Now: ") + status.activityText).font(.callout).foregroundStyle(.secondary)
            }

            Button(action: onToggleRunning) {
                Label(status.running ? L.pick("鈴を止める", "Stop Suzu") : L.pick("鈴を起動する", "Start Suzu"),
                      systemImage: status.running ? "stop.fill" : "play.fill")
                    .frame(maxWidth: .infinity).padding(.vertical, 6)
            }
            .buttonStyle(.borderedProminent)
            .tint(status.running ? .gray : accent)
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)

            HStack {
                Button(L.pick("呼ぶ", "Call"), systemImage: "hand.wave", action: onSummon).disabled(!status.running)
                Button(status.ballOut ? L.pick("ボールをしまう", "Put Ball Away") : L.pick("ボール", "Ball"),
                       systemImage: "circle.circle", action: onToggleBall).disabled(!status.running)
                Button(L.pick("話す", "Chat"), systemImage: "bubble.left", action: onConversation)
                Button(L.pick("設定", "Settings"), systemImage: "gearshape", action: onSettings)
            }
            .frame(maxWidth: .infinity)

            Divider()
            ScaleSlider(settings: settings)
            Toggle(L.pick("自律行動を一時停止（その場でじっとする）", "Pause autonomous behavior (stay still)"), isOn: $settings.paused).disabled(!status.running)
            Toggle(L.pick("ログイン時に起動", "Launch at login"), isOn: $settings.launchAtLogin)
            Toggle(L.pick("アプリ起動時にこの画面を表示", "Show this panel when the app opens"), isOn: $settings.showControlPanelOnLaunch)

            Divider()
            HStack {
                Text(status.bridgeConnected ? L.pick("鈴秘書：接続中", "Suzu Secretary: connected") : L.pick("鈴秘書：未接続", "Suzu Secretary: not connected"))
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button(L.pick("アプリを終了", "Quit app"), role: .destructive, action: onQuit)
            }
        }
        .padding(20)
        .frame(width: 400)
    }
}

/// コントロールパネル上部の「新しいバージョン」のお知らせ
struct UpdateBanner: View {
    @ObservedObject var updater: Updater
    @ObservedObject var settings: AppSettings

    var body: some View {
        let L = settings.language
        HStack(spacing: 8) {
            Image(systemName: "arrow.down.circle.fill").foregroundStyle(Color(red: 0.49, green: 0.58, blue: 0.85))
            Text(updater.describe(L)).font(.callout).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            if updater.state == .ready {
                Button(L.pick("インストール…", "Install…")) { updater.install() }
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color(red: 0.86, green: 0.90, blue: 1.0).opacity(0.6)))
    }
}
