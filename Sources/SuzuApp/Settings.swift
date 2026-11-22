import Foundation
import ServiceManagement
import SwiftUI

/// ユーザー設定（UserDefaults に保存）
final class AppSettings: ObservableObject {
    private let d = UserDefaults.standard

    /// 鈴の大きさ（1.0 = 標準）。範囲外や半端な値は丸める。
    @Published var scale: Double {
        didSet {
            let v = Self.clampScale(scale)
            if v != scale { scale = v; return }
            d.set(scale, forKey: "scale")
        }
    }
    /// 鈴の不透明度（1.0 = 不透明）
    @Published var opacity: Double {
        didSet {
            let v = min(max((opacity * 20).rounded() / 20, Self.opacityRange.lowerBound), Self.opacityRange.upperBound)
            if v != opacity { opacity = v; return }
            d.set(opacity, forKey: "opacity")
        }
    }
    /// ほかのウィンドウより手前に表示する
    @Published var alwaysOnTop: Bool { didSet { d.set(alwaysOnTop, forKey: "alwaysOnTop") } }
    /// 描画の上限フレームレート（0 = モニターのリフレッシュレートに合わせる）
    @Published var frameRate: Int { didSet { d.set(frameRate, forKey: "frameRate") } }
    /// 歩く・登る速さの倍率（1.0 = 標準）
    @Published var moveSpeed: Double {
        didSet {
            let v = min(max((moveSpeed * 10).rounded() / 10, Self.speedRange.lowerBound), Self.speedRange.upperBound)
            if v != moveSpeed { moveSpeed = v; return }
            d.set(moveSpeed, forKey: "moveSpeed")
        }
    }
    @Published var interactWithWindows: Bool { didSet { d.set(interactWithWindows, forKey: "interactWithWindows") } }
    @Published var paused: Bool { didSet { d.set(paused, forKey: "paused") } }
    /// 左向きの素材がないため、右向きを反転して代用する（髪飾りの位置が逆になる）
    @Published var allowMirroring: Bool { didSet { d.set(allowMirroring, forKey: "allowMirroring") } }
    /// 素材が未完成の行動（座る・眠る・登る等）を立ち姿＋吹き出しで試す
    @Published var allowPlaceholders: Bool { didSet { d.set(allowPlaceholders, forKey: "allowPlaceholders") } }
    /// 鈴をデスクトップに表示して動かすか（コントロールパネルの起動/停止）
    @Published var mascotRunning: Bool { didSet { d.set(mascotRunning, forKey: "mascotRunning") } }
    @Published var showControlPanelOnLaunch: Bool { didSet { d.set(showControlPanelOnLaunch, forKey: "showControlPanelOnLaunch") } }
    /// 表示言語（既定は日本語）
    @Published var language: AppLanguage { didSet { d.set(language.rawValue, forKey: "language") } }
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
    static let scaleRange: ClosedRange<Double> = 0.4...2.0
    static let scaleStep: Double = 0.05
    /// メニューから選べる大きさ
    static let scalePresets: [Double] = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0]
    static let opacityRange: ClosedRange<Double> = 0.3...1.0
    static let speedRange: ClosedRange<Double> = 0.5...2.0
    /// 選べるフレームレート（モニターの上限を超えるものは画面に出さない）
    static let frameRateChoices = [30, 60, 90, 120, 144, 165, 180, 240]

    static func clampScale(_ v: Double) -> Double {
        min(max((v / scaleStep).rounded() * scaleStep, scaleRange.lowerBound), scaleRange.upperBound)
    }

    /// 大きさを 1 段階ずつ変える（steps が正なら大きく）
    func adjustScale(by steps: Int) { scale = Self.clampScale(scale + Double(steps) * Self.scaleStep) }

    /// 見た目・行動の設定を初期値に戻す（言語・起動・位置はそのまま）
    func resetToDefaults() {
        scale = 1.0
        opacity = 1.0
        alwaysOnTop = true
        moveSpeed = 1.0
        frameRate = 0
        interactWithWindows = true
        paused = false
        allowMirroring = false
        allowPlaceholders = false
    }

    init() {
        d.register(defaults: ["scale": 1.0, "opacity": 1.0, "alwaysOnTop": true, "moveSpeed": 1.0, "frameRate": 0, "interactWithWindows": true, "paused": false,
                              "allowMirroring": false, "allowPlaceholders": false,
                              "mascotRunning": true, "showControlPanelOnLaunch": true, "language": "ja"])
        scale = Self.clampScale(d.double(forKey: "scale"))
        opacity = d.double(forKey: "opacity")
        alwaysOnTop = d.bool(forKey: "alwaysOnTop")
        moveSpeed = d.double(forKey: "moveSpeed")
        frameRate = max(0, d.integer(forKey: "frameRate"))
        interactWithWindows = d.bool(forKey: "interactWithWindows")
        paused = d.bool(forKey: "paused")
        allowMirroring = d.bool(forKey: "allowMirroring")
        allowPlaceholders = d.bool(forKey: "allowPlaceholders")
        mascotRunning = d.bool(forKey: "mascotRunning")
        showControlPanelOnLaunch = d.bool(forKey: "showControlPanelOnLaunch")
        language = AppLanguage(rawValue: d.string(forKey: "language") ?? "ja") ?? .ja
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
    @ObservedObject var status: MascotStatus
    let missingAssets: [String]

    var body: some View {
        let L = settings.language
        Form {
            Section(L.pick("言語", "Language")) {
                Picker(L.pick("表示言語", "Display language"), selection: $settings.language) {
                    ForEach(AppLanguage.allCases) { Text($0.displayName).tag($0) }
                }
            }
            Section {
                ScaleSlider(settings: settings)
                HStack {
                    Text(L.pick("不透明度", "Opacity"))
                    Slider(value: $settings.opacity, in: AppSettings.opacityRange, step: 0.05)
                    Text("\(Int((settings.opacity * 100).rounded()))%").monospacedDigit().frame(width: 44, alignment: .trailing)
                }
                Toggle(L.pick("ほかのウィンドウより手前に表示", "Keep above other windows"), isOn: $settings.alwaysOnTop)
                Picker(L.pick("フレームレート", "Frame rate"), selection: $settings.frameRate) {
                    Text(L.pick("モニターに合わせる（\(status.refreshRate)Hz）", "Match monitor (\(status.refreshRate) Hz)")).tag(0)
                    ForEach(frameRateChoices, id: \.self) { Text("\($0) fps").tag($0) }
                }
                LabeledContent(L.pick("いまの表示", "Currently")) {
                    Text(status.running
                         ? L.pick("\(status.measuredFPS) fps（鈴のいるモニター：\(status.refreshRate)Hz）", "\(status.measuredFPS) fps (Suzu's monitor: \(status.refreshRate) Hz)")
                         : L.pick("停止中", "Stopped"))
                        .monospacedDigit().foregroundStyle(.secondary)
                }
            } header: {
                Text(L.pick("表示", "Appearance"))
            } footer: {
                Text(L.pick("鈴の上で ⌘ を押しながらスクロールしても大きさを変えられます。フレームレートは高いほど動きがなめらかになりますが、電力を少し多く使います。",
                            "You can also resize Suzu by scrolling over Suzu while holding ⌘. A higher frame rate makes movement smoother but uses a little more power."))
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section(L.pick("行動", "Behavior")) {
                Toggle(L.pick("ウィンドウの上に乗る・飛び降りる", "Climb onto and jump off windows"), isOn: $settings.interactWithWindows)
                Toggle(L.pick("自律行動を一時停止", "Pause autonomous behavior"), isOn: $settings.paused)
                HStack {
                    Text(L.pick("動く速さ", "Movement speed"))
                    Slider(value: $settings.moveSpeed, in: AppSettings.speedRange, step: 0.1)
                    Text(String(format: "×%.1f", settings.moveSpeed)).monospacedDigit().frame(width: 44, alignment: .trailing)
                }
            }
            Section {
                Toggle(L.pick("左向きに右向き画像を反転して使う（仮）", "Mirror the right-facing image for the left (temporary)"), isOn: $settings.allowMirroring)
                Toggle(L.pick("素材が未完成の行動も試す（立ち姿＋吹き出しで代用）", "Try behaviors with unfinished assets (standing pose + bubble)"), isOn: $settings.allowPlaceholders)
            } header: {
                Text(L.pick("素材が揃うまでの暫定設定", "Temporary options until the assets are ready"))
            } footer: {
                Text(L.pick("反転すると髪飾りが反対側に移るため、デザイン資料とは異なります。既定ではオフです。",
                            "Mirroring moves the hair ornament to the other side, which differs from the character sheets. Off by default."))
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section(L.pick("起動", "Startup")) {
                Toggle(L.pick("ログイン時に起動", "Launch at login"), isOn: $settings.launchAtLogin)
            }
            Section {
                HStack {
                    Spacer()
                    Button(L.pick("見た目と行動の設定を初期値に戻す", "Reset appearance and behavior to defaults")) { settings.resetToDefaults() }
                }
            }
            if !missingAssets.isEmpty {
                Section(L.pick("不足している画像素材", "Missing image assets") + " (\(missingAssets.count))") {
                    Text(missingAssets.joined(separator: L.pick("、", ", "))).font(.caption).foregroundStyle(.secondary)
                        .textSelection(.enabled)
                }
            }
        }
        .formStyle(.grouped)
        .onAppear { status.settingsVisible = true }
        .onDisappear { status.settingsVisible = false }
        .frame(width: 440)
        .padding(.vertical, 8)
    }
}

extension SettingsView {
    /// モニターの上限までの選択肢（保存済みの値は上限を超えていても残す）
    var frameRateChoices: [Int] {
        let limit = max(60, status.maxRefreshRate)
        var list = AppSettings.frameRateChoices.filter { $0 <= limit }
        if settings.frameRate > 0, !list.contains(settings.frameRate) { list.append(settings.frameRate); list.sort() }
        return list
    }
}

/// 大きさのスライダー（設定画面・コントロールパネル共通）
struct ScaleSlider: View {
    @ObservedObject var settings: AppSettings

    var body: some View {
        let L = settings.language
        HStack {
            Text(L.pick("大きさ", "Size"))
            Button { settings.adjustScale(by: -1) } label: { Image(systemName: "minus") }
                .buttonStyle(.borderless).disabled(settings.scale <= AppSettings.scaleRange.lowerBound)
                .help(L.pick("小さくする", "Smaller"))
            Slider(value: $settings.scale, in: AppSettings.scaleRange, step: AppSettings.scaleStep)
            Button { settings.adjustScale(by: 1) } label: { Image(systemName: "plus") }
                .buttonStyle(.borderless).disabled(settings.scale >= AppSettings.scaleRange.upperBound)
                .help(L.pick("大きくする", "Larger"))
            Text("\(Int((settings.scale * 100).rounded()))%").monospacedDigit().frame(width: 44, alignment: .trailing)
        }
        .contextMenu {
            Button(L.pick("標準の大きさに戻す", "Reset to standard size")) { settings.scale = 1.0 }
        }
    }
}
