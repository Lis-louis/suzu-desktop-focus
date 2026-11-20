import SwiftUI

/// コントロールパネルに表示する状態
final class MascotStatus: ObservableObject {
    @Published var running = true
    @Published var activityText = ""
    @Published var bridgeConnected = false
}

/// 鈴の起動・停止などをまとめた操作画面
struct ControlPanelView: View {
    @ObservedObject var status: MascotStatus
    @ObservedObject var settings: AppSettings
    var onToggleRunning: () -> Void
    var onSummon: () -> Void
    var onConversation: () -> Void
    var onSettings: () -> Void
    var onQuit: () -> Void

    private let accent = Color(red: 0.49, green: 0.58, blue: 0.85)
    private let navy = Color(red: 0.24, green: 0.29, blue: 0.56)

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                Image(systemName: "bell.fill").font(.title2).foregroundStyle(accent)
                VStack(alignment: .leading, spacing: 2) {
                    Text("鈴").font(.title2.bold()).foregroundStyle(navy)
                    HStack(spacing: 6) {
                        Circle().fill(status.running ? Color.green : Color.gray).frame(width: 8, height: 8)
                        Text(status.running ? (settings.paused ? "表示中（一時停止）" : "デスクトップで活動中") : "停止中")
                            .font(.callout).foregroundStyle(.secondary)
                    }
                }
                Spacer()
            }

            if status.running && !status.activityText.isEmpty {
                Text("いまの様子：\(status.activityText)").font(.callout).foregroundStyle(.secondary)
            }

            Button(action: onToggleRunning) {
                Label(status.running ? "鈴を止める" : "鈴を起動する",
                      systemImage: status.running ? "stop.fill" : "play.fill")
                    .frame(maxWidth: .infinity).padding(.vertical, 6)
            }
            .buttonStyle(.borderedProminent)
            .tint(status.running ? .gray : accent)
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)

            HStack {
                Button("呼ぶ", systemImage: "hand.wave", action: onSummon).disabled(!status.running)
                Button("話す", systemImage: "bubble.left", action: onConversation)
                Button("設定", systemImage: "gearshape", action: onSettings)
            }
            .frame(maxWidth: .infinity)

            Divider()
            Toggle("自律行動を一時停止（その場でじっとする）", isOn: $settings.paused).disabled(!status.running)
            Toggle("ログイン時に起動", isOn: $settings.launchAtLogin)
            Toggle("アプリ起動時にこの画面を表示", isOn: $settings.showControlPanelOnLaunch)

            Divider()
            HStack {
                Text(status.bridgeConnected ? "鈴秘書：接続中" : "鈴秘書：未接続")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("アプリを終了", role: .destructive, action: onQuit)
            }
        }
        .padding(20)
        .frame(width: 360)
    }
}
