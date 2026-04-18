using System.Text.Json;

namespace BusylightTray;

/// <summary>
/// Shared utilities for the on-disk sequence store.
/// – <see cref="Folder"/> is the single canonical location used by both the
///   Sequence Editor (save / load dialogs) and the tray menu (discovery).
/// </summary>
internal static class SequenceFiles
{
    // ── Folder ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>sequences</c> sub-folder next to the running executable.
    /// Created on demand when the editor saves a file.
    /// </summary>
    public static readonly string Folder =
        Path.Combine(AppContext.BaseDirectory, "sequences");

    // ── DTOs (shared with the editor's own serialisation code) ────────────────

    internal sealed class FileDto
    {
        public List<StepDto> Steps { get; init; } = [];
    }

    internal sealed class StepDto
    {
        public string[] Leds   { get; init; } = [];
        public int      WaitMs { get; init; }
    }

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented           = true,
        PropertyNameCaseInsensitive = true,
    };

    // ── File helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all <c>*.json</c> files in <see cref="Folder"/>, sorted by name.
    /// Returns an empty array when the folder does not exist.
    /// </summary>
    public static string[] ListFiles() =>
        Directory.Exists(Folder)
            ? [.. Directory.GetFiles(Folder, "*.json")
                           .OrderBy(f => Path.GetFileNameWithoutExtension(f),
                                    StringComparer.OrdinalIgnoreCase)]
            : [];

    /// <summary>
    /// Loads a JSON sequence file and converts every valid step to a
    /// <see cref="Sequence"/> ready for <see cref="TrinketController.SendSequence"/>.
    /// Returns <see langword="null"/> when the file is missing, empty, or unparseable.
    /// </summary>
    public static IEnumerable<Sequence>? LoadAsSequences(string path)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOpts);
            if (dto is null || dto.Steps.Count == 0) return null;

            var result = new List<Sequence>();
            foreach (var step in dto.Steps)
            {
                if (step.Leds.Length < 4) continue;

                RgbColor[]? leds = ParseLeds(step.Leds);
                if (leds is null) continue;

                result.Add(new Sequence(leds[0], leds[1], leds[2], leds[3],
                                        Math.Max(1, step.WaitMs)));
            }

            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static RgbColor[]? ParseLeds(string[] hexColors)
    {
        var result = new RgbColor[4];
        for (int i = 0; i < 4; i++)
        {
            ReadOnlySpan<char> s = hexColors[i].AsSpan().TrimStart('#');
            if (s.Length != 6) return null;
            if (!int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
                return null;

            result[i] = new RgbColor(
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >>  8) & 0xFF),
                (byte)( rgb        & 0xFF));
        }
        return result;
    }
}
