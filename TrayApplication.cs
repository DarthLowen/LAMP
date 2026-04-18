using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BusylightTray;

/// <summary>
/// Windows system-tray application.
/// – Right-click (or left-click) opens a context menu with all light states.
/// – The tray icon colour reflects the active state.
/// – The MS Teams Integration option, when enabled, auto-updates the light
///   by tailing the Teams log.
/// </summary>
public class TrayApplication : ApplicationContext
{
    // Native call needed to prevent GDI handle leak when creating icons from bitmaps.
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly NotifyIcon  _trayIcon;
    private readonly TeamsMonitor _teamsMonitor;

    // WinForms Timer fires on the UI thread – safe to update UI from its Tick.
    private readonly System.Windows.Forms.Timer _pollTimer;

    // Background thread enqueues Teams states; UI thread dequeues via _pollTimer.
    private readonly ConcurrentQueue<string> _teamsQueue = new();

    private ToolStripMenuItem _teamsToggleItem = null!;
    private bool _teamsEnabled;

    // ── Constructor ──────────────────────────────────────────────────────────

    public TrayApplication()
    {
        _teamsMonitor = new TeamsMonitor();
        _teamsMonitor.StateChanged += s => _teamsQueue.Enqueue(s);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _pollTimer.Tick += (_, _) => DrainTeamsQueue();
        _pollTimer.Start();

        _trayIcon = new NotifyIcon
        {
            Icon               = CreateDotIcon(Color.FromArgb(80, 80, 80)),
            Text               = "BusyLight",
            Visible            = true,
            ContextMenuStrip   = BuildContextMenu(),
        };

        // Left-click also opens the context menu (right-click is automatic).
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _trayIcon.ContextMenuStrip!.Show(Cursor.Position);
        };
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        foreach (var state in LightStates.All)
        {
            var item = new ToolStripMenuItem(state.DisplayName, CreateDotBitmap(state))
            {
                Tag = state
            };
            item.Click += OnStateClicked;
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var rainbowItem = new ToolStripMenuItem("Rainbow Sequence");
        rainbowItem.Click += OnRainbowClicked;
        menu.Items.Add(rainbowItem);

        menu.Items.Add(new ToolStripSeparator());

        _teamsToggleItem = new ToolStripMenuItem("MS Teams Integration: OFF");
        _teamsToggleItem.Click += OnTeamsToggleClicked;
        menu.Items.Add(_teamsToggleItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnStateClicked(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem { Tag: LightColor state })
            ApplyState(state);
    }

    private void OnRainbowClicked(object? sender, EventArgs e)
    {
        TrinketController.SendSequence(RainbowSequence);

        var oldIcon = _trayIcon.Icon;
        _trayIcon.Icon = CreateDotIcon(Color.FromArgb(148, 0, 211)); // violet dot
        _trayIcon.Text = "BusyLight – Rainbow";
        oldIcon?.Dispose();
    }

    // Each step lights one LED with a rainbow colour while the others are off,
    // cycling LED0→LED1→LED2→LED3 through red→orange→yellow→green→cyan→blue→violet.
    private static IEnumerable<Sequence> RainbowSequence
    {
        get
        {
            // Rainbow hues evenly spread across 28 steps (7 colours × 4 LEDs).
            // The active LED follows the step index (step % 4 = active LED).
            (byte R, byte G, byte B)[] hues =
            [
                (255,   0,   0), // red
                (255, 127,   0), // orange
                (255, 255,   0), // yellow
                (  0, 255,   0), // green
                (  0, 255, 255), // cyan
                (  0,   0, 255), // blue
                (148,   0, 211), // violet
            ];

            var off = new RgbColor(0, 0, 0);

            for (int hueIdx = 0; hueIdx < hues.Length; hueIdx++)
            {
                var (r, g, b) = hues[hueIdx];
                var on = new RgbColor(r, g, b);

                for (int led = 0; led < 4; led++)
                {
                    var leds = new RgbColor[] { off, off, off, off };
                    leds[led] = on;
                    yield return new Sequence(leds[0], leds[1], leds[2], leds[3], waitMs: 100);
                }
            }
        }
    }

    private void OnTeamsToggleClicked(object? sender, EventArgs e)
    {
        _teamsEnabled = !_teamsEnabled;
        _teamsToggleItem.Text = _teamsEnabled
            ? "MS Teams Integration: ON  ✓"
            : "MS Teams Integration: OFF";

        if (_teamsEnabled)
            _teamsMonitor.Start();
        else
            _teamsMonitor.Stop();
    }

    // ── Teams queue drain (runs on UI thread via WinForms Timer) ─────────────

    private void DrainTeamsQueue()
    {
        if (!_teamsEnabled) return;

        while (_teamsQueue.TryDequeue(out string? raw))
        {
            LightColor? state = LightStates.FromTeamsState(raw);
            if (state is not null)
                ApplyState(state);
        }
    }

    // ── State application ─────────────────────────────────────────────────────

    private void ApplyState(LightColor state)
    {
        // Send colour to the Trinkey (fire-and-forget; failure is silently ignored).
        TrinketController.SendColor(state.R, state.G, state.B);

        // Update the tray icon and tooltip.
        var oldIcon = _trayIcon.Icon;
        _trayIcon.Icon = CreateDotIcon(state.DisplayColor);
        _trayIcon.Text = $"BusyLight – {state.DisplayName}";
        oldIcon?.Dispose();
    }

    // ── Icon / bitmap helpers ─────────────────────────────────────────────────

    private static Icon CreateDotIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
            DrawDot(g, color, new Rectangle(1, 1, 13, 13));

        // Clone so we own the Icon and can safely call DestroyIcon on the raw handle.
        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private static Bitmap CreateDotBitmap(LightColor state)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        DrawDot(g, state.DisplayColor, new Rectangle(1, 1, 13, 13));
        return bmp;
    }

    private static void DrawDot(Graphics g, Color color, Rectangle bounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var fill   = new SolidBrush(color);
        using var border = new Pen(Color.FromArgb(100, 0, 0, 0), 1f);
        g.FillEllipse(fill,   bounds);
        g.DrawEllipse(border, bounds);
    }

    // ── Shutdown ──────────────────────────────────────────────────────────────

    private void Shutdown()
    {
        _pollTimer.Stop();
        _teamsMonitor.Stop();
        _teamsMonitor.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _teamsMonitor.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
