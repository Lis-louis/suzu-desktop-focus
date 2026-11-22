<p align="center">
  <img src="docs/images/icon.png" width="128" alt="Suzu icon">
</p>

<h1 align="center">Suzu 鈴 — a desktop mascot for macOS and Windows</h1>

<p align="center">
  <b>English</b> | <a href="README.ja.md">日本語</a>
</p>

<p align="center">
  A native app for macOS and Windows where the original character Suzu lives on your desktop.<br>
  She walks around, stands on top of your windows, dozes off, and reacts when you click her.
</p>

---

## Features

- **Native on each OS**: macOS uses Swift (AppKit + Core Animation + SwiftUI); Windows uses C# / .NET 8 (WinForms + Win32 layered windows). No Electron or web technologies.
- **Lives on your desktop**: borderless and transparent. Shown on every Space and over full-screen apps.
- **Stays out of your way**: clicks on transparent areas pass through to the app behind, and clicking Suzu never steals focus from the app you are using.
- **Acts on her own**: idling, walking, looking around, turning away, dozing, heading to the screen edge, and more.
  Her choices depend on her energy, the time of day, where she is, and cooldowns, so her behavior isn't purely random.
- **Interacts with windows**: stands on top of windows, moves along when a window moves, falls when it closes, and jumps down.
- **Mouse interaction**: click to get a reaction, drag to move or throw her, double-click to open the chat window. She turns toward your cursor.
- **Multiple displays**, saved and restored position, and launch at login.
- **Lightweight**: about 0.3–0.8% CPU and 75–100 MB of memory (measured on Apple Silicon, release build).
- **Hooks for Suzu Secretary**: receives notifications, lines, and expressions from the local AI assistant "Suzu Secretary" (鈴秘書).

## Install

Download the installer for your OS from [Releases](https://github.com/Lis-louis/suzu-desktop-focus/releases)
(or from the Artifacts of the [Installers](.github/workflows/installers.yml) workflow).

| OS | File | Steps |
|---|---|---|
| macOS | `Suzu-<version>.pkg` | Open it and follow the installer (Welcome → License → Install). Suzu.app goes into Applications and starts when the installer finishes |
| Windows | `SuzuSetup-<version>-x64.exe` | Run it and follow the setup wizard (License → Install location → Desktop icon / start at sign-in → Install). Installs for the current user, no administrator rights needed |

To uninstall: on macOS, quit Suzu and move it from Applications to the Trash. On Windows, use **Settings → Apps → Installed apps → 鈴 Suzu → Uninstall**.

> The installers are not code-signed yet, so the OS may warn you the first time.
> macOS: if the .pkg is blocked, go to **System Settings → Privacy & Security** and click **Open Anyway**.
> Windows: if SmartScreen appears, click **More info → Run anyway**.

## Requirements

- macOS 14 Sonoma or later (Apple Silicon or Intel)
- Windows 10 (version 1809 or later) / Windows 11, 64-bit (x64. Also runs on Arm through emulation)
- To build: Xcode 16 or later / Swift 6 toolchain (macOS), .NET 8 SDK (Windows)

## Build and run

### macOS

```sh
git clone https://github.com/Lis-louis/suzu-desktop-focus.git
cd suzu-desktop-focus

scripts/build_app.sh          # creates build/Suzu.app
open build/Suzu.app           # launch
```

During development you can also use:

```sh
swift run Suzu                # launch without building the .app
swift test                    # unit tests
```

### Windows

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Run these in PowerShell at the repository root:

```powershell
dotnet test windows\Suzu.Core.Tests        # unit tests
dotnet run --project windows\Suzu          # launch
```

### Building the installers

| Installer | Command | Output |
|---|---|---|
| macOS `.pkg` | `scripts/build_pkg.sh` (on a Mac) | `build/Suzu-<version>.pkg` (Universal: Apple Silicon + Intel) |
| Windows setup `.exe` | `powershell -ExecutionPolicy Bypass -File windows\build.ps1` (on Windows, with [Inno Setup 6](https://jrsoftware.org/isdl.php)) | `build\windows\SuzuSetup-<version>-x64.exe` |

The Windows `.exe` can't be built on a Mac. Pushing to GitHub runs the [Installers](.github/workflows/installers.yml) workflow,
which builds both installers on GitHub's macOS and Windows machines and uploads them as Artifacts.
Pushing a tag such as `v0.1.0` also attaches them to a draft release.
The version number comes from the `VERSION` file.

To sign and notarize the macOS package, set `APP_SIGN_IDENTITY`, `PKG_SIGN_IDENTITY`, and `NOTARY_PROFILE` (see the comments in `scripts/build_pkg.sh`).

## Usage

| Action | Result |
|---|---|
| Click Suzu | She reacts (and wakes up if she was sleeping) |
| Drag Suzu | Move her anywhere. Release with momentum to throw her |
| Double-click Suzu | Open the chat window |
| Right-click Suzu | Menu |
| Click the Dock icon (macOS) / the tray icon (Windows) | Open the control panel |
| Right-click the Dock icon (macOS) / the tray icon (Windows) | Stop/start Suzu, call her |
| ⌘Q (macOS) / "Quit" in the menu (Windows) | Quit the app |

On Windows, Suzu's icon sits in the notification area (system tray) at the right end of the taskbar. If you don't see it, click **^** to show hidden icons.

### Control panel

- **Stop / Start Suzu**: hides or shows Suzu while the app keeps running (almost no CPU while stopped).
- **Call**: brings Suzu to the screen your mouse is on.
- **Pause autonomous behavior**: keeps her still where she is.
- **Launch at login**: starts automatically when you log in (works best with the app in `/Applications`).

### Settings

You can change the display language (Japanese / English), her size (40–200%), opacity (30–100%), whether she stays above other windows, the frame rate (matches your monitor's refresh rate by default, e.g. 180 fps on a 180 Hz monitor; you can cap it at 30–240 fps), how fast she moves (×0.5–×2.0), whether she climbs onto windows, and the temporary options below. **Reset appearance and behavior to defaults** puts these back to their initial values (language and startup options are kept).

Size can also be changed from the Control Panel, from **Size** in the right-click / menu-bar (tray) menu (50%, 75%, 100%, 125%, 150%, 200%, or one step larger/smaller), or by scrolling over Suzu while holding ⌘ (macOS) / Ctrl (Windows).

> The app is shown in Japanese by default. You can switch it to English in Settings → Language.

## Project structure

```
Sources/          macOS version (Swift)
├─ SuzuCore/      behavior, physics, terrain, sprite resolution (no AppKit, unit-tested)
├─ SuzuBridge/    interface to Suzu Secretary
└─ SuzuApp/       rendering, mouse input, control panel, settings, chat window
windows/          Windows version (C# / .NET 8)
├─ Suzu.Core/     port of SuzuCore and the bridge's JSON format (no Windows APIs)
├─ Suzu.Core.Tests/ the same unit tests as the macOS version
├─ Suzu/          layered windows, tray icon, control panel, settings, chat window
├─ installer/     setup wizard (Inno Setup script and images)
└─ build.ps1      tests → Suzu.exe → setup wizard
packaging/macos/  macOS installer (.pkg) screens and scripts
Resources/
├─ Sprites/       Suzu's images (PNG) and manifest.json
└─ AppIcon/       app icon
Tests/            unit tests
docs/
├─ DESIGN.md      design document (design analysis, technology choices, feasibility, architecture)
└─ ASSETS.md      animation assets and the list of missing ones
tools/            tools for cutting out sprites, making icons and installer images, and testing the bridge
scripts/          .app / .pkg build scripts
```

See [docs/DESIGN.md](docs/DESIGN.md) for details (in Japanese).

## Character design and image assets

Suzu's design reference is the two character sheets in `鈴の画像です。これを作って欲しいです/`.
The rule is **never to change her outfit, hairstyle, hair ornament, or colors** from those sheets.

For now, the only images are three poses cut out from the sheets (front, facing right, back).
Missing assets (facing left, walk frames, sitting, sleeping, falling, and so on) are listed in [docs/ASSETS.md](docs/ASSETS.md).
Put a PNG in `Resources/Sprites/` and add one line to `manifest.json`, and the behaviors that need it are enabled automatically.

### Temporary options until the assets are ready

- **Mirror the right-facing image for the left (temporary)**: Suzu's hair ornament is only on her left side, so mirroring changes her design. Off by default.
- **Try behaviors with unfinished assets**: shows sitting, sleeping, climbing, and so on with the standing pose plus a speech bubble, for testing.

## Suzu Secretary integration

All communication goes through the `SecretaryBridge` protocol. The current implementation is a minimal one using the local `DistributedNotificationCenter`.
The Windows version uses named pipes that only the same user's processes can connect to (`\\.\pipe\jp.suzu.secretary.event`, one JSON event per line). The JSON format is the same.

```sh
# Send a line to Suzu in place of Suzu Secretary
swift tools/send_event.swift '{"type":"speak","text":"Good work today!"}'                       # macOS
powershell -File tools\send_event.ps1 '{"type":"speak","text":"Good work today!"}'             # Windows
```

| type | Example | Result |
|---|---|---|
| `speak` | `{"type":"speak","text":"…"}` | Says it in a speech bubble and shows it in the chat window |
| `notify` | `{"type":"notify","title":"…","body":"…"}` | Looks surprised and shows the notification |
| `expression` | `{"type":"expression","pose":"joy","duration":3}` | Expression / reaction |
| `activity` | `{"type":"activity","hint":"userFocused"}` | Stays quiet while you focus or are in a meeting |

## Status

| Stage | Status |
|---|---|
| Design and asset inventory | Done |
| Transparent overlay, dragging, control panel | Done |
| Basic animation and moving around the desktop | Done (within the current assets) |
| Standing on, following, and jumping off windows | Done |
| Hanging and climbing | Logic done, waiting for assets |
| Sitting, sleeping, walk animation | Waiting for assets |
| Full Suzu Secretary integration | Planned, to match Suzu Secretary's implementation |

### Known limitations

- There is no left-facing image yet, so she moves left in her front-facing standing pose.
- Suzu is always shown in front of normal windows (she never goes behind them).
- Suzu cannot move other windows (that would need permission to control other apps).
- Windows: maximized windows and always-on-top windows are not used as footholds. Settings are stored in `%APPDATA%\Suzu\settings.json`.

## Permissions

Reading window positions needs no Screen Recording or Accessibility permission.
The app reads only each window's position and size, never its title or contents.

## License

Code and character are licensed separately.

| What | License |
|---|---|
| Code (`Sources/`, `Tests/`, `windows/`, `packaging/`, `scripts/`, `tools/`, etc.) | [MIT License](LICENSE) |
| The character Suzu (name, design, images, icon, character sheets) | [Character Usage Guidelines](CHARACTER_GUIDELINES.en.md) |

You may draw and share non-commercial fan art of Suzu, with credit.
The following are not allowed:

- Uses that go against public decency
- Reposting or redistributing the images
- Commercial use
- Using Suzu for AI training

If you distribute a modified version of the app, remove all of Suzu's images and her name (it is no longer the official Suzu).

```
Character "Suzu" © Lis-louis
```
