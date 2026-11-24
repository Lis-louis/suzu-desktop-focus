# CLAUDE.md

鈴 Suzu デスクトップマスコット：Mac 版（Swift / `Sources/`）と Windows 版（C# WinForms / `windows/`）の2本立て。
片方を直したら、もう片方にも同じ問題がないか確認すること。行動・物理のロジックは
`Sources/SuzuCore` と `windows/Suzu.Core` に同じ内容で2つあり、テストも対応している。

リポジトリ：https://github.com/Lis-louis/suzu-desktop-focus（公開）

## 配布（GitHub Releases）

- バージョンはリポジトリ直下の `VERSION` に一本化（Windows は csproj が、Mac は `scripts/build_app.sh` が読む）。
- `VERSION` を上げて main に push すると `.github/workflows/installers.yml` が
  テスト → Windows（`SuzuSetup-<v>-x64.exe`・portable zip）と Mac（`Suzu-<v>.pkg`）をビルド →
  Release（タグ `v<v>`）を**下書きで作ってファイルを全部上げてから公開**する。
  公開済みの版なら Release は作らない。失敗したら Actions → Installers → Run workflow で上げ直せる。
- 直下の `SuzuSetup-0.1.0.exe`・`Suzu-0.1.0.pkg` は 0.1.0 を手で入れる人のために残している。新しい版は直下に置かない。

## Windows 版の自動更新（0.2.0 で導入）

- `windows/Suzu/App/Updater.cs`。起動 10 秒後と 3 時間ごとに
  `GET https://api.github.com/repos/Lis-louis/suzu-desktop-focus/releases/latest` を見て、
  `SuzuSetup-*-x64.exe` を `%LOCALAPPDATA%\Suzu\Updates` にダウンロード（大きさと、GitHub の `digest` の SHA-256 を確認）。
- 準備ができたら通知（バルーン）→ コントロールパネル上部の `UpdateBanner`・通知領域メニューに「再起動して更新」。
  押すと `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /RELAUNCH=1` でインストーラーを動かして終了。
  押さずに「終了」しても `/RELAUNCH` なしで入れ替わる。
- `/RELAUNCH=1` は `windows/installer/Suzu.iss` の `[Run]`（`Check: ShouldRelaunch`）で鈴を起動し直すためのもの。
- インストールせずに動かしているとき（exe の隣に `unins*.exe` がない）は入れ替えず、ダウンロードページを開くだけ。
- 設定画面の「アプリ」欄にバージョンと「アップデートを確認」。
- バージョン比較と Release の JSON の読み取りは `windows/Suzu.Core/Release.cs`（テストは `CoreTests.cs` の `ReleaseTests`）。

## Mac 版で後でやること

### 自動更新（Windows 版は 0.2.0 で実装済み、Mac 版は未着手）

下書きのコードがブランチ `mac-auto-update` にある（Windows 上で書いたもので、**ビルドしていない**）。
`Sources/SuzuCore/Release.swift` だけは Windows の Swift でコンパイル・動作確認済み。まずこれを取り込んで
`swift build`・`swift test`・`scripts/build_app.sh` が通るか確認し、直すところから始めること。

決めてある方針：
- Windows 版と同じく、起動 10 秒後と 3 時間ごとに releases/latest を確認し、`Suzu-*.pkg` を
  `~/Library/Caches/jp.suzu.desktop/Updates` に裏でダウンロード（大きさと SHA-256 を CryptoKit で確認）。
  `.app` として起動しているときだけ（`swift run` では確認しない）。
- `/Applications` への書き込みに管理者パスワードが要るので、画面なしでは入れ替えない。
  「アップデートをインストール」で `NSWorkspace.shared.open(pkg)` → インストーラーが開く。
  既存の `packaging/macos/scripts/preinstall`（鈴を終了）と `postinstall`（ログイン中のユーザーで起動し直す）がそのまま使える。
- 知らせ方：鈴の吹き出し ＋ メニューバーのメニュー先頭に「アップデートをインストール（v）…」＋
  コントロールパネル上部のお知らせ（`UpdateBanner`）。アプリメニューに「アップデートを確認…」。
- 設定画面に「アプリ」セクション（バージョン・状態・「アップデートを確認」）。
- バージョン比較・JSON の読み取りは `SuzuCore/Release.swift`（Windows の `Release.cs` と同じ内容）、テストは `ReleaseTests`。

確認すること：
- 署名・公証していない .pkg をアプリが URLSession でダウンロードして開いたとき、Gatekeeper に止められないか
  （URLSession のダウンロードには通常 quarantine が付かないはずだが、実機で確かめる）。
  止められるなら、Developer ID で署名・公証するか（`scripts/build_pkg.sh` の `APP_SIGN_IDENTITY` など）、
  Sparkle（SwiftPM で入る。appcast と EdDSA 署名が必要）を検討する。
- インストール後に postinstall で鈴が起動し直すこと、設定（UserDefaults）が引き継がれること。
- 古い版（自動更新のない Mac 版）を使っている人は、一度だけ手で入れ直す必要がある。

終わったら：
- `VERSION` を上げて main に push（Release に Mac 用 .pkg も同じタグで載る。ワークフローの変更は不要のはず）。
- README（日本語・英語）の「macOS 版の自動アップデートは準備中」の行を直し、Mac での動き
  （吹き出しで知らせる → 「アップデートをインストール」でインストーラーが開く → 管理者パスワード → 起動し直す）を書く。
- この節を「実装済み」に書き換える。
