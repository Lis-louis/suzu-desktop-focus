import Foundation
import ServiceManagement
import SwiftUI

/// ユーザー設定（UserDefaults に保存）
final class AppSettings: ObservableObject {
    private let d = UserDefaults.standard

    @Published var scale: Double { didSet { d.set(scale, forKey: "scale") } }
    @Published var interactWithWindows: Bool { didSet { d.set(interactWithWindows, forKey: "interactWithWindows") } }
    @Published var paused: Bool { didSet { d.set(paused, forKey: "paused") } }
    /// 左向きの素材がないため、右向きを反転して代用する（髪飾りの位置が逆になる）
    @Published var allowMirroring: Bool { didSet { d.set(allowMirroring, forKey: "allowMirroring") } }
    /// 素材が未完成の行動（座る・眠る・登る等）を立ち姿＋吹き出しで試す
    @Published var allowPlaceholders: Bool { didSet { d.set(allowPlaceholders, forKey: "allowPlaceholders") } }
    /// 鈴をデスクトップに表示して動かすか（コントロールパネルの起動/停止）
    @Published var mascotRunning: Bool { didSet { d.set(mascotRunning, forKey: "mascotRunning") } }
    @Published var showControlPanelOnLaunch: Bool { didSet { d.set(showControlPanelOnLaunch, forKey: "showControlPanelOnLaunch") } }
    @Published var launchAtLogin: Bool {
        didSet {
            guard launchAtLogin != oldValue else { return }
            do {
                if launchAtLogin { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            } catch {
                NSLog("[Suzu] ログイン項目の変更に失敗: \(error.localizedDescription)（.app として起動している必要があります）")
            }
        }
    }

    static let baseBodyHeight: Double = 180

    init() {
        d.register(defaults: ["scale": 1.0, "interactWithWindows": true, "paused": false,
                              "allowMirroring": false, "allowPlaceholders": false,
                              "mascotRunning": true, "showControlPanelOnLaunch": true])
        scale = d.double(forKey: "scale")
        interactWithWindows = d.bool(forKey: "interactWithWindows")
        paused = d.bool(forKey: "paused")
        allowMirroring = d.bool(forKey: "allowMirroring")
        allowPlaceholders = d.bool(forKey: "allowPlaceholders")
        mascotRunning = d.bool(forKey: "mascotRunning")
        showControlPanelOnLaunch = d.bool(forKey: "showControlPanelOnLaunch")
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }

    var bodyHeight: CGFloat { CGFloat(Self.baseBodyHeight * scale) }

    var savedPosition: CGPoint? {
        get {
            guard let a = d.array(forKey: "position") as? [Double], a.count == 2 else { return nil }
            return CGPoint(x: a[0], y: a[1])
        }
        set { d.set(newValue.map { [Double($0.x), Double($0.y)] }, forKey: "position") }
    }
}

struct SettingsView: View {
    @ObservedObject var settings: AppSettings
    let missingAssets: [String]

    var body: some View {
        Form {
            Section("表示") {
                HStack {
                    Text("大きさ")
                    Slider(value: $settings.scale, in: 0.6...1.6, step: 0.1)
                    Text("\(Int(settings.scale * 100))%").monospacedDigit().frame(width: 44, alignment: .trailing)
                }
            }
            Section("行動") {
                Toggle("ウィンドウの上に乗る・飛び降りる", isOn: $settings.interactWithWindows)
                Toggle("自律行動を一時停止", isOn: $settings.paused)
            }
            Section {
                Toggle("左向きに右向き画像を反転して使う（仮）", isOn: $settings.allowMirroring)
                Toggle("素材が未完成の行動も試す（立ち姿＋吹き出しで代用）", isOn: $settings.allowPlaceholders)
            } header: {
                Text("素材が揃うまでの暫定設定")
            } footer: {
                Text("反転すると髪飾りが反対側に移るため、デザイン資料とは異なります。既定ではオフです。")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("起動") {
                Toggle("ログイン時に起動", isOn: $settings.launchAtLogin)
            }
            if !missingAssets.isEmpty {
                Section("不足している画像素材 (\(missingAssets.count))") {
                    Text(missingAssets.joined(separator: "、")).font(.caption).foregroundStyle(.secondary)
                        .textSelection(.enabled)
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 440)
        .padding(.vertical, 8)
    }
}
