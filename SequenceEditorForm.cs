using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace BusylightTray;

/// <summary>
/// GUI for programming a multi-step LED colour sequence.
/// – Top panel  : live NeoTrinkey visualisation (4 LED circles).
/// – Centre-left : ListView listing every step (one row per step).
/// – Centre-right: Per-step editor (4 colour pickers + wait-time spinner).
/// – Bottom      : Simulate / Cancel / Save actions.
/// </summary>
public class SequenceEditorForm : Form
{
    // ── Step data model ───────────────────────────────────────────────────────

    /// <summary>Immutable snapshot of one sequence step.</summary>
    private sealed record SequenceStep(Color Led1, Color Led2, Color Led3, Color Led4, int WaitMs)
    {
        public static SequenceStep Default =>
            new(Color.Black, Color.Black, Color.Black, Color.Black, WaitMs: 100);

        public Color LedAt(int index) => index switch
        {
            0 => Led1, 1 => Led2, 2 => Led3, _ => Led4,
        };
    }

    private readonly List<SequenceStep> _steps = [];

    // Tracks the colours currently shown in the right-hand editor panel.
    private readonly Color[] _editorColors = [Color.Black, Color.Black, Color.Black, Color.Black];

    // ── Controls ──────────────────────────────────────────────────────────────

    private Panel         _ledPreviewPanel  = null!;
    private ListView      _stepsListView    = null!;
    private Button        _btnAddStep       = null!;
    private Button        _btnDeleteStep    = null!;
    private Button        _btnMoveUp        = null!;
    private Button        _btnMoveDown      = null!;
    private readonly Button[] _ledColorBtns = new Button[4];
    private NumericUpDown _waitSpinner      = null!;
    private Button        _btnUpdateStep    = null!;
    private Button        _btnSimulate      = null!;
    private Button        _btnCancel        = null!;
    private Button        _btnSave          = null!;
    private Button        _btnLoad          = null!;

    // Floating TextBox that hovers over a colour cell when the user clicks it.
    private TextBox _inlineEditor   = null!;
    private int     _editItemIdx    = -1;   // row being edited
    private int     _editSubItemIdx = -1;   // column being edited (1-4 = LED 1-4)

    // Animation state for the Simulate button.
    private readonly System.Windows.Forms.Timer _animTimer   = new();
    private readonly Color[] _simulColors                    = [Color.Black, Color.Black, Color.Black, Color.Black];
    private int  _animStepIdx;
    private bool _isSimulating;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SequenceEditorForm()
    {
        BuildForm();
    }

    // ── Form construction ─────────────────────────────────────────────────────

    private void BuildForm()
    {
        Text            = "Sequence Editor – BusyLight";
        Size            = new Size(800, 560);
        MinimumSize     = new Size(680, 480);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        // Controls are docked in reverse collection order (last added = processed first).
        // Add Fill first so Top/Bottom panels carve their space before Fill claims the rest.
        BuildMainSplit();        // Dock=Fill   → processed last  → takes remaining centre
        BuildBottomBar();        // Dock=Bottom → processed second → reserves bottom strip
        BuildLedPreviewPanel();  // Dock=Top    → processed first  → reserves top strip

        WireEvents();
    }

    // ── Top panel: NeoTrinkey LED preview ─────────────────────────────────────

    private void BuildLedPreviewPanel()
    {
        _ledPreviewPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 130,
            BackColor = Color.FromArgb(22, 22, 22),
            Padding   = new Padding(0),
        };
        _ledPreviewPanel.Paint += OnLedPreviewPaint;

        Controls.Add(_ledPreviewPanel);
    }

    /// <summary>
    /// Draws the four LEDs in a 2×2 square that mirrors the physical NeoTrinkey layout:
    ///   LED 2 (top-left)    LED 3 (top-right)
    ///   LED 1 (bottom-left) LED 4 (bottom-right)
    /// </summary>
    private void OnLedPreviewPaint(object? sender, PaintEventArgs e)
    {
        var g    = e.Graphics;
        var size = _ledPreviewPanel.ClientSize;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(22, 22, 22));

        const int diam    = 38;
        const int gap     = 12;
        const int block   = diam * 2 + gap;   // total side length of the 2×2 square
        const int labelH  = 16;               // height reserved for the board label

        int startX = (size.Width  - block) / 2;
        int startY = (size.Height - block - labelH) / 2;

        // (col, row) grid positions indexed by LED number (0-based).
        // Row 0 = top, Row 1 = bottom; Col 0 = left, Col 1 = right.
        (int col, int row)[] positions = [(0, 1), (0, 0), (1, 0), (1, 1)];

        for (int i = 0; i < 4; i++)
        {
            var (col, row) = positions[i];
            int x = startX + col * (diam + gap);
            int y = startY + row * (diam + gap);

            // Outer ring (PCB pad illusion)
            using var padBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
            g.FillEllipse(padBrush, x - 3, y - 3, diam + 6, diam + 6);

            // LED body
            var  ledColor  = _isSimulating ? _simulColors[i] : _editorColors[i];
            bool isOff     = ledColor.R == 0 && ledColor.G == 0 && ledColor.B == 0;
            var  bodyColor = isOff ? Color.FromArgb(55, 55, 55) : ledColor;
            using var ledBrush = new SolidBrush(bodyColor);
            g.FillEllipse(ledBrush, x, y, diam, diam);

            // Border
            using var border = new Pen(Color.FromArgb(90, 90, 90), 1f);
            g.DrawEllipse(border, x, y, diam, diam);

            // Specular highlight for lit LEDs
            if (!isOff)
            {
                using var hi = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
                g.FillEllipse(hi, x + diam * 0.2f, y + diam * 0.1f, diam * 0.35f, diam * 0.28f);
            }

            // LED index label
            using var font  = new Font("Segoe UI", 7f);
            using var lbl   = new SolidBrush(Color.FromArgb(150, 150, 150));
            string    text  = (i + 1).ToString();
            var       tsz   = g.MeasureString(text, font);
            g.DrawString(text, font, lbl,
                         x + (diam - tsz.Width)  / 2,
                         y + (diam - tsz.Height) / 2);
        }

        // Board label centred below the square
        using var lblFont  = new Font("Segoe UI", 7f);
        using var lblBrush = new SolidBrush(Color.FromArgb(110, 110, 110));
        string boardText   = "NeoTrinkey";
        var    boardSz     = g.MeasureString(boardText, lblFont);
        g.DrawString(boardText, lblFont, lblBrush,
                     (size.Width - boardSz.Width) / 2,
                     startY + block + 4);
    }

    // ── Bottom bar: Simulate / Cancel / Save ──────────────────────────────────

    private void BuildBottomBar()
    {
        var bottomPanel = new Panel
        {
            Dock   = DockStyle.Bottom,
            Height = 48,
        };

        _btnSimulate = MakeButton("▶  Simulate", 110);
        _btnCancel   = MakeButton("Cancel",       80);
        _btnSave     = MakeButton("💾  Save",      90);
        _btnLoad     = MakeButton("📂  Load…",     90);

        // Left-aligned: Load
        _btnLoad.Anchor   = AnchorStyles.Left | AnchorStyles.Top;
        _btnLoad.Location = new Point(10, 11);

        // Right-align the three action buttons
        _btnSave.Anchor     = AnchorStyles.Right | AnchorStyles.Top;
        _btnCancel.Anchor   = AnchorStyles.Right | AnchorStyles.Top;
        _btnSimulate.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        _btnSave.Location     = new Point(bottomPanel.Width - _btnSave.Width - 10,     11);
        _btnCancel.Location   = new Point(bottomPanel.Width - _btnSave.Width - _btnCancel.Width - 18,  11);
        _btnSimulate.Location = new Point(bottomPanel.Width - _btnSave.Width - _btnCancel.Width - _btnSimulate.Width - 26, 11);

        bottomPanel.Controls.AddRange([_btnLoad, _btnSimulate, _btnCancel, _btnSave]);

        // Separator line at top of bottom bar
        var sep = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 1,
            BackColor = SystemColors.ControlDark,
        };
        bottomPanel.Controls.Add(sep);

        Controls.Add(bottomPanel);
    }

    // ── Main split: step list (left) + step editor (right) ────────────────────

    private void BuildMainSplit()
    {
        var split = new SplitContainer
        {
            Width         = 760,
            Dock          = DockStyle.Fill,
            Panel1MinSize = 280,
            Panel2MinSize = 220,
        };

        BuildStepsPanel(split.Panel1);
        BuildEditPanel(split.Panel2);

        Controls.Add(split);

        // Set AFTER adding to Controls so the docked width is known
        split.SplitterDistance = 440;
    }

    // ── Left panel: ListView + step manipulation buttons ──────────────────────

    private void BuildStepsPanel(SplitterPanel parent)
    {
        var group = new GroupBox
        {
            Text    = "Steps",
            Dock    = DockStyle.Fill,
            Padding = new Padding(6),
        };

        // ── ListView ──────────────────────────────────────────────────────────
        _stepsListView = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = true,
            MultiSelect   = true,
            HideSelection = false,
        };

        _stepsListView.Columns.Add("#",        38,  HorizontalAlignment.Center);
        _stepsListView.Columns.Add("LED 1",    72,  HorizontalAlignment.Center);
        _stepsListView.Columns.Add("LED 2",    72,  HorizontalAlignment.Center);
        _stepsListView.Columns.Add("LED 3",    72,  HorizontalAlignment.Center);
        _stepsListView.Columns.Add("LED 4",    72,  HorizontalAlignment.Center);
        _stepsListView.Columns.Add("Wait (ms)", -2, HorizontalAlignment.Right);

        // ── Step manipulation buttons ─────────────────────────────────────────
        var btnBar = new Panel
        {
            Dock   = DockStyle.Bottom,
            Height = 38,
        };

        _btnAddStep    = MakeButton("＋  Add",    90);
        _btnDeleteStep = MakeButton("－  Delete", 90);
        _btnMoveUp     = MakeButton("▲  Up",      76);
        _btnMoveDown   = MakeButton("▼  Down",    76);

        _btnAddStep.Location    = new Point(0,   6);
        _btnDeleteStep.Location = new Point(96,  6);
        _btnMoveUp.Location     = new Point(196, 6);
        _btnMoveDown.Location   = new Point(278, 6);

        btnBar.Controls.AddRange([_btnAddStep, _btnDeleteStep, _btnMoveUp, _btnMoveDown]);

        // Floating inline editor – parented to the group so coordinates are local.
        _inlineEditor = new TextBox
        {
            Visible     = false,
            BorderStyle = BorderStyle.FixedSingle,
            MaxLength   = 7,
            Font        = _stepsListView.Font,
        };
        _inlineEditor.KeyDown    += OnInlineEditorKeyDown;
        _inlineEditor.LostFocus  += (_, _) => CommitInlineEdit();
        _inlineEditor.TextChanged += OnInlineEditorTextChanged;
        _inlineEditor.KeyPress   += OnInlineEditorKeyPress;

        group.Controls.Add(_stepsListView);
        group.Controls.Add(btnBar);
        group.Controls.Add(_inlineEditor);
        parent.Controls.Add(group);
    }

    // ── Right panel: per-step editor ──────────────────────────────────────────

    private void BuildEditPanel(SplitterPanel parent)
    {
        var group = new GroupBox
        {
            Text    = "Edit Step",
            Dock    = DockStyle.Fill,
            Padding = new Padding(10, 14, 10, 8),
        };

        var table = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 6,   // LED 1-4 + Wait + Update button
            Padding     = new Padding(0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 5; i++)
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Update row stretches

        // ── LED colour picker rows ─────────────────────────────────────────────
        for (int i = 0; i < 4; i++)
        {
            table.Controls.Add(MakeLabel($"LED {i + 1}:"), 0, i);

            var btn = new Button
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Text      = "#000000",
                FlatStyle = FlatStyle.Flat,
                Margin    = new Padding(2, 4, 2, 4),
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            _ledColorBtns[i] = btn;
            table.Controls.Add(btn, 1, i);
        }

        // ── Wait-time row ─────────────────────────────────────────────────────
        table.Controls.Add(MakeLabel("Wait (ms):"), 0, 4);
        _waitSpinner = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 60_000,
            Value   = 100,
            Dock    = DockStyle.Fill,
            Margin  = new Padding(2, 6, 2, 4),
        };
        table.Controls.Add(_waitSpinner, 1, 4);

        // ── Update Step button ────────────────────────────────────────────────
        _btnUpdateStep = new Button
        {
            Text   = "Update Step",
            Dock   = DockStyle.Bottom,
            Height = 28,
            Margin = new Padding(0, 6, 0, 0),
        };
        table.SetColumnSpan(_btnUpdateStep, 2);
        table.Controls.Add(_btnUpdateStep, 0, 5);

        group.Controls.Add(table);
        parent.Controls.Add(group);
    }

    // ── Event wiring ──────────────────────────────────────────────────────────

    private void WireEvents()
    {
        _btnAddStep.Click    += (_, _) => AddStep();
        _btnDeleteStep.Click += (_, _) => DeleteStep();
        _btnMoveUp.Click     += (_, _) => MoveStep(-1);
        _btnMoveDown.Click   += (_, _) => MoveStep(+1);

        _stepsListView.SelectedIndexChanged += (_, _) =>
        {
            UpdateButtonStates();
            LoadStepIntoEditor(SelectedIndex);
        };

        _stepsListView.MouseClick += OnListViewMouseClick;
        _stepsListView.KeyDown     += OnListViewKeyDown;

        for (int i = 0; i < 4; i++)
        {
            int captured = i;
            _ledColorBtns[i].Click += (_, _) => PickLedColor(captured);
        }

        _btnUpdateStep.Click += (_, _) => UpdateStep();

        _btnSimulate.Click += (_, _) => { if (_isSimulating) StopSimulation(); else StartSimulation(); };
        _btnCancel.Click   += (_, _) => OnCancelClicked();
        _btnSave.Click     += (_, _) => SaveToJson();
        _btnLoad.Click     += (_, _) => LoadFromJson();
        _animTimer.Tick    += OnAnimTick;

        // Initialise button states (all disabled until a step exists).
        UpdateButtonStates();
    }

    // ── Step-list operations ───────────────────────────────────────────────────

    /// <summary>
    /// Inserts a default step immediately after the selected row,
    /// or appends at the end when nothing is selected.
    /// </summary>
    private void AddStep()
    {
        int insertAt = SelectedIndex >= 0 ? SelectedIndex + 1 : _steps.Count;
        _steps.Insert(insertAt, SequenceStep.Default);
        RefreshListView(selectIndex: insertAt);
    }

    /// <summary>Removes the currently selected step.</summary>
    private void DeleteStep()
    {
        int idx = SelectedIndex;
        if (idx < 0) return;

        _steps.RemoveAt(idx);

        // Keep selection on the same position, clamped to new list bounds.
        RefreshListView(selectIndex: _steps.Count > 0 ? Math.Min(idx, _steps.Count - 1) : -1);
    }

    /// <summary>
    /// Moves the selected step up (<paramref name="direction"/> = -1)
    /// or down (<paramref name="direction"/> = +1).
    /// </summary>
    private void MoveStep(int direction)
    {
        int idx    = SelectedIndex;
        int target = idx + direction;

        if (idx < 0 || target < 0 || target >= _steps.Count) return;

        (_steps[idx], _steps[target]) = (_steps[target], _steps[idx]);
        RefreshListView(selectIndex: target);
    }

    // ── ListView helpers ───────────────────────────────────────────────────────

    /// <summary>Rebuilds all ListView rows from <see cref="_steps"/>.</summary>
    private void RefreshListView(int selectIndex = -1)
    {
        _stepsListView.BeginUpdate();
        _stepsListView.Items.Clear();

        for (int i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            var item = new ListViewItem((i + 1).ToString());

            for (int led = 0; led < 4; led++)
                item.SubItems.Add(ColorToHex(step.LedAt(led)));

            item.SubItems.Add(step.WaitMs.ToString());
            item.Tag = step;
            _stepsListView.Items.Add(item);
        }

        _stepsListView.EndUpdate();

        if (selectIndex >= 0 && selectIndex < _stepsListView.Items.Count)
        {
            _stepsListView.Items[selectIndex].Selected = true;
            _stepsListView.Items[selectIndex].EnsureVisible();
        }

        UpdateButtonStates();
    }

    /// <summary>Enables/disables manipulation buttons based on current selection.</summary>
    private void UpdateButtonStates()
    {
        int idx   = SelectedIndex;
        bool any  = idx >= 0;

        _btnDeleteStep.Enabled = any;
        _btnMoveUp.Enabled     = idx > 0;
        _btnMoveDown.Enabled   = any && idx < _steps.Count - 1;
    }

    private int SelectedIndex =>
        _stepsListView.SelectedIndices.Count > 0
            ? _stepsListView.SelectedIndices[0]
            : -1;

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ── Inline colour editing (click a colour cell to type a hex value) ─────────

    /// <summary>Starts inline editing when the user clicks a colour column (1–4).</summary>
    private void OnListViewMouseClick(object? sender, MouseEventArgs e)
    {
        var hit = _stepsListView.HitTest(e.Location);
        if (hit.Item is null || hit.SubItem is null) return;

        int subIdx = hit.Item.SubItems.IndexOf(hit.SubItem);
        if (subIdx < 1 || subIdx > 4) return;   // only LED columns

        StartInlineEdit(hit.Item.Index, subIdx);
    }

    /// <summary>Positions the overlay TextBox over the target subitem and focuses it.</summary>
    private void StartInlineEdit(int itemIndex, int subItemIndex)
    {
        var subItemBounds = _stepsListView.Items[itemIndex].SubItems[subItemIndex].Bounds;

        // Convert ListView-client coords → GroupBox-client coords (parent of _inlineEditor).
        var groupLoc = _stepsListView.Parent!.PointToClient(
                           _stepsListView.PointToScreen(subItemBounds.Location));

        _editItemIdx    = itemIndex;
        _editSubItemIdx = subItemIndex;

        _inlineEditor.Bounds    = new Rectangle(groupLoc, new Size(subItemBounds.Width, subItemBounds.Height));
        _inlineEditor.BackColor = SystemColors.Window;
        _inlineEditor.Text      = _stepsListView.Items[itemIndex].SubItems[subItemIndex].Text;
        _inlineEditor.SelectAll();
        _inlineEditor.Visible = true;
        _inlineEditor.BringToFront();
        _inlineEditor.Focus();
    }

    /// <summary>
    /// Parses the typed hex value and, if valid, writes it back to <see cref="_steps"/>
    /// and refreshes the affected ListView cell and the right-hand editor panel.
    /// </summary>
    private void CommitInlineEdit()
    {
        if (!_inlineEditor.Visible) return;   // guard against re-entry
        _inlineEditor.Visible = false;

        if (_editItemIdx < 0 || _editSubItemIdx < 1 || _editSubItemIdx > 4) return;

        Color? parsed = TryParseHexColor(_inlineEditor.Text.Trim());
        if (parsed is null) return;   // invalid input → discard silently

        int ledIdx = _editSubItemIdx - 1;   // 0-based LED index
        var step   = _steps[_editItemIdx];

        // Build a new immutable step with the one channel replaced.
        Color[] leds = [step.Led1, step.Led2, step.Led3, step.Led4];
        leds[ledIdx] = parsed.Value;
        _steps[_editItemIdx] = new SequenceStep(leds[0], leds[1], leds[2], leds[3], step.WaitMs);

        // Patch the single cell in the ListView.
        var lvItem = _stepsListView.Items[_editItemIdx];
        lvItem.SubItems[_editSubItemIdx].Text = ColorToHex(parsed.Value);
        lvItem.Tag = _steps[_editItemIdx];

        // Keep the right-hand editor in sync when editing the selected row.
        if (_editItemIdx == SelectedIndex)
        {
            SetLedButton(ledIdx, parsed.Value);
            _ledPreviewPanel.Invalidate();
        }
    }

    /// <summary>Enter = commit, Escape = discard.</summary>
    private void OnInlineEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            CommitInlineEdit();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _inlineEditor.Visible = false;
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>Turns the TextBox background red while the typed value is not a valid hex colour.</summary>
    private void OnInlineEditorTextChanged(object? sender, EventArgs e)
    {
        _inlineEditor.BackColor = TryParseHexColor(_inlineEditor.Text.Trim()) is not null
            ? SystemColors.Window
            : Color.FromArgb(255, 220, 220);
    }

    /// <summary>Blocks characters that can never be part of a hex colour code.</summary>
    private static void OnInlineEditorKeyPress(object? sender, KeyPressEventArgs e)
    {
        char c = e.KeyChar;
        bool isHexDigit = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        if (!char.IsControl(c) && c != '#' && !isHexDigit)
            e.Handled = true;
    }

    /// <summary>
    /// Accepts <c>#RRGGBB</c> or <c>RRGGBB</c> (case-insensitive).
    /// Returns <see langword="null"/> for any other input.
    /// </summary>
    private static Color? TryParseHexColor(string text)
    {
        ReadOnlySpan<char> s = text.AsSpan().TrimStart('#');
        if (s.Length != 6) return null;

        if (!int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            return null;

        return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    // ── Step editor logic ──────────────────────────────────────────────────────

    /// <summary>
    /// Populates the right-hand editor panel from the step at <paramref name="idx"/>.
    /// Passing a negative index clears the editor back to defaults.
    /// </summary>
    private void LoadStepIntoEditor(int idx)
    {
        if (idx < 0 || idx >= _steps.Count)
        {
            for (int i = 0; i < 4; i++) SetLedButton(i, Color.Black);
            _waitSpinner.Value = 100;
            _ledPreviewPanel.Invalidate();
            return;
        }

        var step = _steps[idx];
        for (int i = 0; i < 4; i++) SetLedButton(i, step.LedAt(i));
        _waitSpinner.Value = Math.Clamp(step.WaitMs, 1, 60_000);
        _ledPreviewPanel.Invalidate();
    }

    /// <summary>Opens a colour dialog for one LED and updates the editor state.</summary>
    private void PickLedColor(int ledIndex)
    {
        using var dlg = new ColorDialog
        {
            Color    = _editorColors[ledIndex],
            FullOpen = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        SetLedButton(ledIndex, dlg.Color);
        _ledPreviewPanel.Invalidate();
    }

    /// <summary>Updates button appearance and stores the colour in <see cref="_editorColors"/>.</summary>
    private void SetLedButton(int idx, Color color)
    {
        _editorColors[idx]                           = color;
        _ledColorBtns[idx].BackColor                 = color == Color.Black
                                                           ? Color.FromArgb(45, 45, 45)
                                                           : color;
        _ledColorBtns[idx].ForeColor                 = color.GetBrightness() < 0.4f
                                                           ? Color.White
                                                           : Color.Black;
        _ledColorBtns[idx].Text                      = ColorToHex(color);
        _ledColorBtns[idx].FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
    }

    /// <summary>
    /// Commits the editor values to the selected step and refreshes that row
    /// in the ListView without rebuilding the entire list.
    /// </summary>
    private void UpdateStep()
    {
        int idx = SelectedIndex;
        if (idx < 0) return;

        _steps[idx] = new SequenceStep(
            _editorColors[0], _editorColors[1],
            _editorColors[2], _editorColors[3],
            (int)_waitSpinner.Value);

        var item = _stepsListView.Items[idx];
        for (int led = 0; led < 4; led++)
            item.SubItems[led + 1].Text = ColorToHex(_editorColors[led]);
        item.SubItems[5].Text = ((int)_waitSpinner.Value).ToString();
        item.Tag = _steps[idx];
    }

    // ── Simulation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts cycling through every step in order, holding each one for its
    /// own <see cref="SequenceStep.WaitMs"/> before advancing to the next.
    /// The top NeoTrinkey preview reflects each step live.
    /// </summary>
    private void StartSimulation()
    {
        if (_steps.Count == 0) return;

        _animTimer.Stop();
        _isSimulating = true;
        _animStepIdx  = 0;

        // Show the first step immediately, then let the timer drive the rest.
        ApplySimStep();

        _btnSimulate.Text = "⏹  Stop";
    }

    /// <summary>Stops the animation and restores the preview to the selected step.</summary>
    private void StopSimulation()
    {
        _animTimer.Stop();
        _isSimulating     = false;
        _btnSimulate.Text = "\u25b6  Simulate";

        // Restore the preview to whatever step is selected in the editor.
        LoadStepIntoEditor(SelectedIndex);
    }

    /// <summary>
    /// Displays the current animation step in the preview panel and
    /// schedules the timer to fire after that step’s hold time.
    /// </summary>
    private void ApplySimStep()
    {
        var step = _steps[_animStepIdx];
        for (int i = 0; i < 4; i++)
            _simulColors[i] = step.LedAt(i);

        _ledPreviewPanel.Invalidate();

        // Restart the timer with this step’s own hold time.
        _animTimer.Stop();
        _animTimer.Interval = Math.Max(1, step.WaitMs);
        _animTimer.Start();

        // Advance index so the NEXT tick shows the following step.
        _animStepIdx = (_animStepIdx + 1) % _steps.Count;
    }

    private void OnAnimTick(object? sender, EventArgs e) => ApplySimStep();

    /// <summary>Closes the form (simulation is stopped automatically via Dispose).</summary>
    private void OnCancelClicked() => Close();

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _animTimer.Stop(); _animTimer.Dispose(); }
        base.Dispose(disposing);
    }

    // ── JSON file I/O ─────────────────────────────────────────────────────────────

    /// <summary>Serialises the current step list to a user-chosen JSON file.</summary>
    private void SaveToJson()
    {
        if (_steps.Count == 0)
        {
            MessageBox.Show(this, "Add at least one step before saving.", Text);
            return;
        }

        Directory.CreateDirectory(SequenceFiles.Folder);

        using var dlg = new SaveFileDialog
        {
            Title            = "Save Sequence",
            InitialDirectory = SequenceFiles.Folder,
            Filter           = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt       = "json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var dto = new SequenceFiles.FileDto
        {
            Steps = _steps.Select(s => new SequenceFiles.StepDto
            {
                Leds   = [ColorToHex(s.Led1), ColorToHex(s.Led2), ColorToHex(s.Led3), ColorToHex(s.Led4)],
                WaitMs = s.WaitMs,
            }).ToList(),
        };

        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(dto, SequenceFiles.JsonOpts));
    }

    /// <summary>Replaces the current step list with steps read from a user-chosen JSON file.</summary>
    private void LoadFromJson()
    {
        using var dlg = new OpenFileDialog
        {
            Title            = "Load Sequence",
            InitialDirectory = SequenceFiles.Folder,
            Filter           = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var dto = JsonSerializer.Deserialize<SequenceFiles.FileDto>(
                          File.ReadAllText(dlg.FileName), SequenceFiles.JsonOpts);

            if (dto is null || dto.Steps.Count == 0)
            {
                MessageBox.Show(this, "No steps found in the selected file.", Text);
                return;
            }

            var loaded = new List<SequenceStep>();
            foreach (var step in dto.Steps)
            {
                if (step.Leds.Length < 4) continue;

                Color? c1 = TryParseHexColor(step.Leds[0]);
                Color? c2 = TryParseHexColor(step.Leds[1]);
                Color? c3 = TryParseHexColor(step.Leds[2]);
                Color? c4 = TryParseHexColor(step.Leds[3]);
                if (c1 is null || c2 is null || c3 is null || c4 is null) continue;

                loaded.Add(new SequenceStep(c1.Value, c2.Value, c3.Value, c4.Value,
                                            Math.Clamp(step.WaitMs, 1, 60_000)));
            }

            if (loaded.Count == 0)
            {
                MessageBox.Show(this, "No valid steps found in the selected file.", Text);
                return;
            }

            _steps.Clear();
            _steps.AddRange(loaded);
            RefreshListView(selectIndex: 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load file:\n{ex.Message}",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Clipboard (Excel-compatible copy / paste) ────────────────────────────

    private void OnListViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C) { CopySelectedSteps(); e.Handled = true; }
        if (e.Control && e.KeyCode == Keys.V) { PasteSteps();         e.Handled = true; }
    }

    /// <summary>
    /// Copies selected rows to the clipboard as tab-separated values
    /// (LED1 \t LED2 \t LED3 \t LED4 \t WaitMs) so they can be pasted
    /// directly into Excel – or pasted back into this editor.
    /// </summary>
    private void CopySelectedSteps()
    {
        if (_stepsListView.SelectedIndices.Count == 0) return;

        // Collect and sort indices so rows always appear top-to-bottom.
        var indices = new List<int>();
        foreach (int i in _stepsListView.SelectedIndices) indices.Add(i);
        indices.Sort();

        var sb = new StringBuilder();
        foreach (int idx in indices)
        {
            var s = _steps[idx];
            sb.Append(ColorToHex(s.Led1)).Append('\t')
              .Append(ColorToHex(s.Led2)).Append('\t')
              .Append(ColorToHex(s.Led3)).Append('\t')
              .Append(ColorToHex(s.Led4)).Append('\t')
              .Append(s.WaitMs)
              .Append("\r\n");
        }

        Clipboard.SetText(sb.ToString());
    }

    /// <summary>
    /// Parses tab-separated rows from the clipboard and inserts them as new
    /// steps after the current selection.  Accepts both the 5-column format
    /// produced by this editor (<c>LED1\tLED2\tLED3\tLED4\tWaitMs</c>) and
    /// the 6-column format Excel produces when it includes a leading row-number
    /// column (<c>#\tLED1\t…</c>).
    /// </summary>
    private void PasteSteps()
    {
        if (!Clipboard.ContainsText()) return;

        var steps = ParseStepsFromTsv(Clipboard.GetText());
        if (steps.Count == 0) return;

        int insertAt = SelectedIndex >= 0 ? SelectedIndex + 1 : _steps.Count;
        _steps.InsertRange(insertAt, steps);
        RefreshListView(selectIndex: insertAt + steps.Count - 1);
    }

    private static List<SequenceStep> ParseStepsFromTsv(string text)
    {
        var result = new List<SequenceStep>();

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split('\t');

            // Try offset 0 (5 cols: LED1 LED2 LED3 LED4 WaitMs).
            // Fall back to offset 1 (6 cols: # LED1 LED2 LED3 LED4 WaitMs).
            var step = TryParseStepAt(cols, offset: 0)
                    ?? TryParseStepAt(cols, offset: 1);

            if (step is not null) result.Add(step);
        }

        return result;
    }

    private static SequenceStep? TryParseStepAt(string[] cols, int offset)
    {
        if (cols.Length < offset + 5) return null;

        Color? c1 = TryParseHexColor(cols[offset    ].Trim());
        Color? c2 = TryParseHexColor(cols[offset + 1].Trim());
        Color? c3 = TryParseHexColor(cols[offset + 2].Trim());
        Color? c4 = TryParseHexColor(cols[offset + 3].Trim());

        if (c1 is null || c2 is null || c3 is null || c4 is null) return null;

        // Accept plain integers and Excel thousands-separated numbers (e.g. "1,000").
        if (!int.TryParse(cols[offset + 4].Trim(),
                          System.Globalization.NumberStyles.Integer |
                          System.Globalization.NumberStyles.AllowThousands,
                          null, out int waitMs)) return null;

        return new SequenceStep(c1.Value, c2.Value, c3.Value, c4.Value,
                                Math.Clamp(waitMs, 1, 60_000));
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private static Button MakeButton(string text, int width) => new()
    {
        Text     = text,
        Width    = width,
        Height   = 26,
        AutoSize = false,
    };

    private static Label MakeLabel(string text) => new()
    {
        Text     = text,
        AutoSize = true,
        Margin   = new Padding(0, 10, 4, 0),
    };
}
