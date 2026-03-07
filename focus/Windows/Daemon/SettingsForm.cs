using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Focus.Windows.Daemon.Overlay;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;

namespace Focus.Windows.Daemon;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class SettingsForm : Form
{
    private readonly FocusConfig _config;
    private readonly Dictionary<Direction, Color> _swatchColors = new();
    private byte _opacityAlpha;

    // Controls accessed in save handler
    private ComboBox _strategyCombo = null!;
    private NumericUpDown _gridXNumeric = null!;
    private NumericUpDown _gridYNumeric = null!;
    private NumericUpDown _snapNumeric = null!;
    private NumericUpDown _opacityNumeric = null!;
    private readonly Dictionary<Direction, Panel> _swatchPanels = new();

    // Grid preview overlay
    private OverlayWindow? _gridOverlay;
    private CheckBox _gridPreviewCheck = null!;

    // Startup controls
    private CheckBox _startupCheck = null!;
    private CheckBox _elevationCheck = null!;

    public SettingsForm()
    {
        _config = FocusConfig.Load();
        InitializeColors();
        BuildUi();
        FormClosing += (_, _) => HideGridPreview();
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
        ClientSize          = new Size(500, 780);

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

        // ---- Startup GroupBox ----
        root.Controls.Add(BuildStartupGroup());
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
            Width         = 456,
            Padding       = new Padding(4, 8, 4, 8),
        };
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(attributionLabel);
        panel.Controls.Add(linkLabel);
        return panel;
    }

    private GroupBox BuildNavigationGroup()
    {
        var group = MakeGroup("Navigation", 456, 64);

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
        _strategyCombo.SelectedIndexChanged += (_, _) => SaveConfig();

        group.Controls.Add(label);
        group.Controls.Add(_strategyCombo);
        return group;
    }

    private GroupBox BuildGridGroup()
    {
        var group = MakeGroup("Grid & Snapping", 456, 148);

        int y = 26;
        (_, _gridXNumeric) = AddLabeledNumeric(group, "Grid Fraction X:", 1, 64, _config.GridFractionX, ref y);
        (_, _gridYNumeric) = AddLabeledNumeric(group, "Grid Fraction Y:", 1, 64, _config.GridFractionY, ref y);
        (_, _snapNumeric)  = AddLabeledNumeric(group, "Snap Tolerance %:", 0, 50, _config.SnapTolerancePercent, ref y);

        _gridPreviewCheck = new CheckBox
        {
            Text     = "Show Grid Preview",
            AutoSize = true,
            Location = new Point(12, y + 4),
        };
        _gridPreviewCheck.CheckedChanged += (_, _) =>
        {
            if (_gridPreviewCheck.Checked)
                ShowGridPreview();
            else
                HideGridPreview();
        };
        group.Controls.Add(_gridPreviewCheck);

        _gridXNumeric.ValueChanged += (_, _) => { SaveConfig(); RefreshGridPreview(); };
        _gridYNumeric.ValueChanged += (_, _) => { SaveConfig(); RefreshGridPreview(); };
        _snapNumeric.ValueChanged  += (_, _) => SaveConfig();

        return group;
    }

    private GroupBox BuildOverlaysGroup()
    {
        var group = MakeGroup("Overlays", 456, 124);

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
                Location    = new Point(swatchX, 54),
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
        int opacityY = 90;
        int opacityPercent = (int)Math.Round(_opacityAlpha / 255.0 * 100);
        (_, _opacityNumeric) = AddLabeledNumeric(group, "Opacity %:", 0, 100, opacityPercent, ref opacityY);
        _opacityNumeric.ValueChanged += (_, _) =>
        {
            _opacityAlpha = (byte)Math.Round((double)_opacityNumeric.Value / 100.0 * 255);
            SaveConfig();
        };


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
            SaveConfig();
        }
    }

    private GroupBox BuildKeybindingsGroup()
    {
        var group = MakeGroup("Keybindings", 456, 125);

        const string content =
            "CAPS + Arrow      Navigate\r\n" +
            "CAPS + WASD       Navigate\r\n" +
            "CAPS + LAlt       Move\r\n" +
            "CAPS + LWin       Resize\r\n" +
            "CAPS + 1\u20139        Quick Navigate";

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

    // ---- Startup controls ----

    private GroupBox BuildStartupGroup()
    {
        var group = MakeGroup("Startup", 456, 100);

        _startupCheck = new CheckBox
        {
            Text     = "Run at startup",
            AutoSize = true,
            Location = new Point(12, 24),
        };

        _elevationCheck = new CheckBox
        {
            Text     = "Request elevated permissions",
            AutoSize = true,
            Location = new Point(12, 48),
        };

        var note = new Label
        {
            Text      = "Required to navigate between admin windows",
            AutoSize  = true,
            Location  = new Point(30, 70),
            Font      = new Font("Segoe UI", 8f),
            ForeColor = SystemColors.GrayText,
        };

        // Detect current task state BEFORE wiring handlers to avoid triggering schtasks
        var (exists, isElevated) = DetectTaskState();
        _startupCheck.Checked    = exists;
        _elevationCheck.Checked  = isElevated;
        _elevationCheck.Enabled  = exists;

        // Wire handlers AFTER setting initial state
        _startupCheck.CheckedChanged   += OnStartupToggled;
        _elevationCheck.CheckedChanged += OnElevationToggled;

        group.Controls.Add(_startupCheck);
        group.Controls.Add(_elevationCheck);
        group.Controls.Add(note);
        return group;
    }

    private static (bool exists, bool isElevated) DetectTaskState()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                Arguments              = "/Query /TN \"FocusDaemon\" /XML",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                return (false, false);

            bool isElevated = output.Contains("HighestAvailable", StringComparison.Ordinal);
            return (true, isElevated);
        }
        catch
        {
            return (false, false);
        }
    }

    private static bool RunSchtasksElevated(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName        = "schtasks.exe",
                Arguments       = arguments,
                UseShellExecute = true,
                Verb            = "runas",
                WindowStyle     = ProcessWindowStyle.Hidden,
            };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
            return false;
        }
    }

    private static bool RunElevatedCmd(string command)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = $"/c {command}",
                UseShellExecute = true,
                Verb            = "runas",
                WindowStyle     = ProcessWindowStyle.Hidden,
            };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
    }

    private static string BuildTaskXml(string appPath, bool runElevated)
    {
        string runLevel = runElevated ? "HighestAvailable" : "LeastPrivilege";
        return
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <RegistrationInfo>\r\n" +
            "    <Description>Focus daemon - window navigation</Description>\r\n" +
            "  </RegistrationInfo>\r\n" +
            "  <Triggers>\r\n" +
            "    <LogonTrigger>\r\n" +
            "      <Enabled>true</Enabled>\r\n" +
            "    </LogonTrigger>\r\n" +
            "  </Triggers>\r\n" +
            "  <Principals>\r\n" +
            "    <Principal id=\"Author\">\r\n" +
            "      <LogonType>InteractiveToken</LogonType>\r\n" +
            "      <RunLevel>" + runLevel + "</RunLevel>\r\n" +
            "    </Principal>\r\n" +
            "  </Principals>\r\n" +
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
            "    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
            "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
            "    <Enabled>true</Enabled>\r\n" +
            "    <Hidden>false</Hidden>\r\n" +
            "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "    <Priority>7</Priority>\r\n" +
            "  </Settings>\r\n" +
            "  <Actions Context=\"Author\">\r\n" +
            "    <Exec>\r\n" +
            "      <Command>" + appPath + "</Command>\r\n" +
            "      <Arguments>daemon --background</Arguments>\r\n" +
            "    </Exec>\r\n" +
            "  </Actions>\r\n" +
            "</Task>";
    }

    private bool CreateTask(bool elevated)
    {
        string exePath = Environment.ProcessPath!;
        string xml = BuildTaskXml(exePath, elevated);
        string xmlPath = Path.Combine(Path.GetTempPath(), "FocusDaemon.xml");

        File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

        try
        {
            // Delete + create in a single elevated process to avoid double UAC prompt
            return RunElevatedCmd(
                $"schtasks.exe /Delete /TN \"FocusDaemon\" /F 2>nul & schtasks.exe /Create /XML \"{xmlPath}\" /TN \"FocusDaemon\" /F");
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    private static bool DeleteTask()
    {
        // Try non-elevated first
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName        = "schtasks.exe",
                Arguments       = "/Delete /TN \"FocusDaemon\" /F",
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            process.Start();
            process.WaitForExit();
            if (process.ExitCode == 0) return true;
        }
        catch { }

        // Fallback to elevated
        return RunSchtasksElevated("/Delete /TN \"FocusDaemon\" /F");
    }

    private async void OnStartupToggled(object? sender, EventArgs e)
    {
        bool wantStartup = _startupCheck.Checked;

        // Disable both checkboxes during operation
        _startupCheck.Enabled   = false;
        _elevationCheck.Enabled = false;

        bool success;
        try
        {
            success = await Task.Run(() =>
            {
                if (wantStartup)
                    return CreateTask(elevated: _elevationCheck.Checked);
                else
                    return DeleteTask();
            });
        }
        catch (Exception ex)
        {
            success = false;
            MessageBox.Show(
                this,
                $"Failed to update startup task:\n{ex.Message}",
                "Focus",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (!success)
        {
            // Silently revert toggle (unhook to prevent recursion)
            _startupCheck.CheckedChanged -= OnStartupToggled;
            _startupCheck.Checked = !wantStartup;
            _startupCheck.CheckedChanged += OnStartupToggled;
        }

        // Update dependent controls
        _elevationCheck.Enabled = _startupCheck.Checked;
        if (!_startupCheck.Checked)
        {
            _elevationCheck.CheckedChanged -= OnElevationToggled;
            _elevationCheck.Checked = false;
            _elevationCheck.CheckedChanged += OnElevationToggled;
        }

        _startupCheck.Enabled = true;
    }

    private async void OnElevationToggled(object? sender, EventArgs e)
    {
        bool wantElevated = _elevationCheck.Checked;

        // Disable both checkboxes during operation
        _startupCheck.Enabled   = false;
        _elevationCheck.Enabled = false;

        bool success;
        try
        {
            success = await Task.Run(() => CreateTask(elevated: wantElevated));
        }
        catch (Exception ex)
        {
            success = false;
            MessageBox.Show(
                this,
                $"Failed to update startup task:\n{ex.Message}",
                "Focus",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (!success)
        {
            // Silently revert toggle (unhook to prevent recursion)
            _elevationCheck.CheckedChanged -= OnElevationToggled;
            _elevationCheck.Checked = !wantElevated;
            _elevationCheck.CheckedChanged += OnElevationToggled;
        }

        // Re-enable both checkboxes
        _startupCheck.Enabled   = true;
        _elevationCheck.Enabled = true;
    }

    // ---- Grid preview ----

    private unsafe RECT GetWorkArea()
    {
        var handle = new HWND(Handle);
        var hMon = PInvoke.MonitorFromWindow(handle, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO mi = default;
        mi.cbSize = (uint)sizeof(MONITORINFO);
        if (PInvoke.GetMonitorInfo(hMon, ref mi))
            return mi.rcWork;
        return default;
    }

    private void ShowGridPreview()
    {
        var workArea = GetWorkArea();
        int w = workArea.right - workArea.left;
        int h = workArea.bottom - workArea.top;
        if (w <= 0 || h <= 0) return;

        _gridOverlay ??= new OverlayWindow();

        var (stepX, stepY) = GridCalculator.GetGridStep(w, h, (int)_gridXNumeric.Value, (int)_gridYNumeric.Value);

        _gridOverlay.Reposition(workArea);
        GridRenderer.PaintGrid(_gridOverlay.Hwnd, workArea, stepX, stepY);
        _gridOverlay.Show();
    }

    private void HideGridPreview()
    {
        if (_gridOverlay != null)
        {
            _gridOverlay.Dispose();
            _gridOverlay = null;
        }
    }

    private void RefreshGridPreview()
    {
        if (_gridPreviewCheck.Checked)
            ShowGridPreview();
    }

    // ---- Auto-save on every change ----

    private void SaveConfig()
    {
        _config.Strategy             = (Strategy)_strategyCombo.SelectedItem!;
        _config.GridFractionX        = (int)_gridXNumeric.Value;
        _config.GridFractionY        = (int)_gridYNumeric.Value;
        _config.SnapTolerancePercent = (int)_snapNumeric.Value;

        _config.OverlayColors.Left  = ToHexColor(_opacityAlpha, _swatchColors[Direction.Left]);
        _config.OverlayColors.Right = ToHexColor(_opacityAlpha, _swatchColors[Direction.Right]);
        _config.OverlayColors.Up    = ToHexColor(_opacityAlpha, _swatchColors[Direction.Up]);
        _config.OverlayColors.Down  = ToHexColor(_opacityAlpha, _swatchColors[Direction.Down]);

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
