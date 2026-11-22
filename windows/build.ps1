# 鈴 Suzu — Windows 版のビルドとセットアップウィザード（インストーラー）の作成
#
# 必要なもの（Windows 10/11）:
#   - .NET 8 SDK           https://dotnet.microsoft.com/download/dotnet/8.0
#   - Inno Setup 6（任意） https://jrsoftware.org/isdl.php   ※ winget install JRSoftware.InnoSetup
#
# 使い方（リポジトリ直下で）:
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1 -SkipTests -SkipInstaller
#
# できるもの:
#   build\windows\publish\Suzu.exe ＋ Sprites\   … インストールせずに動かせるアプリ
#   build\windows\SuzuSetup-<version>-x64.exe     … セットアップウィザード
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$SkipInstaller
)
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$version = (Get-Content (Join-Path $root "VERSION") -Raw).Trim()
$out = Join-Path $root "build\windows"
$publish = Join-Path $out "publish"
$licenseDir = Join-Path $out "license"
Write-Host "Suzu $version ($Runtime, $Configuration)"

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
New-Item -ItemType Directory -Force -Path $publish, $licenseDir | Out-Null

# 1. 単体テスト（行動・物理ロジック）
if (-not $SkipTests) {
    dotnet test (Join-Path $PSScriptRoot "Suzu.Core.Tests\Suzu.Core.Tests.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

# 2. .NET を別途インストールしなくても動く単一の exe を作る（画像素材は Sprites\ に置かれる）
dotnet publish (Join-Path $PSScriptRoot "Suzu\Suzu.csproj") -c $Configuration -r $Runtime -o $publish
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# 3. セットアップウィザードの「使用許諾」に表示する文書（MIT License ＋ キャラクター利用ガイドライン）
function Get-PlainText([string]$path) {
    $text = Get-Content -Raw -Encoding UTF8 $path
    # 先頭の言語切り替えリンク（HTML）を除く
    $text = [regex]::Replace($text, '(?s)^\s*<p align="center">.*?</p>\s*', '')
    return $text.Trim()
}
$utf8Bom = New-Object System.Text.UTF8Encoding $true
$mit = Get-PlainText (Join-Path $root "LICENSE")
$sections = @{
    "ja" = @(
        "鈴 Suzu デスクトップマスコット　使用許諾",
        "このソフトウェアは、プログラムとキャラクターでライセンスが分かれています。",
        "・プログラム：MIT License（下記 1）",
        "・キャラクター「鈴」（名前・デザイン・画像・アイコン）：キャラクター利用ガイドライン（下記 2）",
        "インストールすると、両方に同意したものとみなされます。",
        "",
        "==================== 1. MIT License ====================",
        "",
        $mit,
        "",
        "==================== 2. キャラクター利用ガイドライン ====================",
        "",
        (Get-PlainText (Join-Path $root "CHARACTER_GUIDELINES.md"))
    )
    "en" = @(
        "Suzu Desktop Mascot - License Agreement",
        "This software is licensed in two parts.",
        "- The program: MIT License (section 1 below)",
        "- The character ""Suzu"" (name, design, images, icons): Character Guidelines (section 2 below)",
        "By installing, you agree to both.",
        "",
        "==================== 1. MIT License ====================",
        "",
        $mit,
        "",
        "==================== 2. Character Guidelines ====================",
        "",
        (Get-PlainText (Join-Path $root "CHARACTER_GUIDELINES.en.md"))
    )
}
foreach ($lang in $sections.Keys) {
    $body = ($sections[$lang] -join "`n") -replace "`r?`n", "`r`n"
    [System.IO.File]::WriteAllText((Join-Path $licenseDir "license-$lang.txt"), $body, $utf8Bom)
}

if ($SkipInstaller) {
    Write-Host "built: $publish\Suzu.exe"
    exit 0
}

# 4. Inno Setup でセットアップウィザードを作る
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $iscc) {
    throw "Inno Setup 6 (ISCC.exe) が見つかりません。 winget install JRSoftware.InnoSetup でインストールしてください。"
}
& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" "/DLicenseDir=$licenseDir" "/DOutputDir=$out" `
    (Join-Path $PSScriptRoot "installer\Suzu.iss")
if ($LASTEXITCODE -ne 0) { throw "iscc failed" }
Write-Host "built: $out\SuzuSetup-$version-x64.exe"
