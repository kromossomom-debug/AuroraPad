<div align="center">

<img src="assets/aurorapad-256.png" alt="AuroraPad" width="120" />

# 🌌 AuroraPad

[🇧🇷 Português](README.md) · **🇺🇸 English**

**Bring your DualSense to the PC for real.**
Adaptive triggers, haptics, lightbar, touchpad as a mouse, Xbox 360 / DualShock 4 emulation and much more — in a light, good-looking app available in English, Portuguese and Spanish.

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-6C3FC7)
![Languages](https://img.shields.io/badge/languages-PT%20%7C%20EN%20%7C%20ES-2EA44F)
![License](https://img.shields.io/badge/license-MIT-informational)

<img src="docs/screenshots/home.png" alt="AuroraPad home screen" width="860" />

</div>

---

## ✨ Highlights

| | |
|---|---|
| 🎮 **Live controller** | A DualSense drawn on screen reacts to everything: buttons light up, sticks move, triggers fill with pressure, the touchpad shows your fingers and the lightbar shows its real colour. |
| 🔫 **Adaptive triggers** | Resistance, semi-automatic gun, rifle, bow, galloping, machine, vibration… with a **resistance-over-travel graph** and ready-made presets (pistol, brake, throttle, horse riding). |
| 🖱️ **Touchpad as a mouse** | Works like a laptop touchpad: one finger moves, pressing clicks (right side = right click), a light tap clicks, two fingers scroll. It only becomes a mouse after a **double tap or holding a finger** — the controller vibrates to confirm and locks itself again when you stop using it. |
| 💥 **Rumble and HD haptics** | Turns game rumble — or PC audio, or an **audio file** — into high-definition haptics on the DualSense actuators, over USB and also wirelessly over Bluetooth, with a live level meter. Test patterns: pulse, explosion, engine, heartbeat, rain. Tray warning when the **battery runs low**. |
| 🔊 **PC audio on the controller** | Plays the computer's sound on the DualSense speaker (or a headset plugged into it) without changing Windows' sound output — over the USB cable and also wirelessly over Bluetooth. |
| 🌈 **Lighting** | RGB lightbar with brightness, battery-based colour, player LEDs and the mic LED. |
| 🕹️ **Emulation** | Appears as an **Xbox 360** or **DualShock 4** controller (with gyro and touchpad) through ViGEmBus. HidHide hides the physical controller to avoid double input. |
| ⌨️ **Keyboard, mouse and macros** | Any button can become a key, a click or a macro (`ctrl+c`, `w hold 300, wait 50, space`). Per-button turbo and gyro-driven cursor. |
| 👆 **Activators and layers** | Each button can act on **tap, double tap or hold** (normal use still goes to the game), **toggle** mode (press once, stays held) and a full **Shift layer** of mappings while you hold a button. |
| 🎯 **Advanced aiming** | **Flick stick** (the right stick turns the camera instantly), **gyro as a stick** for games without mouse support, player-space gyro, acceleration, smoothing and **automatic gyro calibration**. |
| 🔫 **Two-stage triggers** | Half pull and full pull become separate buttons: aim at half, fire at full. |
| 🎯 **Per-game profiles** | Game library from **Steam, Epic and GOG**; the right profile loads by itself when the game starts. |
| 🔌 **Integrations** | UDP server compatible with adaptive-trigger mods (port 6969), **Cemuhook/DSU** motion server for emulators (Cemu, Dolphin, Ryujinx…) and **OSC** output (VRChat, TouchDesigner…). |
| ⚡ **PS shortcuts** | PS + Options/Create switches profile, PS + touchpad toggles the mouse, PS + △ pauses the virtual controller, PS + ▲/▼ changes brightness. |
| 🪶 **Runs in the background** | Closing the window keeps AuroraPad in the system tray using very little memory; it can start with Windows straight into the tray. |

## 📸 Screens

<table>
<tr>
<td><img src="docs/screenshots/live.png" alt="Live input" /><p align="center"><b>Live input</b></p></td>
<td><img src="docs/screenshots/triggers.png" alt="Adaptive triggers" /><p align="center"><b>Adaptive triggers</b></p></td>
</tr>
<tr>
<td><img src="docs/screenshots/keyboard-mouse.png" alt="Keyboard, mouse and macros" /><p align="center"><b>Touchpad, mouse and macros</b></p></td>
<td><img src="docs/screenshots/haptics.png" alt="Rumble and haptics" /><p align="center"><b>Rumble and haptics</b></p></td>
</tr>
<tr>
<td colspan="2"><img src="docs/screenshots/emulation.png" alt="Emulation and connections" /><p align="center"><b>Emulation and connections</b></p></td>
</tr>
</table>

## 🎮 Supported controllers

| Controller | USB | Bluetooth | Adaptive triggers | Audio haptics |
|---|:---:|:---:|:---:|:---:|
| DualSense (PS5) | ✅ | ✅ | ✅ | ✅ (USB and Bluetooth) |
| DualSense Edge | ✅ | ✅ | ✅ | ✅ (USB and Bluetooth) |
| DualShock 4 (PS4) | ✅ | ✅ | — | — |

## 🚀 Getting started

### Install

1. Download **`AuroraPad-<version>-win-x64.msi`** from the [`release-files`](release-files) folder (or from **[Releases](../../releases/latest)**).
2. Open the installer → **Next** → accept the license → done. No administrator rights or .NET needed: it installs to `%LOCALAPPDATA%\Programs\AuroraPad` and creates Start menu and desktop shortcuts. You can also choose to start it with Windows in the background.
3. Connect the controller by cable or Bluetooth — it shows up by itself.

> Rather not install? Download **`AuroraPad-<version>-win-x64-portable.zip`**, extract it and run `AuroraPad.exe`.
>
> Windows may show a SmartScreen warning because the installer has no paid code-signing certificate: click **More info → Run anyway**.

To uninstall: **Settings → Apps → AuroraPad → Uninstall** (profiles stay in `%APPDATA%\AuroraPad`).

**Optional**
- Xbox 360 / DualShock 4 emulation: install [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) (the app has a button for it).
- Hide the physical controller from games: install [HidHide](https://github.com/nefarius/HidHide/releases).

### Build and create the installer

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download). The WiX Toolset comes from NuGet, nothing else to install.

```powershell
.\build-installer.ps1                  # project version
.\build-installer.ps1 -Version 1.5.0   # or a specific version
```

Output goes to `artifacts\installer\*.msi` and `artifacts\*-portable.zip`.

### Publish a release on GitHub

The [`.github/workflows/release.yml`](.github/workflows/release.yml) workflow builds the installer and creates the Release automatically when a version tag is pushed:

```bash
git tag v1.5.0
git push origin v1.5.0
```

## 🔌 UDP API for mods

Send JSON to `127.0.0.1:6969` (format compatible with existing adaptive-trigger mods):

```json
{"instructions":[{"type":1,"parameters":[0,2,13,0,8]}]}
```

| type | instruction | parameters |
|---|---|---|
| 1 | TriggerUpdate | controller, trigger (1=L, 2=R), mode, ...extras |
| 2 | RGBUpdate | controller, R, G, B[, brightness] |
| 3 | PlayerLED | controller, 5 booleans |
| 4 | TriggerThreshold | controller, trigger, threshold 0-255 |
| 5 | MicLED | controller, 0=on 1=pulsing 2=off |
| 6 | PlayerLEDNewRevision | controller, 0-4 = player 1-5, 5 = off |
| 7 | ResetToUserSettings | controller |

Every packet gets a JSON reply with the controllers' state. Trigger mode numbers are in [`TriggerSettings.cs`](src/AuroraPad.Core/Triggers/TriggerSettings.cs).

## 📡 OSC

`/dsx/n/lx`, `ly`, `rx`, `ry`, `l2`, `r2` (float) · `/dsx/n/button/<name>` (0/1, on change) · `/dsx/n/gyro`, `/dsx/n/accel` (3 floats) · `/dsx/n/touch1`, `touch2` (active, x, y) · `/dsx/n/battery`

## 🧱 Architecture

```
src/
├─ AuroraPad.Core   HID protocols (DualSense and DS4), triggers, haptics, speaker audio,
│                   emulation, mapping/macros, touchpad mouse, DSU, OSC, UDP, profiles, library
└─ AuroraPad.App    WPF UI, controller drawing, translations (PT/EN/ES)
tools/
└─ AuroraPad.Probe  hardware tests: dsu, osc, haptics, bthaptics, btspeaker, tree, library, macro, hidhide
installer/          MSI installer (WiX Toolset) — built by build-installer.ps1
assets/             app icon
release-files/      ready-to-use installer and portable zip
```

Profiles and settings live in `%APPDATA%\AuroraPad`.

## ⚠️ Notes

- Keyboard/mouse mapping uses `SendInput`; some games with anti-cheat ignore synthetic input.
- Over Bluetooth, haptics use HID report 0x32 and PC audio on the controller speaker uses report 0x36 (Opus audio), no cable needed.
- HidHide may require running AuroraPad as administrator.

## 📜 Legal

AuroraPad is an independent project and is **not affiliated with, endorsed or sponsored by Sony Interactive Entertainment**. "PlayStation", "DualSense" and "DualShock" are trademarks of their respective owners and are mentioned only to indicate compatibility. The controller illustration is original.

Built with [HidSharp](https://www.zer7.com/software/hidsharp), [ViGEm.Client](https://github.com/nefarius/ViGEm.NET), [Nefarius.Drivers.HidHide](https://github.com/nefarius/Nefarius.Drivers.HidHide), [NAudio](https://github.com/naudio/NAudio), [Concentus](https://github.com/lostromb/concentus) and [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet).

Licensed under the [MIT License](LICENSE).
