// 鈴秘書の代わりにイベントを送る開発用ツール。
// 例: swift tools/send_event.swift '{"type":"speak","text":"おつかれさま！"}'
import Foundation
let json = CommandLine.arguments.dropFirst().first ?? #"{"type":"speak","text":"こんにちは、鈴です。"}"#
DistributedNotificationCenter.default().postNotificationName(
    .init("jp.suzu.secretary.event"), object: json, userInfo: nil, deliverImmediately: true)
RunLoop.main.run(until: Date().addingTimeInterval(0.3))
print("sent:", json)
