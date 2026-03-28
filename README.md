# BusylightTray

A Windows system-tray application that controls the Adafruit Trinkey Neo RGB LED from a context menu, with optional automatic MS Teams presence integration.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Windows 10 / 11
- Adafruit Trinkey Neo (VID `239A` / PID `80EF`) loaded with firmware that accepts `R,G,B\r` over its virtual COM port

## Build & Run

```powershell
cd BusyLight\BusylightTray
dotnet run          # run directly from source
dotnet publish -c Release -r win-x64 --self-contained   # produce a single .exe
```

The published executable ends up in `bin\Release\net8.0-windows\win-x64\publish\`.

## Usage

1. Plug in the Trinkey.
2. Launch `BusylightTray.exe`. A grey dot appears in the system tray.
3. **Left-click or right-click** the tray icon to open the context menu.
4. Select a status – the LED changes immediately.

| Status         | LED colour |
|----------------|------------|
| Available      | 🟢 Green `(0, 150, 0)` |
| Busy           | 🔴 Red `(150, 0, 0)` |
| Do Not Disturb | 🔴 Red `(150, 0, 0)` |
| Away           | ⚫ Off |
| Be Right Back  | 🔵 Blue `(0, 0, 255)` |
| On The Phone   | 🔵 Blue `(0, 0, 255)` |
| Presenting     | 🔵 Blue `(0, 0, 255)` |
| In A Meeting   | 🔵 Blue `(0, 0, 255)` |
| Offline        | ⚫ Off |

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
