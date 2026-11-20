import Foundation
import SuzuCore

/// 鈴秘書 → マスコットへのイベント
public enum SecretaryEvent: Equatable, Sendable {
    /// ユーザーへの通知（吹き出しで表示）
    case notify(title: String, body: String)
    /// 会話内容に応じた表情（例: "joy", "surprise", "troubled"）
    case expression(Pose, duration: Double)
    /// 作業状況のヒント（例: 集中中は静かにする）
    case activity(ActivityHint)
    /// 鈴秘書が話し始めた
    case speak(text: String)
}

public enum ActivityHint: String, Codable, Sendable {
    case userFocused, userIdle, userAway, meeting, breakTime
}

/// マスコット → 鈴秘書へのイベント
public enum MascotEvent: Equatable, Sendable {
    case userClicked
    case conversationRequested
    case userMessage(String)
}

/// 連携方式に依存しない境界。実装を差し替えるだけで XPC / WebSocket / ローカル HTTP 等に移行できる。
public protocol SecretaryBridge: AnyObject {
    var isConnected: Bool { get }
    /// メインスレッドで呼ばれる
    var onEvent: ((SecretaryEvent) -> Void)? { get set }
    func start()
    func stop()
    func send(_ event: MascotEvent)
}

/// 未接続時の実装
public final class NullSecretaryBridge: SecretaryBridge {
    public init() {}
    public var isConnected: Bool { false }
    public var onEvent: ((SecretaryEvent) -> Void)?
    public func start() {}
    public func stop() {}
    public func send(_ event: MascotEvent) {}
}

/// 同一マシン上のプロセス間で使える、最小の連携実装（DistributedNotificationCenter）。
/// 鈴秘書側は "jp.suzu.secretary.event" に JSON 文字列を object として投稿する:
///   {"type":"notify","title":"…","body":"…"} / {"type":"expression","pose":"joy","duration":3}
///   {"type":"activity","hint":"userFocused"} / {"type":"speak","text":"…"}
/// マスコット側からは "jp.suzu.mascot.event" に {"type":"click"} などを投稿する。
public final class DistributedNotificationBridge: SecretaryBridge {
    public static let inbound = Notification.Name("jp.suzu.secretary.event")
    public static let outbound = Notification.Name("jp.suzu.mascot.event")

    public var onEvent: ((SecretaryEvent) -> Void)?
    public private(set) var isConnected = false
    private var observer: NSObjectProtocol?
    private let center = DistributedNotificationCenter.default()

    public init() {}

    public func start() {
        guard observer == nil else { return }
        observer = center.addObserver(forName: Self.inbound, object: nil, queue: .main) { [weak self] note in
            guard let json = note.object as? String, let e = Self.decode(json) else { return }
            self?.isConnected = true
            self?.onEvent?(e)
        }
    }

    public func stop() {
        if let o = observer { center.removeObserver(o) }
        observer = nil
        isConnected = false
    }

    public func send(_ event: MascotEvent) {
        let dict: [String: String]
        switch event {
        case .userClicked: dict = ["type": "click"]
        case .conversationRequested: dict = ["type": "conversation"]
        case .userMessage(let t): dict = ["type": "message", "text": t]
        }
        guard let data = try? JSONSerialization.data(withJSONObject: dict), let s = String(data: data, encoding: .utf8) else { return }
        center.postNotificationName(Self.outbound, object: s, userInfo: nil, deliverImmediately: true)
    }

    public static func decode(_ json: String) -> SecretaryEvent? {
        guard let data = json.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let type = obj["type"] as? String else { return nil }
        switch type {
        case "notify":
            return .notify(title: obj["title"] as? String ?? "", body: obj["body"] as? String ?? "")
        case "expression":
            guard let p = (obj["pose"] as? String).flatMap(Pose.init(rawValue:)) else { return nil }
            return .expression(p, duration: obj["duration"] as? Double ?? 3)
        case "activity":
            guard let h = (obj["hint"] as? String).flatMap(ActivityHint.init(rawValue:)) else { return nil }
            return .activity(h)
        case "speak":
            return .speak(text: obj["text"] as? String ?? "")
        default:
            return nil
        }
    }
}
