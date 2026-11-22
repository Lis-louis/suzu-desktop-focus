import AppKit
import CoreGraphics
import SuzuCore

/// 画面とウィンドウの配置を取得して Terrain を作る。
/// CGWindowListCopyWindowInfo のウィンドウ位置（bounds）は画面収録の権限なしで取得できる（名前は取得しない）。
final class EnvironmentSensor {
    var interactWithWindows = true
    var headroom: CGFloat = 200
    private let ownPID = ProcessInfo.processInfo.processIdentifier

    func screens() -> [ScreenInfo] {
        NSScreen.screens.map { s in
            let id = (s.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
            return ScreenInfo(id: id, visibleFrame: s.visibleFrame, frame: s.frame)
        }
    }

    /// 前面から背面の順で、通常レベルのウィンドウのみ
    func windows() -> [WindowInfo] {
        guard interactWithWindows,
              let primary = NSScreen.screens.first,
              let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]]
        else { return [] }
        let primaryHeight = primary.frame.height
        return list.compactMap { info in
            guard (info[kCGWindowLayer as String] as? Int) == 0,
                  (info[kCGWindowOwnerPID as String] as? Int32) != ownPID,
                  (info[kCGWindowAlpha as String] as? Double ?? 1) > 0.05,
                  let id = info[kCGWindowNumber as String] as? UInt32,
                  let dict = info[kCGWindowBounds as String] as? NSDictionary,
                  let r = CGRect(dictionaryRepresentation: dict),
                  r.width >= 160, r.height >= 100
            else { return nil }
            // 左上原点（CG）→ 左下原点（Cocoa）
            let cocoa = CGRect(x: r.minX, y: primaryHeight - r.maxY, width: r.width, height: r.height)
            return WindowInfo(id: id, frame: cocoa)
        }
    }

    func terrain() -> Terrain {
        Terrain.build(screens: screens(), windows: windows(), headroom: headroom)
    }
}
