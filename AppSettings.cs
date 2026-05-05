using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusylightTray;

/// <summary>
/// Persists user preferences to a JSON file next to the executable.
/// Settings are saved whenever a relevant value changes.
/// </summary>
public class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // ── Persisted properties ──────────────────────────────────────────────────

    /// <summary>Whether MS Teams integration is enabled.</summary>
    public bool TeamsEnabled { get; set; } = false;

    /// <summary>
    /// The last active light mode.
    /// "rainbow"        – rainbow sequence
    /// "sequence:&lt;name&gt;" – a saved sequence file (stem only, no extension)
    /// "color:&lt;key&gt;"    – a solid <see cref="LightColor.TeamsState"/> key
    /// null             – no mode was active (LEDs were off / never set)
    /// </summary>
    public string? LastMode { get; set; } = null;

    // ── Load / Save ───────────────────────────────────────────────────────────

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                       ?? new AppSettings();
            }
        }
        catch { /* corrupt / missing – fall back to defaults */ }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* best-effort */ }
    }
}
