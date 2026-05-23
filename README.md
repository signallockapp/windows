# SignalLock — Windows System Tray App

Windows 10/11 system-tray utility that automatically locks the workstation when a trusted Bluetooth Low Energy device (e.g. your phone, watch, AirPods, BLE keychain tag) moves out of range. Functional and architectural mirror of the macOS app — same algorithm, same proximity logic, same safety defaults.

## Requirements

- **Windows 10 1809 (build 17763)** or newer (Windows 11 supported)
- **Bluetooth Low Energy** capable hardware
- **.NET 8 SDK** to build (the published binary needs only the .NET 8 runtime, or none if published self-contained)

## Build & Run

```powershell
# From the repository root
cd windows

# Restore + build (debug)
dotnet build

# Run the tray app
dotnet run

# Release build, framework-dependent
dotnet publish -c Release -r win-x64 --no-self-contained -o publish

# Release build, single-file self-contained (no .NET install required)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The output executable is `publish\SignalLock.exe`. Double-click to launch — the icon appears in the system tray (notification area).

## How to Use

1. Right-click the SignalLock tray icon → **Select Trusted Device…**
2. Bring the device you want to use as your "key" close to the PC. Pick the entry with the strongest (least negative) RSSI. Click **Select**.
3. From now on, the app starts monitoring automatically. Walk away with the trusted device and the screen will lock once the configured away delay elapses.
4. Use **Settings…** to tune RSSI threshold, away delay, smoothing window, and Start at login.
5. **Test Lock** locks the workstation immediately — useful to verify behavior before relying on auto-lock.

## How Proximity Detection Works

1. `BluetoothScanner` runs `Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher` in active scanning mode and emits each advertisement with its RSSI.
2. `ProximityMonitor` keeps a moving average of recent RSSI samples (default window 5).
3. The trusted device is considered **away** when **either**:
   - The smoothed RSSI is below the configured threshold (default -80 dBm), **or**
   - No advertisement has been received for longer than the away delay (default 20 seconds).
4. The away condition must persist for the full away-delay grace period before a lock fires. Exactly one lock fires per away episode.
5. After locking, SignalLock listens for the Windows `SessionSwitch / SessionUnlock` event. When you unlock with your password / PIN / Hello, the proximity monitor is rearmed: the next walkaway will lock again.

## How Locking Works

`LockService` invokes `user32!LockWorkStation` (Win32). It is the canonical Windows API for locking the current session, requires no elevated privileges, no Accessibility-style consent, and is the only place the project P/Invokes for the lock action.

## Required Permissions

- **Bluetooth** access. Modern Windows 11 lets users gate apps via **Settings → Privacy & security → Bluetooth**. SignalLock does not request elevated privileges.
- **No** Location, Accessibility, or Full Disk Access requirements.
- **No** network access — the app never opens a socket.

## Auto-start at Login

Toggling **Start at login** writes a value under `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`. Per-user, no admin required, no Task Scheduler dependency.

## Diagnostics

Logs are written to `%APPDATA%\SignalLock\signallock.log` and also forwarded to the Visual Studio Debug Output and stdout. Categories: `app`, `lock`, `proximity`, `bluetooth` — same as the macOS port. Sample lines:

```
2026-05-05 21:08:34.121 [INFO ] [SignalLock][app] Trusted device selected: Galaxy Watch (id=AABBCCDDEEFF)
2026-05-05 21:08:35.014 [INFO ] [SignalLock][app] Monitoring started (device=Galaxy Watch, threshold=-80 dBm, awayDelay=20s)
2026-05-05 21:09:00.832 [INFO ] [SignalLock][proximity] Away condition started (signal=true, silence=false)
2026-05-05 21:09:20.010 [INFO ] [SignalLock][proximity] Confirmed AWAY after 20s; will trigger lock
2026-05-05 21:09:20.012 [INFO ] [SignalLock][lock] Locked via LockWorkStation
2026-05-05 21:09:34.420 [INFO ] [SignalLock][app] SessionUnlock → rearming proximity monitor
```

`%APPDATA%\SignalLock\` also stores `settings.json` and `trusted-device.json`.

## Architecture

```
Program.cs                       — entry point; single-instance mutex; auto-start
AppState.cs                      — orchestrator; marshals events to UI thread

Models.cs                        — AppSettings, TrustedDevice, DiscoveredDevice, BluetoothAvailability
Logging.cs                       — minimal file + Trace + Console logger

Services/StoragePaths.cs         — %APPDATA%\SignalLock\ paths
Services/SettingsStore.cs        — JSON persistence of AppSettings
Services/TrustedDeviceStore.cs   — JSON persistence of TrustedDevice
Services/BluetoothScanner.cs     — WinRT BluetoothLEAdvertisementWatcher wrapper
Services/ProximityMonitor.cs     — moving-average + grace-period away decision
Services/LockService.cs          — Win32 LockWorkStation P/Invoke
Services/LoginItemService.cs     — HKCU\…\Run registry toggle

UI/TrayController.cs             — NotifyIcon + ContextMenuStrip
UI/SettingsForm.cs               — WinForms settings window
UI/DeviceSelectionForm.cs        — WinForms BLE picker
```

State flow is one-directional: `BluetoothScanner` → `AppState` → `ProximityMonitor` → `LockService`. UI only reads via `AppState.StateChanged` and writes via public methods.

## Current Limitations

- iPhones rotate BLE advertising addresses for privacy; tracking a stock iPhone reliably is hard. Apple Watch, Galaxy Watch, AirPods, fitness bands and dedicated BLE tags work well.
- The single-instance mutex is per-user; running the app under different users on the same machine produces independent instances.
- No notarized installer yet; running an unsigned `.exe` will trigger SmartScreen — choose **More info → Run anyway** the first time.
- Watcher recovery on transient Bluetooth failures is minimal: on `BluetoothLEAdvertisementWatcherStoppedEventArgs.Error`, we log the error; restarting monitoring is up to the user.

## Safety Defaults

- Default RSSI threshold: **-80 dBm**.
- Default away delay: **20 seconds**.
- Default smoothing window: **5 samples**.
- Auto-start on launch is enabled when a trusted device is configured.
- The first lock cannot fire until the device has been seen at least once after monitoring starts.

## License

SignalLock is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License v3.0** as published by the Free Software Foundation. See the [`LICENSE`](./LICENSE) file in this directory for the full text, or <https://www.gnu.org/licenses/gpl-3.0.html>.

SignalLock is distributed in the hope that it will be useful, but **without any warranty**; without even the implied warranty of merchantability or fitness for a particular purpose.

### Notes for forks and downstream builds

- The Inno Setup `AppId` GUID (`{56B5F70A-5A1E-40B5-B9C0-31C437B72F54}` in `installer/SignalLock.iss`) uniquely identifies upgrades. If you produce your own installer, **generate a fresh GUID** — otherwise the installer will treat your build as an upgrade of the upstream SignalLock.
- The publisher/support URL in the installer (`https://signallock.app`) points to the upstream project website; change it in your fork so that the **Help** and **Update** links in **Apps & features** point somewhere you control.
- This repository contains only the Windows app. The marketing website is maintained separately and is not GPL-licensed.
