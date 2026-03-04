using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Focus.Windows.Daemon.Overlay;

namespace Focus.Windows.Daemon;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class SettingsForm : Form
{
    private readonly FocusConfig _config;
    private readonly Dictionary<Direction, Color> _swatchColors = new();
    private byte _opacityAlpha;

    // Controls accessed in apply handler
    private ComboBox _strategyCombo = null!;
    private NumericUpDown _gridXNumeric = null!;
    private NumericUpDown _gridYNumeric = null!;
    private NumericUpDown _snapNumeric = null!;
    private NumericUpDown _opacityNumeric = null!;
    private NumericUpDown _delayNumeric = null!;
    private Button _applyBtn = null!;
    private readonly Dictionary<Direction, Panel> _swatchPanels = new();

    // Snapshot of initial values for dirty tracking
    private Strategy _initialStrategy;
    private int _initialGridX, _initialGridY, _initialSnap, _initialOpacity, _initialDelay;
    private readonly Dictionary<Direction, Color> _initialSwatchColors = new();

    public SettingsForm()
    {
        _config = FocusConfig.Load();
        InitializeColors();
        SnapshotInitialValues();
        BuildUi();
    }

    // -------------------------------------------------------------------------
    // Color helpers
    // -------------------------------------------------------------------------

    private static (byte alpha, Color rgb) ParseHexColor(string hex)
    {
        var raw = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber);
        byte a = (byte)(raw >> 24);
        byte r = (byte)(raw >> 16);
        byte g = (byte)(raw >> 8);
        byte b = (byte)raw;
        return (a, Color.FromArgb(r, g, b));
    }

    private static string ToHexColor(byte alpha, Color rgb)
        => $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    private void InitializeColors()
    {
        // Alpha comes from Left color; all four share the same alpha by design.
        // If they differ (e.g. manual config edits), Left wins — documented lossy behavior.
        var (leftAlpha, leftRgb) = ParseHexColor(_config.OverlayColors.Left);
        _opacityAlpha = leftAlpha;
        _swatchColors[Direction.Left] = leftRgb;

        var (_, rightRgb) = ParseHexColor(_config.OverlayColors.Right);
        _swatchColors[Direction.Right] = rightRgb;

        var (_, upRgb) = ParseHexColor(_config.OverlayColors.Up);
        _swatchColors[Direction.Up] = upRgb;

        var (_, downRgb) = ParseHexColor(_config.OverlayColors.Down);
        _swatchColors[Direction.Down] = downRgb;
    }

    private void SnapshotInitialValues()
    {
        _initialStrategy = _config.Strategy;
        _initialGridX = _config.GridFractionX;
        _initialGridY = _config.GridFractionY;
        _initialSnap = _config.SnapTolerancePercent;
        _initialOpacity = (int)Math.Round(_opacityAlpha / 255.0 * 100);
        _initialDelay = _config.OverlayDelayMs;
        foreach (var kvp in _swatchColors)
            _initialSwatchColors[kvp.Key] = kvp.Value;
    }

    private bool IsDirty()
    {
        if ((Strategy)_strategyCombo.SelectedItem! != _initialStrategy) return true;
        if ((int)_gridXNumeric.Value != _initialGridX) return true;
        if ((int)_gridYNumeric.Value != _initialGridY) return true;
        if ((int)_snapNumeric.Value != _initialSnap) return true;
        if ((int)_opacityNumeric.Value != _initialOpacity) return true;
        if ((int)_delayNumeric.Value != _initialDelay) return true;
        foreach (var kvp in _swatchColors)
            if (kvp.Value != _initialSwatchColors[kvp.Key]) return true;
        return false;
    }

    private void UpdateApplyEnabled() => _applyBtn.Enabled = IsDirty();

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        // Form-level properties
        Text                = "Focus Settings";
        FormBorderStyle     = FormBorderStyle.FixedDialog;
        MaximizeBox         = false;
        MinimizeBox         = false;
        AutoScaleMode       = AutoScaleMode.Dpi;
        StartPosition       = FormStartPosition.CenterScreen;
        Padding             = new Padding(12);
        ClientSize          = new Size(500, 680);

        // Root panel stacks sections vertically with auto-scroll as safety net
        var root = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoScroll    = true,
            Padding       = new Padding(0),
        };
        Controls.Add(root);

        // ---- About header ----
        root.Controls.Add(BuildAboutPanel());

        // ---- Navigation GroupBox ----
        root.Controls.Add(BuildNavigationGroup());

        // ---- Grid & Snapping GroupBox ----
        root.Controls.Add(BuildGridGroup());

        // ---- Overlays GroupBox ----
        root.Controls.Add(BuildOverlaysGroup());

        // ---- Keybindings GroupBox ----
        root.Controls.Add(BuildKeybindingsGroup());

        // ---- Apply button ----
        root.Controls.Add(BuildApplyRow());
    }

    // ---- Section helpers ----

    private Panel BuildAboutPanel()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = $"Focus v{version?.Major}.{version?.Minor}";

        var titleLabel = new Label
        {
            Text      = versionText,
            Font      = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize  = true,
        };

        var attributionLabel = new Label
        {
            Text      = "by Daniel M\u00e5nsson",
            Font      = new Font("Segoe UI", 9f),
            AutoSize  = true,
        };

        const string url = "https://github.com/daniel-mansson/focus";
        var linkLabel = new LinkLabel
        {
            Text     = url,
            Font     = new Font("Segoe UI", 9f),
            AutoSize = true,
        };
        linkLabel.Links.Add(0, url.Length, url);
        linkLabel.LinkClicked += (_, e) =>
            Process.Start(new ProcessStartInfo
            {
                FileName       = (string)e.Link!.LinkData!,
                UseShellExecute = true,
            });

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoSize      = true,
            Width         = 476,
            Padding       = new Padding(4, 8, 4, 8),
        };
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(attributionLabel);
        panel.Controls.Add(linkLabel);
        return panel;
    }

    private GroupBox BuildNavigationGroup()
    {
        var group = MakeGroup("Navigation", 476, 64);

        var label = new Label
        {
            Text     = "Strategy:",
            AutoSize = true,
            Location = new Point(12, 26),
        };

        _strategyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(100, 22),
            Width         = 200,
        };
        foreach (Strategy s in Enum.GetValues<Strategy>())
            _strategyCombo.Items.Add(s);
        _strategyCombo.SelectedItem = _config.Strategy;
        _strategyCombo.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();

        group.Controls.Add(label);
        group.Controls.Add(_strategyCombo);
        return group;
    }

    private GroupBox BuildGridGroup()
    {
        var group = MakeGroup("Grid & Snapping", 476, 120);

        int y = 26;
        (_, _gridXNumeric) = AddLabeledNumeric(group, "Grid Fraction X:", 1, 64, _config.GridFractionX, ref y);
        (_, _gridYNumeric) = AddLabeledNumeric(group, "Grid Fraction Y:", 1, 64, _config.GridFractionY, ref y);
        (_, _snapNumeric)  = AddLabeledNumeric(group, "Snap Tolerance %:", 0, 50, _config.SnapTolerancePercent, ref y);

        _gridXNumeric.ValueChanged += (_, _) => UpdateApplyEnabled();
        _gridYNumeric.ValueChanged += (_, _) => UpdateApplyEnabled();
        _snapNumeric.ValueChanged  += (_, _) => UpdateApplyEnabled();

        return group;
    }

    private GroupBox BuildOverlaysGroup()
    {
        var group = MakeGroup("Overlays", 476, 150);

        // Row 1: Color swatches
        var colorsLabel = new Label
        {
            Text     = "Colors:",
            AutoSize = true,
            Location = new Point(12, 28),
        };
        group.Controls.Add(colorsLabel);

        int swatchX = 100;
        foreach (var (dir, labelText) in new[]
        {
            (Direction.Left,  "Left"),
            (Direction.Right, "Right"),
            (Direction.Up,    "Up"),
            (Direction.Down,  "Down"),
        })
        {
            var dirLabel = new Label
            {
                Text      = labelText,
                AutoSize  = true,
                Location  = new Point(swatchX, 26),
                TextAlign = ContentAlignment.TopCenter,
            };
            group.Controls.Add(dirLabel);

            var swatch = new Panel
            {
                Location    = new Point(swatchX, 44),
                Size        = new Size(36, 28),
                BackColor   = _swatchColors[dir],
                BorderStyle = BorderStyle.FixedSingle,
                Cursor      = Cursors.Hand,
                Tag         = dir,
            };
            swatch.Click += OnSwatchClicked;
            _swatchPanels[dir] = swatch;
            group.Controls.Add(swatch);

            swatchX += 70;
        }

        // Row 2: Opacity
        int opacityY = 84;
        int opacityPercent = (int)Math.Round(_opacityAlpha / 255.0 * 100);
        (_, _opacityNumeric) = AddLabeledNumeric(group, "Opacity %:", 0, 100, opacityPercent, ref opacityY);
        _opacityNumeric.ValueChanged += (_, _) =>
        {
            _opacityAlpha = (byte)Math.Round((double)_opacityNumeric.Value / 100.0 * 255);
            UpdateApplyEnabled();
        };

        // Row 3: Delay
        int delayY = 112;
        (_, _delayNumeric) = AddLabeledNumeric(group, "Delay (ms):", 0, 5000, _config.OverlayDelayMs, ref delayY);
        _delayNumeric.ValueChanged += (_, _) => UpdateApplyEnabled();

        return group;
    }

    private void OnSwatchClicked(object? sender, EventArgs e)
    {
        if (sender is not Panel panel || panel.Tag is not Direction dir)
            return;

        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color    = _swatchColors[dir],
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _swatchColors[dir] = dialog.Color;
            panel.BackColor    = dialog.Color;
            UpdateApplyEnabled();
        }
    }

    private GroupBox BuildKeybindingsGroup()
    {
        var group = MakeGroup("Keybindings", 476, 120);

        const string content =
            "CapsLock + Left/Right/Up/Down   Navigate to window in direction\r\n" +
            "CapsLock + W/A/S/D              Navigate (WASD aliases)\r\n" +
            "CapsLock + LAlt + Arrow         Move window in direction\r\n" +
            "CapsLock + LWin + Arrow         Grow/shrink window in direction\r\n" +
            "CapsLock + 1\u20139                  Jump to numbered window overlay";

        var label = new Label
        {
            Text      = content,
            Font      = new Font("Consolas", 9f),
            AutoSize  = true,
            Location  = new Point(12, 22),
        };
        group.Controls.Add(label);
        return group;
    }

    private Panel BuildApplyRow()
    {
        _applyBtn = new Button
        {
            Text     = "Apply",
            Width    = 80,
            Height   = 28,
            Anchor   = AnchorStyles.Right,
            Enabled  = false,
        };
        _applyBtn.Click += OnApplyClicked;

        var panel = new Panel
        {
            Width  = 476,
            Height = 36,
        };
        _applyBtn.Location = new Point(panel.Width - _applyBtn.Width - 4, 4);
        panel.Controls.Add(_applyBtn);
        return panel;
    }

    // ---- Apply handler ----

    private void OnApplyClicked(object? sender, EventArgs e)
    {
        // Read control values back into config
        _config.Strategy             = (Strategy)_strategyCombo.SelectedItem!;
        _config.GridFractionX        = (int)_gridXNumeric.Value;
        _config.GridFractionY        = (int)_gridYNumeric.Value;
        _config.SnapTolerancePercent = (int)_snapNumeric.Value;
        _config.OverlayDelayMs       = (int)_delayNumeric.Value;

        // Recompose ARGB hex strings from swatch colors + shared alpha
        _config.OverlayColors.Left  = ToHexColor(_opacityAlpha, _swatchColors[Direction.Left]);
        _config.OverlayColors.Right = ToHexColor(_opacityAlpha, _swatchColors[Direction.Right]);
        _config.OverlayColors.Up    = ToHexColor(_opacityAlpha, _swatchColors[Direction.Up]);
        _config.OverlayColors.Down  = ToHexColor(_opacityAlpha, _swatchColors[Direction.Down]);

        // Atomic save: write .tmp then File.Replace (Pitfall 3: handle fresh install)
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters    = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
        };
        var json       = JsonSerializer.Serialize(_config, options);
        var configPath = FocusConfig.GetConfigPath();
        var tmpPath    = configPath + ".tmp";

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(tmpPath, json);

        if (File.Exists(configPath))
            File.Replace(tmpPath, configPath, null);
        else
            File.Move(tmpPath, configPath);

        // Update snapshot so button grays out until next change
        SnapshotInitialValues();
        _applyBtn.Enabled = false;
    }

    // ---- Layout helpers ----

    private static GroupBox MakeGroup(string title, int width, int height)
    {
        return new GroupBox
        {
            Text   = title,
            Width  = width,
            Height = height,
        };
    }

    /// <summary>
    /// Adds a Label + NumericUpDown pair to <paramref name="parent"/> at the given y offset.
    /// Returns the created controls and advances <paramref name="y"/> by one row height (28px).
    /// </summary>
    private static (Label label, NumericUpDown numeric) AddLabeledNumeric(
        Control parent, string labelText, decimal min, decimal max, decimal value, ref int y)
    {
        var label = new Label
        {
            Text     = labelText,
            AutoSize = true,
            Location = new Point(12, y + 4),
        };

        var numeric = new NumericUpDown
        {
            Minimum  = min,
            Maximum  = max,
            Value    = value,
            Location = new Point(160, y),
            Width    = 80,
        };

        parent.Controls.Add(label);
        parent.Controls.Add(numeric);
        y += 28;
        return (label, numeric);
    }
}
