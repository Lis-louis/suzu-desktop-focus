; 鈴 Suzu — Windows 用セットアップウィザード（Inno Setup 6）
;
; 通常は windows\build.ps1 から実行する（exe の作成 → 使用許諾の文書の用意 → このスクリプトのコンパイル）。
; 手動で実行する場合:
;   iscc /DAppVersion=0.5.2 /DPublishDir=..\..\build\windows\publish /DLicenseDir=..\..\build\windows\license windows\installer\Suzu.iss
;
; ウィザードの流れ: 言語の選択 → ようこそ → 使用許諾（MIT ＋ キャラクター利用ガイドライン）
;                   → インストール先 → 追加のタスク（デスクトップアイコン・自動起動）→ 確認 → インストール → 完了（鈴を起動）
;
; アプリの自動更新（windows/Suzu/App/Updater.cs）は、このウィザードを画面なしで動かして入れ替える:
;   SuzuSetup-<version>-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS [/RELAUNCH=1]
; 前回のインストール先・言語・タスクはそのまま引き継がれる。/RELAUNCH=1 のときは終わったら鈴を起動し直す。

#ifndef AppVersion
  #define AppVersion "0.5.2"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\build\windows\publish"
#endif
#ifndef LicenseDir
  #define LicenseDir "..\..\build\windows\license"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\build\windows"
#endif

#define AppName "鈴 Suzu"
#define AppExe "Suzu.exe"
#define AppPublisher "Lis-louis"
#define AppUrl "https://github.com/Lis-louis/suzu-desktop-focus"

[Setup]
; AppId はアップグレード・アンインストールの識別に使うので変更しないこと
AppId={{6F1B8C2E-5A3D-4E7F-9B21-3C8D7A4E5F60}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Program: MIT License / Character "Suzu" © {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

; 管理者権限なしで自分用にインストール（必要ならウィザードで「すべてのユーザー」も選べる）
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\Suzu
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=no
DisableReadyPage=no
ShowLanguageDialog=auto

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir={#OutputDir}
OutputBaseFilename=SuzuSetup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes

WizardStyle=modern
WizardImageFile=wizard-100.bmp,wizard-200.bmp
WizardSmallImageFile=wizard-small-100.bmp,wizard-small-200.bmp
SetupIconFile=..\..\Resources\AppIcon\Suzu.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

; 起動中の鈴はインストール前に自動で終了させる
CloseApplications=force
RestartApplications=no

[Languages]
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"; LicenseFile: "{#LicenseDir}\license-ja.txt"
Name: "en"; MessagesFile: "compiler:Default.isl"; LicenseFile: "{#LicenseDir}\license-en.txt"

[CustomMessages]
ja.AutoStartTask=Windows へのサインイン時に鈴を起動する
en.AutoStartTask=Start Suzu when I sign in to Windows
ja.StartupGroup=起動:
en.StartupGroup=Startup:
ja.DeleteSettings=鈴の設定（大きさ・位置・言語など）も削除しますか？%n%n「いいえ」を選ぶと、次にインストールしたときに設定が引き継がれます。
en.DeleteSettings=Also delete Suzu's settings (size, position, language, etc.)?%n%nChoose "No" to keep them for the next installation.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
; サインイン時の起動は既定でオン（ウィザードで外せる。上書き・自動更新のときは前回の選択を引き継ぐ）
Name: "autostart"; Description: "{cm:AutoStartTask}"; GroupDescription: "{cm:StartupGroup}"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Sprites\*"; DestDir: "{app}\Sprites"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#LicenseDir}\license-ja.txt"; DestDir: "{app}"; DestName: "LICENSE-ja.txt"; Flags: ignoreversion
Source: "{#LicenseDir}\license-en.txt"; DestDir: "{app}"; DestName: "LICENSE-en.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; アプリの「ログイン時に起動」と同じ値（アプリの設定画面からも切り替えられる）
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Suzu"; \
  ValueData: """{app}\{#AppExe}"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; 自動更新：画面なしで入れ替えたあと、鈴を起動し直す（--updated で「新しいバージョンになりました」と知らせる）
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait runasoriginaluser; Check: ShouldRelaunch

[UninstallRun]
; 起動中の鈴を終了してからファイルを削除する
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopSuzu"

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { アプリの設定画面で有効にした自動起動も消す }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Suzu');
    if (not UninstallSilent) and DirExists(ExpandConstant('{userappdata}\Suzu')) then
      if MsgBox(CustomMessage('DeleteSettings'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{userappdata}\Suzu'), True, True, True);
  end;
end;
