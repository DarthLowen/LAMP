using System.Drawing;

namespace BusylightTray;

/// <summary>
/// Represents one named status with its Teams log key and RGB LED values.
/// </summary>
public class LightColor(string displayName, string teamsState, byte r, byte g, byte b, bool showInPopup)
{
    public string DisplayName { get; } = displayName;

    /// <summary>Exact key as it appears between {"…"} in the MS Teams log.</summary>
    public string TeamsState { get; } = teamsState;

    public byte R { get; } = r;
    public byte G { get; } = g;
    public byte B { get; } = b;
    public bool ShowInPopup { get; } = showInPopup;

    /// <summary>True when the LED should be off (all channels zero).</summary>
    public bool IsOff => R == 0 && G == 0 && B == 0;

    /// <summary>
    /// Colour used when drawing the indicator dot in the UI.
    /// "Off" states show as dim grey so the dot is still visible.
    /// </summary>
    public Color DisplayColor =>
        IsOff ? Color.FromArgb(80, 80, 80) : Color.FromArgb(R, G, B);
}

/// <summary>All known status states, mirroring the Python GlyphBadge dictionary.</summary>
public static class LightStates
{
    public static readonly LightColor[] All =
    [
        new("Available",      "available",   0,   255, 0  , true),
        new("Busy",           "busy",        255, 0,   0  , false),
        new("Do Not Disturb", "doNotDistrb", 255, 0,   0  , true),
        new("Away",           "away",        255, 60,  0  , true),
        new("Be Right Back",  "beRightBack", 255, 60,  0  , false),
        new("On The Phone",   "onThePhone",  255, 0,   0  , false),
        new("Presenting",     "presenting",  255, 0,   0  , false),
        new("In A Meeting",   "inAMeeting",  255, 0,   0  , false),
        new("Janetten",       "janetten",    255, 0,   255, true),
        new("I'm blue",       "blue",        0,   0,   255, true),
        new("Offline",        "offline",     0,   0,   0  , true),
    ];

    /// <summary>
    /// Maps a raw Teams log state string (e.g. "available", "InAMeeting") to a
    /// <see cref="LightColor"/>. Matching is case-insensitive.
    /// </summary>
    public static LightColor? FromTeamsState(string state) =>
        All.FirstOrDefault(s =>
            string.Equals(s.TeamsState, state, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.DisplayName.Replace(" ", ""), state, StringComparison.OrdinalIgnoreCase));
}
