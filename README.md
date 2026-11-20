<p align="center">
  <img src="docs/images/icon.png" width="128" alt="Suzu icon">
</p>

<h1 align="center">Suzu 鈴 — a macOS desktop mascot</h1>

<p align="center">
  <b>English</b> | <a href="README.ja.md">日本語</a>
</p>

<p align="center">
  A native macOS app where the original character Suzu lives on your desktop.<br>
  She walks around, stands on top of your windows, dozes off, and reacts when you click her.
</p>

---

## Features

- **Native Swift**: AppKit + Core Animation + SwiftUI. No Electron or web technologies.
- **Lives on your desktop**: borderless and transparent. Shown on every Space and over full-screen apps.
- **Stays out of your way**: clicks on transparent areas pass through to the app behind, and clicking Suzu never steals focus from the app you are using.
- **Acts on her own**: idling, walking, looking around, turning away, dozing, heading to the screen edge, and more.
  Her choices depend on her energy, the time of day, where she is, and cooldowns, so her behavior isn't purely random.
- **Interacts with windows**: stands on top of windows, moves along when a window moves, falls when it closes, and jumps down.
- **Mouse interaction**: click to get a reaction, drag to move or throw her, double-click to open the chat window. She turns toward your cursor.
- **Multiple displays**, saved and restored position, and launch at login.
- **Lightweight**: about 0.3–0.8% CPU and 75–100 MB of memory (measured on Apple Silicon, release build).
- **Hooks for Suzu Secretary**: receives notifications, lines, and expressions from the local AI assistant "Suzu Secretary" (鈴秘書).

## Requirements

- macOS 14 Sonoma or later (Apple Silicon recommended)
- Xcode 16 or later / Swift 6 toolchain (to build)

## Build and run

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

## Usage

| Action | Result |
|---|---|
| Click Suzu | She reacts (and wakes up if she was sleeping) |
| Drag Suzu | Move her anywhere. Release with momentum to throw her |
| Double-click Suzu | Open the chat window |
| Right-click Suzu | Menu |
| Click the Dock icon | Open the control panel |
| Right-click the Dock icon | Stop/start Suzu, call her |
| ⌘Q | Quit the app |

### Control panel

- **Stop / Start Suzu**: hides or shows Suzu while the app keeps running (almost no CPU while stopped).
- **Call**: brings Suzu to the screen your mouse is on.
- **Pause autonomous behavior**: keeps her still where she is.
- **Launch at login**: starts automatically when you log in (works best with the app in `/Applications`).

### Settings

You can change the display language (Japanese / English), her size (60–160%), whether she climbs onto windows, and the temporary options below.

> The app is shown in Japanese by default. You can switch it to English in Settings → Language.

## Project structure

```
Sources/
├─ SuzuCore/      behavior, physics, terrain, sprite resolution (no AppKit, unit-tested)
├─ SuzuBridge/    interface to Suzu Secretary
└─ SuzuApp/       rendering, mouse input, control panel, settings, chat window
Resources/
├─ Sprites/       Suzu's images (PNG) and manifest.json
└─ AppIcon/       app icon
Tests/            unit tests
docs/
├─ DESIGN.md      design document (design analysis, technology choices, feasibility, architecture)
└─ ASSETS.md      animation assets and the list of missing ones
tools/            tools for cutting out sprites, making the icon, and testing the bridge
scripts/          .app build script
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

```sh
# Send a line to Suzu in place of Suzu Secretary
swift tools/send_event.swift '{"type":"speak","text":"Good work today!"}'
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

## Permissions

Reading window positions needs no Screen Recording or Accessibility permission.
The app reads only each window's position and size, never its title or contents.

## License

Code and character are licensed separately.

| What | License |
|---|---|
| Code (`Sources/`, `Tests/`, `scripts/`, `tools/`, etc.) | [MIT License](LICENSE) |
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
