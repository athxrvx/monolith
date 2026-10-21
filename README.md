# Monolith

![Monolith](assets/monolith.png)

Monolith is an open-source scrolling window manager and desktop companion for Windows 11. It combines a native WinUI 3 settings app, a Rust window-management engine, a keyboard-driven launcher, and system tray controls.

> Monolith is under active development. Features and behavior may change between releases.

## Features

- Scrolling window management with columns and vertical window arrangements
- Nine Monolith-managed workspaces with vertical navigation, independent of Windows virtual desktops
- Move windows between Monolith workspaces while preserving each workspace's layout
- Keyboard navigation and window-management shortcuts
- A launcher available with `Alt + Space`
- A WinUI 3 settings app for engine controls, layout, window rules, appearance, and hotkeys
- A system tray menu for controlling Monolith
- Per-user configuration stored at `%USERPROFILE%\.config\monolith\config.json`

## Components

- `studio/` contains the WinUI 3 app, settings pages, launcher, tray controls, and engine supervisor.
- `engine/` contains the Rust window-management engine.
- `installer/` contains the per-user Windows installer and uninstaller source.

## Requirements

- Windows 11
- .NET 8 SDK
- Rust stable with the MSVC toolchain and Windows target
- Visual Studio 2022 with the **.NET Desktop Development** workload is useful for IDE development

## Build from source

Run this from the repository root in PowerShell to build the engine, embed it in the app, publish Monolith, and create the setup executable in one output folder:

```powershell
powershell -ExecutionPolicy Bypass -File resources\scripts\build-release.ps1
```

The unified `publish/` folder contains `Monolith.exe`, its support files, and `Monolith-Setup.exe`. Run `publish\Monolith.exe` for the portable published app or `publish\Monolith-Setup.exe` to install it.

To create only the setup executable after publishing the app:

```powershell
powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1 -AppPublishDirectory publish -OutputDirectory publish
```

## Updating

See [UPDATING.md](UPDATING.md) for upgrade and configuration-retention details.

## Configuration

Monolith stores per-user configuration in:

```text
%USERPROFILE%\.config\monolith\config.json
```

Hotkeys and window-management settings can be changed in the settings app.

The default workspace shortcuts are `Alt + Shift + Up/Down` to move between workspaces, `Alt + Shift + PageUp/PageDown` to move the focused window, and `Alt + 1` through `Alt + 9` to jump to a numbered workspace. Inactive workspace windows are minimized so their taskbar buttons remain available; activating one returns to its Monolith workspace.

## Inspirations

Monolith's interaction and layout ideas were informed by these projects:

- [komorebi](https://github.com/LGUG2Z/komorebi)
- [FancyWM](https://github.com/FancyWM/FancyWM)
- [PaperWM](https://github.com/paperwm/PaperWM)
- [GlazeWM](https://github.com/glzr-io/glazewm)

These links credit projects that inspired Monolith. Monolith is an independent project; see each project's repository for its code and license.

## License

Monolith is licensed under the GNU General Public License v3.0. See [LICENSE.md](LICENSE.md).
