# CLAUDE.md

鈴 Suzu デスクトップマスコット：Mac 版（Swift / `Sources/`）と Windows 版（C# WinForms / `windows/`）の2本立て。
片方を直したら、もう片方にも同じ問題がないか確認すること。行動・物理のロジックは
`Sources/SuzuCore` と `windows/Suzu.Core` に同じ内容で2つあり、テストも対応している。

リポジトリ：https://github.com/Lis-louis/suzu-desktop-focus（公開）

## やりとりのきまり

- **返事・説明・コミットメッセージは日本語で書く**（コード中のコメントも日本語）。
- 利用者は Git や GitHub の用語（PR・マージ・ブランチなど）に詳しくないので、使うときは短く説明する。
- 手間の少ない進め方を好む：確認が済んだら main に直接入れてよい（PR を挟まなくてよい）。
  ただし `VERSION` を上げた push はその場で Release が公開されるので、上げる前に一言伝える。
- この Windows PC の `gh` はログインしていない。GitHub の状態は公開 API（curl）で見るが、
  認証なしは 1 時間 60 回までなので、Actions の見張りは間隔を空ける（1 分以上）。

## 配布（GitHub Releases）

- バージョンはリポジトリ直下の `VERSION` に一本化（Windows は csproj が、Mac は `scripts/build_app.sh` が読む）。
- `VERSION` を上げて main に push すると `.github/workflows/installers.yml` が
  テスト → Windows（`SuzuSetup-<v>-x64.exe`・portable zip）と Mac（`Suzu-<v>.pkg`）をビルド →
  Release（タグ `v<v>`）を**下書きで作ってファイルを全部上げてから公開**する。
  公開済みの版なら Release は作らない。失敗したら Actions → Installers → Run workflow で上げ直せる。
- Mac のビルドは `runs-on: macos-latest`。`macos-15` を指定していたら機械が割り当てられず 15 分で cancelled になるようになった
  （2026-11、0.5.3 で 2 回続いた）。Mac のジョブが `runner_name` 空のまま cancelled なら、Mac の機械の指定を疑う。
- 直下の `SuzuSetup-0.1.0.exe`・`Suzu-0.1.0.pkg` は 0.1.0 を手で入れる人のために残している。新しい版は直下に置かない。

## 自動更新

### Windows 版（0.2.0 で導入）

- `windows/Suzu/App/Updater.cs`。起動 10 秒後と 3 時間ごとに
  `GET https://api.github.com/repos/Lis-louis/suzu-desktop-focus/releases/latest` を見て、
  `SuzuSetup-*-x64.exe` を `%LOCALAPPDATA%\Suzu\Updates` にダウンロード（大きさと、GitHub の `digest` の SHA-256 を確認）。
- **0.4.0 から自動で入れ替える**（アプリは開きっぱなしで使われるため）。ダウンロードが終わると鈴が吹き出しで
  「新しいバージョンが届きました！あと N 秒でアプリを更新して、すぐ戻ってきますね」と 10 秒の秒読みをし（鈴を止めているときは通知バルーン）、
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /RELAUNCH=1` でインストーラーを動かして終了する（`AppController.ScheduleAutoInstall`）。
- **入れ替え中も鈴を出しておく（0.5.1〜）**：`windows/Suzu/App/UpdateSplash.cs`。終了する直前に自分の exe と Sprites を
  `%LOCALAPPDATA%\Suzu\Splash` に複製して `--update-splash` で起動し、同じ場所に正面の鈴と「更新しています…すぐ戻りますね！」の吹き出しを出す。
  新しい鈴（二重起動防止の Mutex）が現れたら消える。入れ替えが終わって 15 秒たっても来なければ、インストール先の鈴を起動し直す。
  複製は次に起動した鈴が片付ける。
- **新しい版の調べ方（0.5.1〜）**：まず最新の Release に添付した `latest.json`
  （`releases/latest/download/latest.json`。GitHub API と同じ形でワークフローが作る）を読み、無ければ API（`releases/latest`）を使う。
  **認証なしの API は同じネットワーク全体で 1 時間 60 回まで**で、使い切ると更新を見つけられない（学校のネットワークなどで起こりうる。
  開発中に curl で何度も API を叩いても同じことになる）。Mac 版も同じ方法に変えるとよい。
- 起動し直すときはインストーラーが `--updated` を付けるので、鈴が「バージョン X になりました！」と言う（コントロールパネルは出さない）。
- 設定「新しいバージョンを自動で入れる」（`AutoInstallUpdates`・既定オン）をオフにしたとき、または Program Files に
  入れていて管理者の確認（UAC）が要るときは自動では入れ替えず、通知 → コントロールパネル上部の `UpdateBanner`・
  通知領域メニューの「再起動して更新」で入れ替える。押さずに「終了」しても `/RELAUNCH` なしで入れ替わる。
- `/RELAUNCH=1` は `windows/installer/Suzu.iss` の `[Run]`（`Check: ShouldRelaunch`）で鈴を起動し直すためのもの。
- インストーラーの「Windows へのサインイン時に鈴を起動する」は 0.4.0 から既定でオン（上書き・自動更新では前回の選択を引き継ぐ）。
- 確かめ方：`VERSION` を公開中の版より低くして `windowsuild.ps1` でビルド → そのインストーラーを入れて起動すると、
  公開中の版に自動で入れ替わる（0.2.9 → 0.3.0 で確認済み）。
- インストールせずに動かしているとき（exe の隣に `unins*.exe` がない）は入れ替えず、ダウンロードページを開くだけ。
- 設定画面の「アプリ」欄にバージョンと「アップデートを確認」。
- バージョン比較と Release の JSON の読み取りは `windows/Suzu.Core/Release.cs`（テストは `CoreTests.cs` の `ReleaseTests`）。

### Mac 版（0.3.0 で導入・実機での確認がまだ）

- `Sources/SuzuApp/Updater.swift`。Windows 版と同じく起動 10 秒後と 3 時間ごとに releases/latest を確認し、
  `Suzu-*.pkg` を `~/Library/Caches/jp.suzu.desktop/Updates` に裏でダウンロード（大きさと SHA-256 を CryptoKit で確認）。
  `.app` として起動しているときだけ（`swift run` では確認しない）。
- `/Applications` への書き込みに管理者パスワードが要るので、画面なしでは入れ替えない。
  「アップデートをインストール」で `NSWorkspace.shared.open(pkg)` → インストーラーが開く。
  `packaging/macos/scripts/preinstall`（鈴を終了）と `postinstall`（ログイン中のユーザーで起動し直す）で入れ替わる。
- 知らせ方：鈴の吹き出し ＋ メニューバーのメニュー先頭に「アップデートをインストール（v）…」＋
  コントロールパネル上部のお知らせ（`UpdateBanner`）。アプリメニューに「アップデートを確認…」。設定画面に「アプリ」セクション。
- バージョン比較・JSON の読み取りは `Sources/SuzuCore/Release.swift`（Windows の `Release.cs` と同じ内容）、テストは `ReleaseTests`。
- Windows 上で書き、GitHub Actions の Mac（macos-15）で `swift test` とアプリのビルドが通ることまで確認した（PR #4）。

## ボール遊び（Windows 版 0.5.0・Mac 版 0.5.3 で導入）

- 物理は `windows/Suzu.Core/Ball.cs`（床で弾む・転がる・画面の端で跳ね返る・つかんで投げる）。
  行動は `Activity.PlayBall`（`Behavior.cs` の `BehaviorSpec`・`MascotSimulation.StepPlayBall`）：ボールへ小走りで寄り、届いたら蹴る。
  時間が来たらボールが止まるのを見届けて終わり、終わったら 150 秒は自分からは遊ばない。
- ボールがないのに遊び始めると `MascotSimulation.WantsBall` が立ち、App 層（`AppController.UpdateBall`）が鈴の手元にボールを出す。
  鈴が自分で出したボールは、遊び終わって 6 秒たち止まっていれば片付ける。メニュー・コントロールパネルから出したボールは残す。
- 表示は `windows/Suzu/App/BallWindow.cs`（模様が転がりに合わせて回る。ドラッグで投げる・クリックで弾む）。
- 設定「ボール遊びをする」（`AllowBallPlay`・既定オン）。テストは `CoreTests.cs` の `BallTests`。
- 動作確認：環境変数 `SUZU_DEBUG_BALL=1` を付けて起動すると、1.5 秒後にボールが出る。
- Mac 版：物理は `Sources/SuzuCore/Ball.swift`、行動は `MascotSimulation.swift` の `stepPlayBall`（Windows と同じ内容。
  Mac はポイント単位なので速さの倍率 `SpeedScale` はない）。表示は `Sources/SuzuApp/BallWindow.swift`、
  出し入れは `AppDelegate` の「ボール」の節。テストは `SuzuCoreTests.swift` の `BallTests`。
  `SUZU_DEBUG_BALL=1 .build/debug/Suzu` で同じように確かめられる。

## 画像の向き

- `Resources/Sprites/side_right.png` は 0.5.0 で左右反転した。デザイン図の「Right」は右半身が見える絵（画面の左を向いている）ので、
  そのままだと右へ歩くときに後ろ歩き（ムーンウォーク）に見えていた。`tools/extract_sprites.swift` も反転して書き出すようにした。

## Mac でやること（次に Mac で作業するとき）

0.3.0 から Mac 版にも自動更新が入ったが、**実機ではまだ一度も動かしていない**。次の版（0.4.0 など）を出したときに、
0.3.0 の入った Mac で次を確かめる：

- 起動して 10 秒ほどで鈴の吹き出しが出て、メニューに「アップデートをインストール」が出るか
  （設定 →「アプリ」→「アップデートを確認」でもよい）。
- 署名・公証していない .pkg を開いたとき、Gatekeeper に止められないか
  （URLSession のダウンロードには通常 quarantine が付かないはずだが、実機で確かめる）。
  止められるなら、Developer ID で署名・公証するか（`scripts/build_pkg.sh` の `APP_SIGN_IDENTITY` など）、
  Sparkle（SwiftPM で入る。appcast と EdDSA 署名が必要）を検討する。
- インストール後に postinstall で鈴が起動し直すこと、設定（UserDefaults）が引き継がれること。
- 問題がなければこの節を消し、上の見出しの「実機での確認がまだ」を外す。

ほか：
- 0.2.0 以前の Mac 版には自動更新がないので、その利用者は 0.3.0 を一度だけ手で入れ直す必要がある（README に記載済み）。
- 古いブランチ `mac-auto-update`・`windows-auto-update`・`mac-updater` は使い終わったので、GitHub 上で消してよい。
