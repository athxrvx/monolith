# Updating Monolith

## Install a newer build

1. Close Monolith from its tray menu with **Quit**, so the app and window-management engine stop cleanly.
2. Run the new `Monolith-Setup.exe` and install to the existing Monolith folder.
3. Launch Monolith from the Start menu.

The installer replaces the application files and refreshes the Start menu shortcut and per-user uninstall entry. Your settings are stored separately at `%USERPROFILE%\.config\monolith\config.json` and are kept during an update.

## Build an update from source

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File resources\scripts\build-release.ps1
```

The script rebuilds the Rust engine, embeds the new engine in Monolith, publishes the app, and builds `Monolith-Setup.exe`. All release files are collected in `publish/`.

## Uninstall

Use **Monolith Uninstall** in Windows Installed apps or run `MonolithUninstall.exe` from the install folder. The uninstaller asks before removing the app and its user data. Choosing to continue deletes settings, logs, and cached files as well as the installed app.
