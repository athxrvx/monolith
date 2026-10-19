# Monolith

![Monolith](assets/monolith.png)

Monolith is an open-source scrolling window manager and desktop companion for Windows 11. It combines a native WinUI 3 settings app, a Rust window-management engine, a keyboard-driven launcher, and system tray controls.

> Monolith is under active development. Features and behavior may change between releases.

## Features

- Scrolling window management with columns and vertical window arrangements
- Keyboard navigation and window-management shortcuts
- A launcher available with `Alt + Space`
- A WinUI 3 settings app for engine controls, layout, window rules, appearance, and hotkeys
- A system tray menu for controlling Monolith
- Per-user configuration stored at `%USERPROFILE%\.config\monolith\config.json`

## Components

- `studio/` contains the WinUI 3 application and settings pages.
- `engine/` contains the Rust window-management engine.
- `launcher/` contains shared launcher and engine-supervision source and the embedded engine resource.
- `installer/` contains the per-user Windows installer and uninstaller source.
- `packages/` contains Rust workspace libraries and command-line tools.

## Requirements

- Windows 11
- .NET 8 SDK
- Rust stable with the MSVC toolchain and Windows target
- Visual Studio 2022 with the **.NET Desktop Development** workload is useful for IDE development

## Build from source

Run these commands from the repository root in PowerShell:

```powershell
# Build the window-management engine
cargo build --release --manifest-path engine/Cargo.toml --bin monolith_engine

# Update the engine binary embedded by the WinUI app
Copy-Item target\release\monolith_engine.exe launcher\Assets\monolith_engine.exe -Force

# Publish the self-contained WinUI 3 app
dotnet publish studio\Monolith.Studio.csproj -c Release -r win-x64 --self-contained true -o publish
```

Run `publish\Monolith.exe`. Keep the published support files alongside the executable.

To build the per-user installer after publishing the app:

```powershell
powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
```

The installer is written to `publish\Monolith-Setup.exe`.

## Configuration

Monolith stores per-user configuration in:

```text
%USERPROFILE%\.config\monolith\config.json
```

Hotkeys and window-management settings can be changed in the settings app.

## Inspirations

Monolith's interaction and layout ideas were informed by these projects:

- [komorebi](https://github.com/LGUG2Z/komorebi)
- [FancyWM](https://github.com/FancyWM/FancyWM)
- [PaperWM](https://github.com/paperwm/PaperWM)
- [GlazeWM](https://github.com/glzr-io/glazewm)

These links credit projects that inspired Monolith. Monolith is an independent project; see each project's repository for its code and license.

## License

Monolith is licensed under the GNU General Public License v3.0. See [LICENSE.md](LICENSE.md).
