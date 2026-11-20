import AppKit

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
// Dock に表示する（Dock のアイコンをクリックするとコントロールパネルが開く）
app.setActivationPolicy(.regular)
app.run()
