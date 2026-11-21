#!/bin/zsh
# Suzu.app を build/ に作成する。使い方: scripts/build_app.sh [debug|release]
#   UNIVERSAL=1 scripts/build_app.sh   # Apple Silicon / Intel 両対応（Xcode が必要）
set -euo pipefail
cd "$(dirname "$0")/.."
CONFIG=${1:-release}
VERSION=$(tr -d '[:space:]' < VERSION)
ARCH_ARGS=()
if [[ "${UNIVERSAL:-0}" == "1" ]]; then ARCH_ARGS=(--arch arm64 --arch x86_64); fi
swift build -c "$CONFIG" "${ARCH_ARGS[@]}"
BIN="$(swift build -c "$CONFIG" "${ARCH_ARGS[@]}" --show-bin-path)/Suzu"
APP=build/Suzu.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Suzu"
cp -R Resources/Sprites "$APP/Contents/Resources/Sprites"
cp Resources/AppIcon/Suzu.icns "$APP/Contents/Resources/Suzu.icns"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>jp.suzu.desktop</string>
  <key>CFBundleName</key><string>Suzu</string>
  <key>CFBundleDisplayName</key><string>鈴</string>
  <key>CFBundleExecutable</key><string>Suzu</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>CFBundleIconFile</key><string>Suzu</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
codesign --force --sign - "$APP"
echo "built: $APP"
