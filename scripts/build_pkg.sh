#!/bin/zsh
# macOS 用インストーラー（build/Suzu-<version>.pkg）を作る。
#
# 使い方:
#   scripts/build_pkg.sh                 # Apple Silicon / Intel 両対応（Universal）でビルド
#   UNIVERSAL=0 scripts/build_pkg.sh     # この Mac のアーキテクチャだけでビルド（速い）
#
# 配布用に署名・公証する場合（Apple Developer Program のアカウントが必要）:
#   APP_SIGN_IDENTITY="Developer ID Application: ..." \
#   PKG_SIGN_IDENTITY="Developer ID Installer: ..." \
#   NOTARY_PROFILE="notarytool に保存したプロファイル名" \
#   scripts/build_pkg.sh
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(tr -d '[:space:]' < VERSION)
IDENTIFIER=jp.suzu.desktop
WORK=build/pkg
ROOT="$WORK/root"
COMPONENT="Suzu-component.pkg"
OUT="build/Suzu-$VERSION.pkg"

# 1. Suzu.app を作る
UNIVERSAL=${UNIVERSAL:-1} scripts/build_app.sh release
APP=build/Suzu.app
if [[ -n "${APP_SIGN_IDENTITY:-}" ]]; then
    codesign --force --deep --options runtime --timestamp --sign "$APP_SIGN_IDENTITY" "$APP"
fi

# 対応アーキテクチャ（インストーラーの hostArchitectures に使う）
ARCHS=$(lipo -archs "$APP/Contents/MacOS/Suzu" | tr ' ' ',')
echo "archs: $ARCHS"

# 2. /Applications に置く形でまとめる
rm -rf "$WORK"
mkdir -p "$ROOT/Applications" "$WORK/resources"
ditto "$APP" "$ROOT/Applications/Suzu.app"

# 既にある Suzu.app を探して別の場所に入れてしまわないよう、再配置を無効にする
pkgbuild --analyze --root "$ROOT" "$WORK/component.plist" >/dev/null
plutil -replace 0.BundleIsRelocatable -bool NO "$WORK/component.plist"
plutil -replace 0.BundleOverwriteAction -string upgrade "$WORK/component.plist"

chmod +x packaging/macos/scripts/preinstall packaging/macos/scripts/postinstall
pkgbuild --root "$ROOT" \
    --component-plist "$WORK/component.plist" \
    --identifier "$IDENTIFIER" \
    --version "$VERSION" \
    --install-location / \
    --scripts packaging/macos/scripts \
    "$WORK/$COMPONENT"

# 3. インストーラーの画面（ようこそ・使用許諾・完了）を用意する
cp -R packaging/macos/resources/ "$WORK/resources/"
for f in "$WORK"/resources/*.lproj/*.html; do
    sed -i '' "s/@VERSION@/$VERSION/g" "$f"
done

# 使用許諾 = MIT License ＋ キャラクター利用ガイドライン（Markdown をそのまま整形済みテキストとして表示）
make_license() { # <lang> <intro-html> <guidelines.md> <guidelines-title>
    local dest="$WORK/resources/$1.lproj/license.html"
    {
        echo '<!DOCTYPE html><html><head><meta charset="utf-8"><style>'
        echo 'body{font-family:-apple-system,"Hiragino Sans",sans-serif;font-size:12px;line-height:1.55;color:#222}'
        echo 'pre{white-space:pre-wrap;font-family:inherit;margin:0}h2{font-size:14px;color:#3D4A8F;margin:18px 0 6px}'
        echo '@media (prefers-color-scheme: dark){body{color:#eee}h2{color:#A9B8EE}}</style></head><body>'
        echo "$2"
        echo '<h2>1. MIT License</h2><pre>'
        sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' LICENSE
        echo '</pre><h2>2. '"$4"'</h2><pre>'
        # 先頭の言語切り替えリンク（HTML）を除いてから埋め込む
        awk 'BEGIN{skip=1} skip && /^<p align="center">/{inp=1} inp{ if(/<\/p>/){inp=0}; next } {skip=0; print}' "$3" \
            | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'
        echo '</pre></body></html>'
    } > "$dest"
}
make_license ja '<p>このソフトウェアは、プログラムとキャラクターでライセンスが分かれています。<br>・プログラム：MIT License（下記 1）<br>・キャラクター「鈴」（名前・デザイン・画像・アイコン）：キャラクター利用ガイドライン（下記 2）<br>「同意する」を選ぶと、両方に同意したものとみなされます。</p>' \
    CHARACTER_GUIDELINES.md 'キャラクター利用ガイドライン'
make_license en '<p>This software is licensed in two parts.<br>- The program: MIT License (section 1)<br>- The character “Suzu” (name, design, images, icons): Character Guidelines (section 2)<br>By clicking “Agree”, you agree to both.</p>' \
    CHARACTER_GUIDELINES.en.md 'Character Guidelines'

sed -e "s/@VERSION@/$VERSION/g" -e "s/@ARCHS@/$ARCHS/g" -e "s/@COMPONENT@/$COMPONENT/g" \
    packaging/macos/distribution.xml > "$WORK/distribution.xml"

# 4. ウィザード形式のインストーラーにまとめる
SIGN_ARGS=()
if [[ -n "${PKG_SIGN_IDENTITY:-}" ]]; then SIGN_ARGS=(--sign "$PKG_SIGN_IDENTITY" --timestamp); fi
rm -f "$OUT"
productbuild --distribution "$WORK/distribution.xml" \
    --resources "$WORK/resources" \
    --package-path "$WORK" \
    "${SIGN_ARGS[@]}" \
    "$OUT"

if [[ -n "${NOTARY_PROFILE:-}" ]]; then
    xcrun notarytool submit "$OUT" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$OUT"
fi

echo "built: $OUT"
