# SignalLock — Windows Installer

Inno Setup script that turns `windows\publish\SignalLock.exe` into a wizard-style installer that ships to end users.

## What it does

- Asks the user up-front whether to install **for everyone** (UAC-elevated, `%ProgramFiles%\SignalLock\`) or **just for me** (no UAC, `%LocalAppData%\Programs\SignalLock\`).
- Lets the user override the install directory.
- Adds a **Start Menu** shortcut. No desktop shortcut, no Quick Launch entry.
- Final page offers an opt-out **"Launch SignalLock"** checkbox (default on).
- Refuses to install if SignalLock is currently running and prompts the user to close it.
- **Does not** modify auto-start at login — SignalLock manages that itself from its own settings.

### Uninstall behaviour

- Removes `{app}` (the program directory).
- Removes the `HKCU\…\Run\SignalLock` auto-start entry if present.
- **Asks** before deleting `%APPDATA%\SignalLock\` (settings, trusted device, log). Default is **No**, so a future reinstall preserves the user's setup.

## One-time prerequisites

- Windows 10 1809 or newer.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- [Inno Setup 6](https://jrsoftware.org/isdl.php) installed in its default location.

## Build the installer

From the repository root, in PowerShell:

```powershell
.\windows\installer\build.ps1
```

This:

1. Reads the version from `windows\SignalLock.csproj` (`<Version>`).
2. Runs `dotnet publish -c Release -r win-x64` to produce `windows\publish\SignalLock.exe` (single-file, self-contained, compressed — see `csproj` Release `PropertyGroup`).
3. Invokes `ISCC.exe` with the version injected.
4. Writes `windows\installer\output\SignalLock-Setup-<version>-x64.exe`.

ARM64: `.\windows\installer\build.ps1 -Runtime win-arm64`. Output filename keeps the `-x64` suffix unless you adjust `OutputBaseFilename` in `SignalLock.iss` — fine for now since the .exe inside is what matters.

## Bumping the version

Edit `<Version>` in `windows\SignalLock.csproj` once. The csproj is the single source of truth — `build.ps1` reads it, the running app reports it, the installer carries it.

## Code signing — deferred

The installer is currently **unsigned**. Windows SmartScreen will display "Unknown publisher" warning to early downloaders until we acquire a code-signing certificate. To sign later:

1. Acquire an EV / OV code-signing certificate (~$200-400/year from Sectigo, DigiCert, SSL.com, etc.).
2. Add a `[Setup] SignTool=` directive to `SignalLock.iss` and a `signtool.exe` configuration in Inno Setup's Options dialog.
3. Sign both the inner `SignalLock.exe` (before `ISCC` packs it) and the outer setup .exe.

## Manual ad-hoc compile

If you want to compile without `build.ps1` (e.g., from the Inno Setup IDE):

1. Run `dotnet publish -c Release -r win-x64` in `windows\` first so `windows\publish\SignalLock.exe` exists.
2. Open `SignalLock.iss` in the Inno Setup Compiler and hit Build.
3. The fallback `MyAppVersion` defined at the top of the script (currently `0.1.0`) is what ends up in the artifact name unless you supply `/DMyAppVersion=...`.
