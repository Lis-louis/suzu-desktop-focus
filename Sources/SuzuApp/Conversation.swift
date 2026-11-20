import AppKit
import SwiftUI
import SuzuBridge

/// 会話画面のモデル。実際の応答は鈴秘書（SecretaryBridge）側が担う。
final class ConversationModel: ObservableObject {
    struct Message: Identifiable { let id = UUID(); let fromSuzu: Bool; let text: String }
    @Published var messages: [Message] = []
    @Published var draft = ""
    @Published var connected = false
    var onSend: ((String) -> Void)?

    func send() {
        let t = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else { return }
        messages.append(.init(fromSuzu: false, text: t))
        draft = ""
        onSend?(t)
    }
}

struct ConversationView: View {
    @ObservedObject var model: ConversationModel

    var body: some View {
        VStack(spacing: 0) {
            if !model.connected {
                Text("鈴秘書はまだ接続されていません。接続すると、ここで鈴とお話しできます。")
                    .font(.caption).foregroundStyle(.secondary)
                    .padding(8).frame(maxWidth: .infinity).background(Color.blue.opacity(0.06))
            }
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 8) {
                        ForEach(model.messages) { m in
                            HStack {
                                if !m.fromSuzu { Spacer() }
                                Text(m.text)
                                    .padding(.horizontal, 10).padding(.vertical, 6)
                                    .background(m.fromSuzu ? Color(red: 0.86, green: 0.9, blue: 1) : Color(white: 0.93))
                                    .clipShape(RoundedRectangle(cornerRadius: 10))
                                    .textSelection(.enabled)
                                if m.fromSuzu { Spacer() }
                            }.id(m.id)
                        }
                    }.padding(10)
                }
                .onChange(of: model.messages.count) { _, _ in
                    if let last = model.messages.last { proxy.scrollTo(last.id, anchor: .bottom) }
                }
            }
            Divider()
            HStack {
                TextField("鈴に話しかける…", text: $model.draft).textFieldStyle(.roundedBorder).onSubmit { model.send() }
                Button("送信") { model.send() }.disabled(model.draft.isEmpty)
            }.padding(10)
        }
        .frame(minWidth: 340, minHeight: 320)
    }
}

/// 鈴の頭上に一時的に表示するセリフの吹き出し（通知・会話用）
final class SpeechBubble {
    private let panel: NSPanel
    private let host: NSHostingView<BubbleText>
    private var hideWork: DispatchWorkItem?

    struct BubbleText: View {
        var text: String
        var body: some View {
            Text(text)
                .font(.system(size: 13))
                .foregroundStyle(Color(red: 0.24, green: 0.29, blue: 0.56))
                .padding(.horizontal, 12).padding(.vertical, 8)
                .background(RoundedRectangle(cornerRadius: 12).fill(.white.opacity(0.97)))
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color(red: 0.49, green: 0.58, blue: 0.85), lineWidth: 1.5))
                .padding(4)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    init() {
        host = NSHostingView(rootView: BubbleText(text: ""))
        host.sizingOptions = [] // ウィンドウサイズは自前で計算する
        panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.ignoresMouseEvents = true
        panel.canHide = false
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        panel.contentView = host
    }

    var isVisible: Bool { panel.isVisible }

    func show(_ text: String, above anchor: NSRect, seconds: TimeInterval) {
        host.rootView = BubbleText(text: text)
        // 本文の大きさ＋余白（左右 12+4、上下 8+4）
        let textRect = (text as NSString).boundingRect(with: NSSize(width: 236, height: 1000),
                                                       options: [.usesLineFragmentOrigin, .usesFontLeading],
                                                       attributes: [.font: NSFont.systemFont(ofSize: 13)])
        let size = NSSize(width: ceil(textRect.width) + 34, height: ceil(textRect.height) + 26)
        panel.setContentSize(size)
        host.frame = NSRect(origin: .zero, size: size)
        reposition(above: anchor)
        panel.orderFrontRegardless()
        hideWork?.cancel()
        let w = DispatchWorkItem { [weak self] in self?.panel.orderOut(nil) }
        hideWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: w)
    }

    func reposition(above anchor: NSRect) {
        guard panel.isVisible || panel.frame.size != .zero else { return }
        var origin = NSPoint(x: anchor.midX - panel.frame.width / 2, y: anchor.maxY + 4)
        if let s = NSScreen.screens.first(where: { $0.frame.contains(NSPoint(x: anchor.midX, y: anchor.minY + 1)) }) {
            origin.x = min(max(origin.x, s.visibleFrame.minX), s.visibleFrame.maxX - panel.frame.width)
            origin.y = min(origin.y, s.visibleFrame.maxY - panel.frame.height)
        }
        panel.setFrameOrigin(origin)
    }

    func close() { hideWork?.cancel(); panel.orderOut(nil) }
}
