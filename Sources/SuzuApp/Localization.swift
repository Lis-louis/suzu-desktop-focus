import Foundation
import SuzuCore

/// アプリの表示言語。設定画面から切り替えられる（既定は日本語）。
enum AppLanguage: String, CaseIterable, Identifiable {
    case ja, en

    var id: String { rawValue }

    /// 選択肢としての表示名（どちらの言語でも読めるよう自言語で表記）
    var displayName: String {
        switch self {
        case .ja: "日本語"
        case .en: "English"
        }
    }

    /// 日本語と英語の文言から、現在の言語のものを選ぶ
    func pick(_ ja: String, _ en: String) -> String {
        self == .ja ? ja : en
    }

    /// 鈴の頭上に出す記号の表示文字列
    func emoteText(_ e: Emote) -> String? {
        switch e {
        case .none: nil
        case .stretch: pick("ん〜", "Mmm~")
        default: e.rawValue
        }
    }

    /// コントロールパネルに出す「いまの様子」
    func activityText(_ a: Activity) -> String {
        switch a {
        case .idle: pick("のんびり立っている", "Standing around")
        case .walk: pick("お散歩中", "Taking a walk")
        case .lookAround: pick("きょろきょろしている", "Looking around")
        case .turnAway: pick("あっちを見ている", "Looking the other way")
        case .doze: pick("うとうとしている", "Dozing off")
        case .sit: pick("座っている", "Sitting")
        case .sleep: pick("眠っている", "Sleeping")
        case .stretch: pick("伸びをしている", "Stretching")
        case .joy: pick("ごきげん", "In a good mood")
        case .goToEdge: pick("画面の端へ向かっている", "Heading to the screen edge")
        case .jumpDown: pick("飛び降りようとしている", "About to jump down")
        case .climb: pick("よじ登っている", "Climbing")
        case .hang: pick("ぶら下がっている", "Hanging")
        case .crossScreen: pick("隣の画面へお出かけ中", "Heading over to the other screen")
        case .playBall: pick("ボールで遊んでいる", "Playing with a ball")
        case .react: pick("反応している", "Reacting")
        case .dragged: pick("つままれている", "Being carried")
        case .falling: pick("落ちている", "Falling")
        }
    }
}
