using System.IO.Ports;
using System.Management;
using System.Text;

namespace BusylightTray;

/// <summary>Single LED colour.</summary>
public record RgbColor(byte R, byte G, byte B);

/// <summary>
/// One step in a repeating LED sequence: colours for all 4 LEDs and
/// how long (in milliseconds) to hold them before advancing.
/// </summary>
public class Sequence
{
    /// <summary>Colours for the 4 NeoPixels (index 0-3).</summary>
    public RgbColor[] Leds { get; }

    /// <summary>Time to hold this step, in milliseconds (≥ 1).</summary>
    public int WaitMs { get; }

    /// <param name="led1">Colour of LED 0.</param>
    /// <param name="led2">Colour of LED 1.</param>
    /// <param name="led3">Colour of LED 2.</param>
    /// <param name="led4">Colour of LED 3.</param>
    /// <param name="waitMs">Hold time in milliseconds.</param>
    public Sequence(RgbColor led1, RgbColor led2, RgbColor led3, RgbColor led4, int waitMs)
    {
        if (waitMs < 1) throw new ArgumentOutOfRangeException(nameof(waitMs), "Must be ≥ 1 ms.");
        Leds   = [led1, led2, led3, led4];
        WaitMs = waitMs;
    }
}

/// <summary>
/// Handles discovery and communication with the Adafruit Trinkey Neo
/// (Hardware ID: VID_239A / PID_80EF).
///
/// Protocols:
///   Static colour : "R,G,B\r"
///   Sequence      : "s;r1,g1,b1,r2,g2,b2,r3,g3,b3,r4,g4,b4;wait_ms;...\r"
/// </summary>
public static class TrinketController
{
    private const string VendorId  = "VID_239A";
    private const string ProductId = "PID_80EF";

    /// <summary>
    /// Searches WMI for a COM port whose hardware ID matches the Trinkey.
    /// Returns the port name (e.g. "COM3") or null if not found.
    /// </summary>
    public static string? FindComPort()
    {
        try
        {
            // Query all PnP Entities that have a COM port assignment.
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Caption FROM Win32_PnPEntity " +
                "WHERE Caption LIKE '%(COM%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                string? deviceId = obj["DeviceID"]?.ToString();
                string? caption  = obj["Caption"]?.ToString();

                if (deviceId is null || caption is null) continue;

                if (!deviceId.Contains(VendorId,  StringComparison.OrdinalIgnoreCase) ||
                    !deviceId.Contains(ProductId, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Caption looks like "Adafruit Trinkey Neo (COM3)"
                int openParen  = caption.IndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                int closeParen = openParen >= 0 ? caption.IndexOf(')', openParen) : -1;

                if (openParen >= 0 && closeParen > openParen)
                    return caption[(openParen + 1)..closeParen];
            }
        }
        catch { /* device or WMI unavailable */ }

        return null;
    }

    /// <summary>
    /// Sends an RGB colour command to the Trinkey.
    /// Returns true on success, false if the device was not found or the write failed.
    /// </summary>
    public static bool SendColor(byte r, byte g, byte b)
    {
        string? port = FindComPort();
        if (port is null) return false;

        try
        {
            using var serial = new SerialPort(port);
            serial.Open();
            serial.Write($"{r},{g},{b}\r");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a repeating colour sequence to the Trinkey.
    /// The device will loop through the steps indefinitely until the next command arrives.
    /// Returns true on success, false if the device was not found or the write failed.
    /// </summary>
    /// <param name="steps">Sequence steps to transmit (at least one required).</param>
    public static bool SendSequence(IEnumerable<Sequence> steps)
    {
        var stepList = steps as IList<Sequence> ?? [.. steps];
        if (stepList.Count == 0) return false;

        string? port = FindComPort();
        if (port is null) return false;

        // Build: s;r1,g1,b1,r2,g2,b2,r3,g3,b3,r4,g4,b4;wait_ms;...
        var sb = new StringBuilder("s");
        foreach (var step in stepList)
        {
            sb.Append(';');
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(step.Leds[i].R).Append(',')
                  .Append(step.Leds[i].G).Append(',')
                  .Append(step.Leds[i].B);
            }
            sb.Append(';').Append(step.WaitMs);
        }
        sb.Append('\r');

        try
        {
            using var serial = new SerialPort(port);
            serial.Open();
            serial.Write(sb.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
