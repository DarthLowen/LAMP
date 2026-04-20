namespace BusylightTray;

/// <summary>
/// Monitors the MS Teams log files for GlyphBadge status changes, exactly
/// replicating the logic of the original Python script.
///
/// Log location:
///   %LOCALAPPDATA%\Packages\MSTeams_*\LocalCache\Microsoft\MSTeams\Logs\MSTeams_*
///
/// Relevant log line format:
///   ... GlyphBadge {"available"} ...
/// </summary>
public class TeamsMonitor : IDisposable
{
    /// <summary>
    /// Fired (from a background thread) whenever a new Teams availability
    /// state is detected.  The argument is the raw state key, e.g. "available".
    /// </summary>
    public event Action<string>? StateChanged;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public void Start()
    {
        Stop(); // cancel any previous run
        _cts = new CancellationTokenSource();
        Task.Run(() => MonitorLoop(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    // ── Main loop ────────────────────────────────────────────────────────────

    private void MonitorLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? logFile = FindLogFile();
            if (logFile is null)
            {
                Thread.Sleep(5_000);
                continue;
            }

            // Emit the last known state from the current log file on first read.
            string? initial = ReadLatestState(logFile);
            if (initial is not null)
                StateChanged?.Invoke(initial);

            // Tail the file for live updates.
            TailLogFile(logFile, ct);
        }
    }

    /// <summary>Follows new lines appended to <paramref name="logFile"/>.</summary>
    private void TailLogFile(string logFile, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(
                logFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            // Start from the end – we only care about new entries.
            stream.Seek(0, SeekOrigin.End);

            while (!ct.IsCancellationRequested)
            {
                // If Teams has switched to a newer log file, restart the outer loop.
                if (FindLogFile() != logFile) return;

                string? line = reader.ReadLine();
                if (line is null)
                {
                    Thread.Sleep(500);
                    continue;
                }

                string? state = ParseGlyphBadge(line);
                if (state is not null)
                    StateChanged?.Invoke(state);

                if (line.Contains("TelemetryService: Telemetry service stopped",
                                  StringComparison.Ordinal))
                    StateChanged?.Invoke("offline");
            }
        }
        catch { /* log file deleted / inaccessible – outer loop will retry */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? ReadLatestState(string logFile)
    {
        try
        {
            string? last = null;
            using var stream = new FileStream(
                logFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                string? s = ParseGlyphBadge(line);
                if (s is not null) last = s;
            }
            return last;
        }
        catch { return null; }
    }

    /// <summary>
    /// Extracts the state key from a log line containing GlyphBadge {"state"}.
    /// Mirrors the Python:  start = line.find("{\"") + 2 ; end = line.find("\"}")
    /// </summary>
    private static string? ParseGlyphBadge(string line)
    {
        if (line.Contains("GlyphBadge", StringComparison.Ordinal))
        {
            int start = line.IndexOf("{\"", StringComparison.Ordinal);
            int end = line.IndexOf("\"}", StringComparison.Ordinal);

            if (start >= 0 && end > start + 2)
                return line[(start + 2)..end];
        }
        else if (line.Contains("native_modules::TaskbarModule: ShowBadge New Badge",
                                  StringComparison.Ordinal))
        {
            string matchWord = ", status ";
            int start = line.LastIndexOf(matchWord, StringComparison.Ordinal);
            int end = line.Length;

            if (start >= 0 && end > start + 2)
                return line[(start + matchWord.Length)..end];
        }

        return null;
    }

    /// <summary>
    /// Returns the path to the newest MSTeams_ log file, or null if not found.
    /// Path: %LOCALAPPDATA%\Packages\MSTeams_*\LocalCache\Microsoft\MSTeams\Logs\MSTeams_*
    /// </summary>
    private static string? FindLogFile()
    {
        try
        {
            string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (localAppData is null) return null;

            string packagesPath = Path.Combine(localAppData, "Packages");
            if (!Directory.Exists(packagesPath)) return null;

            string? teamsPackage = Directory
                .GetDirectories(packagesPath, "MSTeams*")
                .FirstOrDefault();
            if (teamsPackage is null) return null;

            string logsPath = Path.Combine(
                teamsPackage, "LocalCache", "Microsoft", "MSTeams", "Logs");
            if (!Directory.Exists(logsPath)) return null;

            // Sort by filename descending to get the newest log (filenames are date-prefixed).
            return Directory
                .GetFiles(logsPath, "MSTeams_*")
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
