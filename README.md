# L.A.M.P.
### _Local Activity & Mood Peripheral_
> Because your colleagues somehow still can't tell when you don't want to be disturbed.

A Windows system-tray application that controls the Adafruit Trinkey Neo RGB LED from a context menu, with optional automatic MS Teams presence integration and a built-in sequence editor for custom animated lighting patterns.

> **Based on** [wolllis/busylight](https://github.com/wolllis/busylight) — the original Python-based busylight project that this C# app was derived from.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Windows 10 / 11
- Adafruit Trinkey Neo (VID `239A` / PID `80EF`) loaded with the firmware in the folder `firmware` (the Neo's default firmware doesn't work with this app, so you must flash it first using the instructions in the Adafruit guide: [Adafruit Trinkey Neo – NeoPixel Firmware](https://learn.adafruit.com/adafruit-trinkey-neo/firmware-neopixel))
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

### Overriding a Teams state with a sequence

By default each Teams state lights a fixed solid colour. You can replace any state with an animated sequence by saving a JSON file named after the state's key into the `sequences` folder:

| Teams state | Override file |
|-------------|---------------|
| Available | `sequences\available.json` |
| Busy | `sequences\busy.json` |
| Do Not Disturb | `sequences\doNotDistrb.json` |
| Away | `sequences\away.json` |
| Be Right Back | `sequences\beRightBack.json` |
| On The Phone | `sequences\onThePhone.json` |
| Presenting | `sequences\presenting.json` |
| In A Meeting | `sequences\inAMeeting.json` |
| Offline | `sequences\offline.json` |

Filename matching is case-insensitive. When Teams triggers a state change, the app checks the `sequences` folder first; if a matching file is found it sends that sequence to the Trinkey, otherwise it falls back to the built-in solid colour. Removing the file restores the default behaviour instantly — no restart required.

## Sequence Editor

Open the editor via **Sequence Editor…** in the tray context menu.

![Sequence Editor layout](docs/sequence-editor.png)

### Layout

| Area | Description |
|------|-------------|
| **Top strip** | Live NeoTrinkey visualisation — four LED circles arranged in the same 2 × 2 square as the physical device (LED 1 bottom-left, LED 2 top-left, LED 3 top-right, LED 4 bottom-right). Updates in real time as you edit or simulate. |
| **Steps list** | One row per step showing the colour of each LED and its hold time. Supports multi-row selection. |
| **Edit Step panel** | Click any LED colour button to open a colour picker, adjust the wait time, then press **Update Step** to commit the change. Colours can also be typed directly into any LED cell as a hex value (`#RRGGBB` or `RRGGBB`). |
| **Bottom bar** | **📂 Load…** / **💾 Save** for file I/O; **▶ Simulate** / **⏹ Stop** to preview the animation on screen; **Cancel** to close. |

### Editing steps

- **＋ Add** — inserts a new default step (all LEDs off, 100 ms) after the selected row, or appends if nothing is selected.
- **－ Delete** — removes the selected step.
- **▲ Up / ▼ Down** — reorders steps.
- **Click a colour cell** — an inline text box appears; type a hex colour and press Enter to commit, or Escape to cancel. The background turns red for invalid input.
- **Click a colour button** (right panel) — opens the full colour-picker dialog.
- **Update Step** — writes the right-panel values back to the selected row.

### Simulate

Press **▶ Simulate** to cycle through all steps on screen, holding each frame for its own wait time. The button becomes **⏹ Stop** — click it again to halt and restore the view to the selected step.

### Saving and loading sequences

Sequences are stored as human-readable JSON files in the `sequences` folder next to the executable:

```
<install dir>\sequences\my-sequence.json
```

Example file:

```json
{
  "steps": [
    { "leds": ["#FF0000", "#000000", "#000000", "#000000"], "waitMs": 200 },
    { "leds": ["#000000", "#00FF00", "#000000", "#000000"], "waitMs": 200 },
    { "leds": ["#000000", "#000000", "#0000FF", "#000000"], "waitMs": 200 },
    { "leds": ["#000000", "#000000", "#000000", "#FF00FF"], "waitMs": 200 }
  ]
}
```

Use **📂 Load…** / **💾 Save** in the editor to open or save files — both dialogs default to the `sequences` folder.

Saved sequences are also listed under **Sequences ▶** in the tray context menu. Clicking a name sends it straight to the device without opening the editor.

### Excel copy / paste

Select one or more rows and press **Ctrl+C** to copy them as tab-separated values. The format is:

```
LED1<tab>LED2<tab>LED3<tab>LED4<tab>WaitMs
```

Paste this into Excel to inspect or bulk-edit values, then copy the rows back and press **Ctrl+V** in the editor to insert them as new steps.

## Project layout

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, WinForms bootstrap |
| `TrayApplication.cs` | `ApplicationContext` – tray icon, context menu, UI thread |
| `LightState.cs` | Colour/state definitions and Teams-state mapping |
| `TrinketController.cs` | WMI COM-port detection and serial write |
| `TeamsMonitor.cs` | Background Teams log tailer |
| `SequenceEditorForm.cs` | Sequence editor GUI |
| `SequenceFiles.cs` | Shared JSON load/save utilities and sequences folder constant |
