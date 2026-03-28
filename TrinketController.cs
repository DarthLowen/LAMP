using System.IO.Ports;
using System.Management;

namespace BusylightTray;

/// <summary>
/// Handles discovery and communication with the Adafruit Trinkey Neo
/// (Hardware ID: VID_239A / PID_80EF).
///
/// Protocol: write "R,G,B\r" as ASCII text over the virtual COM port,
/// matching the Python script's  ser.write(b"R,G,B\r")  command.
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
}
