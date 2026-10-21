# Contributing to Monolith

Contributions are welcome. For substantial changes, open an issue first so the scope and behavior can be discussed before implementation.

## Development setup

- Windows 11
- .NET 8 SDK
- Rust stable with the MSVC toolchain

Build the desktop app and installer from the repository root with:

```powershell
powershell -ExecutionPolicy Bypass -File resources\scripts\build-release.ps1
```

The script builds the Rust engine, embeds it in the WinUI app, then publishes the app and installer into `publish/`.

## Pull requests

- Keep changes focused and explain user-visible behavior in the pull request.
- Update the README or settings documentation when behavior or configuration changes.
- Do not commit build output, local configuration, or personal logs.
