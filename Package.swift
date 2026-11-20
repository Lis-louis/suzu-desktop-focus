// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "SuzuDesktop",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "Suzu", targets: ["SuzuApp"]),
        .library(name: "SuzuCore", targets: ["SuzuCore"]),
        .library(name: "SuzuBridge", targets: ["SuzuBridge"]),
    ],
    targets: [
        // 行動・物理・地形・スプライト解決などの純粋ロジック（AppKit 非依存・テスト対象）
        .target(name: "SuzuCore"),
        // 鈴秘書との連携インターフェース（将来拡張用）
        .target(name: "SuzuBridge", dependencies: ["SuzuCore"]),
        // AppKit/SwiftUI によるデスクトップ表示層
        .executableTarget(name: "SuzuApp", dependencies: ["SuzuCore", "SuzuBridge"]),
        .testTarget(name: "SuzuCoreTests", dependencies: ["SuzuCore", "SuzuBridge"]),
    ],
    swiftLanguageModes: [.v5]
)
