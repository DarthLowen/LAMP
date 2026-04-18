/*********************************************************************
 Adafruit invests time and resources providing this open source code,
 please support Adafruit and open-source hardware by purchasing
 products from Adafruit!

 MIT license, check LICENSE for more information
 Copyright (c) 2019 Ha Thach for Adafruit Industries
 All text above, and the splash screen below must be included in
 any redistribution
*********************************************************************/

#include "SPI.h"
#include "SdFat.h"
#include "Adafruit_InternalFlash.h"
#include "Adafruit_TinyUSB.h"

#include <Adafruit_NeoPixel.h>
#include "Adafruit_FreeTouch.h"

// Start address and size should matches value in the CircuitPython (INTERNAL_FLASH_FILESYSTEM = 1)
// to make it easier to switch between Arduino and CircuitPython
#define INTERNAL_FLASH_FILESYSTEM_START_ADDR  (0x00040000 - 256 - 0 - INTERNAL_FLASH_FILESYSTEM_SIZE)
#define INTERNAL_FLASH_FILESYSTEM_SIZE        (128*1024)

// Internal Flash object
Adafruit_InternalFlash flash(INTERNAL_FLASH_FILESYSTEM_START_ADDR, INTERNAL_FLASH_FILESYSTEM_SIZE);

// file system object from SdFat
FatFileSystem fatfs;

FatFile root;
FatFile file;

// USB MSC object
Adafruit_USBD_MSC usb_msc;

// Set to true when PC write to flash
bool fs_changed;

// Create the neopixel strip with the built in definitions NUM_NEOPIXEL and PIN_NEOPIXEL
Adafruit_NeoPixel strip = Adafruit_NeoPixel(NUM_NEOPIXEL, PIN_NEOPIXEL, NEO_GRB + NEO_KHZ800);

// Create the two touch pads on pins 1 and 2:
Adafruit_FreeTouch qt_1 = Adafruit_FreeTouch(1, OVERSAMPLE_4, RESISTOR_50K, FREQ_MODE_NONE);
Adafruit_FreeTouch qt_2 = Adafruit_FreeTouch(2, OVERSAMPLE_4, RESISTOR_50K, FREQ_MODE_NONE);

// ---------------------------------------------------------------------------
// Serial protocol
//   Static color : "R,G,B\r"        – sets all 4 LEDs to rgb(R,G,B)
//   Sequence     : "s;r1,g1,b1,r2,g2,b2,r3,g3,b3,r4,g4,b4;wait_ms;...\r"
//                  Repeats indefinitely until the next message arrives.
// ---------------------------------------------------------------------------

#define SERIAL_BUFFER_SIZE 1024
#define MAX_STEPS          32

struct LedColor {
  uint8_t r, g, b;
};

struct SequenceStep {
  LedColor   leds[4];
  uint16_t   wait_ms;
};

static char         serialBuf[SERIAL_BUFFER_SIZE];
static int          serialBufLen   = 0;

static SequenceStep sequence[MAX_STEPS];
static int          sequenceLength = 0;
static bool         inSequenceMode = false;
static int          currentStep    = 0;
static unsigned long stepStartTime = 0;

// Forward declarations
void processCommand(char* buf);
void parseSequence(char* buf);
void applyStep(int step);

// the setup function runs once when you press reset or power the board
void setup()
{
  // Initialize internal flash
  flash.begin();

  // Set disk vendor id, product id and revision with string up to 8, 16, 4 characters respectively
  usb_msc.setID("Adafruit", "Internal Flash", "1.0");

  // Set callback
  usb_msc.setReadWriteCallback(msc_read_callback, msc_write_callback, msc_flush_callback);
  usb_msc.setWritableCallback(msc_writable_callback);

  // Set disk size, block size should be 512 regardless of flash page size
  usb_msc.setCapacity(flash.size()/512, 512);

  // Set Lun ready
  usb_msc.setUnitReady(true);

  usb_msc.begin();

  // Init file system on the flash
  fatfs.begin(&flash);

  Serial.begin(9600);

  fs_changed = true; // to print contents initially
  
  strip.begin();
  strip.setBrightness(255);
  strip.show(); // Initialize all pixels to 'off'

  for(int i = 0; i < 4 ; i++)
  {
    Spin(strip.Color(100,0,0));
    delay(100);
  }
  
  for(int i = 0; i < 4 ; i++)
  {
    Spin(strip.Color(0,100,0));
    delay(100);
  }
  
  for(int i = 0; i < 4 ; i++)
  {
    Spin(strip.Color(0,0,100));
    delay(100);
  }
  
  strip.setPixelColor(0, 0);
  strip.setPixelColor(1, 0);
  strip.setPixelColor(2, 0);
  strip.setPixelColor(3, 0);
  strip.show();
}

void loop()
{
  // ---- Non-blocking serial accumulation ----
  while (Serial.available() > 0)
  {
    char c = (char)Serial.read();
    if (c == '\r' || c == '\n')
    {
      if (serialBufLen > 0)
      {
        serialBuf[serialBufLen] = '\0';
        processCommand(serialBuf);
        serialBufLen = 0;
      }
    }
    else if (serialBufLen < SERIAL_BUFFER_SIZE - 1)
    {
      serialBuf[serialBufLen++] = c;
    }
    else
    {
      // Buffer overflow – discard and reset
      serialBufLen = 0;
    }
  }

  // ---- Sequence playback ----
  if (inSequenceMode && sequenceLength > 0)
  {
    unsigned long now = millis();
    if (now - stepStartTime >= sequence[currentStep].wait_ms)
    {
      stepStartTime += sequence[currentStep].wait_ms;
      currentStep = (currentStep + 1) % sequenceLength;
      applyStep(currentStep);
    }
  }
}

// ---------------------------------------------------------------------------
// processCommand – dispatches to static-color or sequence handler
// ---------------------------------------------------------------------------
void processCommand(char* buf)
{
  if (buf[0] == 's')
  {
    parseSequence(buf);
  }
  else
  {
    // Static color: "R,G,B"
    inSequenceMode = false;
    char* save;
    char* token = strtok_r(buf, ",", &save);
    if (!token) return;
    int r = constrain(atoi(token), 0, 255);
    token = strtok_r(NULL, ",", &save);
    if (!token) return;
    int g = constrain(atoi(token), 0, 255);
    token = strtok_r(NULL, ",", &save);
    if (!token) return;
    int b = constrain(atoi(token), 0, 255);

    for (int i = 0; i < 4; i++)
      strip.setPixelColor(i, strip.Color(r, g, b));
    strip.show();
  }
}

// ---------------------------------------------------------------------------
// parseSequence – parses "s;r1,g1,b1,...,b4;wait_ms;..." and starts playback
// ---------------------------------------------------------------------------
void parseSequence(char* buf)
{
  sequenceLength = 0;

  char* outerSave;
  char* token = strtok_r(buf, ";", &outerSave);
  if (!token || token[0] != 's') return;

  while (sequenceLength < MAX_STEPS)
  {
    char* colorStr = strtok_r(NULL, ";", &outerSave);
    if (!colorStr) break;
    char* waitStr  = strtok_r(NULL, ";", &outerSave);
    if (!waitStr) break;

    // Parse 12 colour values (4 LEDs × RGB)
    int vals[12];
    int count = 0;
    char* innerSave;
    char* v = strtok_r(colorStr, ",", &innerSave);
    while (v && count < 12)
    {
      vals[count++] = atoi(v);
      v = strtok_r(NULL, ",", &innerSave);
    }
    if (count < 12) break; // incomplete step – stop parsing

    for (int led = 0; led < 4; led++)
    {
      sequence[sequenceLength].leds[led].r = (uint8_t)constrain(vals[led * 3],     0, 255);
      sequence[sequenceLength].leds[led].g = (uint8_t)constrain(vals[led * 3 + 1], 0, 255);
      sequence[sequenceLength].leds[led].b = (uint8_t)constrain(vals[led * 3 + 2], 0, 255);
    }
    sequence[sequenceLength].wait_ms = (uint16_t)constrain(atoi(waitStr), 1, 65535);
    sequenceLength++;
  }

  if (sequenceLength > 0)
  {
    inSequenceMode  = true;
    currentStep     = 0;
    applyStep(0);
    stepStartTime   = millis();
  }
}

// ---------------------------------------------------------------------------
// applyStep – writes one sequence step to the LED strip
// ---------------------------------------------------------------------------
void applyStep(int step)
{
  for (int i = 0; i < 4; i++)
  {
    strip.setPixelColor(i, strip.Color(
      sequence[step].leds[i].r,
      sequence[step].leds[i].g,
      sequence[step].leds[i].b
    ));
  }
  strip.show();
}

// Callback invoked when received READ10 command.
// Copy disk's data to buffer (up to bufsize) and
// return number of copied bytes (must be multiple of block size)
int32_t msc_read_callback (uint32_t lba, void* buffer, uint32_t bufsize)
{
  // Note: InternalFlash Block API: readBlocks/writeBlocks/syncBlocks
  // already include sector caching (if needed). We don't need to cache it, yahhhh!!
  return flash.readBlocks(lba, (uint8_t*) buffer, bufsize/512) ? bufsize : -1;
}

// Callback invoked when received WRITE10 command.
// Process data in buffer to disk's storage and
// return number of written bytes (must be multiple of block size)
int32_t msc_write_callback (uint32_t lba, uint8_t* buffer, uint32_t bufsize)
{
  // Note: InternalFlash Block API: readBlocks/writeBlocks/syncBlocks
  // already include sector caching (if needed). We don't need to cache it, yahhhh!!
  return flash.writeBlocks(lba, buffer, bufsize/512) ? bufsize : -1;
}

// Callback invoked when WRITE10 command is completed (status received and accepted by host).
// used to flush any pending cache.
void msc_flush_callback (void)
{
  // sync with flash
  flash.syncBlocks();

  // clear file system's cache to force refresh
  fatfs.cacheClear();

  fs_changed = true;
}

// Invoked to check if device is writable as part of SCSI WRITE10
// Default mode is writable
bool msc_writable_callback(void)
{
  // true for writable, false for read-only
  return true;
}

void Spin(uint32_t color) 
{
  static int position = 0;
  switch (position)
  {
    case 0:
      strip.setPixelColor(0, color);
      strip.setPixelColor(1, 0);
      strip.setPixelColor(2, 0);
      strip.setPixelColor(3, 0);
      break;
    case 1:
      strip.setPixelColor(0, 0);
      strip.setPixelColor(1, color);
      strip.setPixelColor(2, 0);
      strip.setPixelColor(3, 0);
      break;
    case 2:
      strip.setPixelColor(0, 0);
      strip.setPixelColor(1, 0);
      strip.setPixelColor(2, color);
      strip.setPixelColor(3, 0);
      break;
    case 3:
      strip.setPixelColor(0, 0);
      strip.setPixelColor(1, 0);
      strip.setPixelColor(2, 0);
      strip.setPixelColor(3, color);
      break;
    default:
      strip.setPixelColor(0, 0);
      strip.setPixelColor(1, 0);
      strip.setPixelColor(2, 0);
      strip.setPixelColor(3, 0);
      break;
  }

  position++;
  position %= 4;
  strip.show();
}
