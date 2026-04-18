# L.A.M.P.
### _Local Activity & Mood Peripheral_
> Because your colleagues somehow still can't tell when you don't want to be disturbed.

A Windows system-tray application that controls the Adafruit Trinkey Neo RGB LED from a context menu, with optional automatic MS Teams presence integration.

> **Based on** [wolllis/busylight](https://github.com/wolllis/busylight) — the original Python-based busylight project that this C# app was derived from.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Windows 10 / 11
- Adafruit Trinkey Neo (VID `239A` / PID `80EF`) loaded with the [CURRENT.UF2 firmware](https://github.com/wolllis/busylight/blob/main/03_Precompiled_Firmware/CURRENT.UF2) from the source project – drag the `.UF2` file onto the Trinkey's USB drive to flash it
- Optional 3D-printed cover: STL files for the device housing are available on [Thingiverse – thing:5518069](https://www.thingiverse.com/thing:5518069)

## Build & Run

```powershell
cd LAMP
dotnet run          # run directly from source
dotnet publish -c Release -r win-x64 --self-contained   # produce a single .exe
```

The published executable ends up in `bin\Release\net8.0-windows\win-x64\publish\`.

## Usage

1. Plug in the Trinkey.
2. Launch `LAMP.exe`. A grey dot appears in the system tray.
3. **Left-click or right-click** the tray icon to open the context menu.
4. Select a status – the LED changes immediately.


## MS Teams Integration

Click **MS Teams Integration: OFF** in the context menu to toggle automatic mode.

When enabled, the app tails the newest Teams log file at:

```
%LOCALAPPDATA%\Packages\MSTeams_*\LocalCache\Microsoft\MSTeams\Logs\MSTeams_*
```

It looks for `GlyphBadge {"state"}` entries and updates the LED automatically, exactly as the original Python script does.

When Teams closes (log entry `TelemetryService: Telemetry service stopped`) the light turns off.

## Project layout

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, WinForms bootstrap |
| `TrayApplication.cs` | `ApplicationContext` – tray icon, context menu, UI thread |
| `LightState.cs` | Colour/state definitions and Teams-state mapping |
| `TrinketController.cs` | WMI COM-port detection and serial write |
| `TeamsMonitor.cs` | Background Teams log tailer |
