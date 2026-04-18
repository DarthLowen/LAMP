# Overview

A USB-controlled RGB status light built on the [Adafruit Neo Trinkey](https://www.adafruit.com/product/4870) (SAMD21 + 4 NeoPixels). The device appears as a USB serial port and accepts simple text commands to set static colors or animated sequences.

## Hardware

- **Adafruit Neo Trinkey** – SAMD21-based USB key with 4 NeoPixels and 2 capacitive touch pads  
  Product page: https://www.adafruit.com/product/4870

---

## Arduino IDE Setup

> Full reference: https://learn.adafruit.com/adafruit-neo-trinkey/arduino-ide-setup

### 1. Install Arduino IDE

Download and install **Arduino IDE 1.8 or later** from https://www.arduino.cc/en/software.

### 2. Add Adafruit Board Manager URL

1. Open Arduino IDE.
2. Go to **File → Preferences** (Windows/Linux) or **Arduino → Preferences** (macOS).
3. Find the **Additional Boards Manager URLs** field and add the following URL:

   ```
   https://adafruit.github.io/arduino-board-index/package_adafruit_index.json
   ```

   If you already have other URLs there, separate them with a comma.

4. Click **OK**.

### 3. Install Arduino SAMD Board Support

1. Go to **Tools → Board → Boards Manager**.
2. Search for **Arduino SAMD Boards**.
3. Install the latest version (1.6.11 or later).

### 4. Install Adafruit SAMD Board Support

1. In Boards Manager, change the filter to **All**.
2. Search for **Adafruit SAMD**.
3. Install the latest version.
4. **Quit and reopen** Arduino IDE to ensure the new boards are loaded.

### 5. Select the Board

1. Go to **Tools → Board → Adafruit SAMD Boards**.
2. Select **Adafruit Neo Trinkey M0**.

### 6. Set the USB Stack to TinyUSB

The sketch uses `Adafruit_TinyUSB.h` and requires the TinyUSB stack to be active.

1. With the Neo Trinkey board selected, go to **Tools → USB Stack**.
2. Select **TinyUSB**.

> If this option is missing, ensure the **Adafruit SAMD** board package is installed (not just the Arduino SAMD package).

### 7. Select the Port

Plug in the Neo Trinkey via USB. Go to **Tools → Port** and select the port labeled with the board name (e.g., `COM3 (Adafruit Neo Trinkey M0)` on Windows).

> **Tip – entering bootloader mode:** If the board is unresponsive or the port doesn't appear, double-click the reset button. The NeoPixels will pulse, indicating the bootloader is active. A new drive and COM port will appear – select that port before uploading.

---

## Required Libraries

Install all libraries via **Sketch → Include Library → Manage Libraries** (search by name).

| Library | Purpose |
|---|---|
| **Adafruit NeoPixel** | Driving the 4 onboard NeoPixels |
| **Adafruit FreeTouch** | Capacitive touch pad support |
| **Adafruit TinyUSB Library** | USB CDC/MSC device stack |
| **Adafruit Internal Flash** | Access to onboard QSPI/internal flash |
| **SdFat – Adafruit Fork** | FAT filesystem on internal flash |

> **SPI** is a built-in Arduino library and does not need to be installed separately.

---

## Uploading the Sketch

1. Open `01_Arduino_Sketch_BusyLight/Arduino_Sketch_BusyLight/Arduino_Sketch_BusyLight.ino` in Arduino IDE.
2. Confirm **Tools → Board** is set to **Adafruit Neo Trinkey M0** and the correct **Port** is selected.
3. Click **Upload** (→).
4. After a successful upload the NeoPixels will run a brief RGB startup animation and then turn off, indicating the device is ready.

### Precompiled Firmware (alternative)

If you don't want to build from source, drag the `.uf2` file from `03_Precompiled_Firmware/` onto the `TRINKETBOOT` drive that appears when the board is in bootloader mode.

---

## Serial Protocol

Connect to the USB serial port at **9600 baud**.

### Static color

Set all 4 LEDs to a single RGB color:

```
R,G,B\r
```

Example – solid red: `255,0,0`

### Animated sequence

Cycle through multiple steps indefinitely (repeats until the next command):

```
s;r1,g1,b1,r2,g2,b2,r3,g3,b3,r4,g4,b4;wait_ms;r1,g1,b1,...;wait_ms;...\r
```

- Each step defines all 4 LED colors followed by a hold time in milliseconds.
- The sequence repeats until a new command is received.

Example – slow red/green blink: `s;255,0,0,255,0,0,255,0,0,255,0,0;500;0,255,0,0,255,0,0,255,0,0,255,0;500`
