---
phase: quick-3
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/Daemon/DaemonCommand.cs
autonomous: true
requirements: [QUICK-3]

must_haves:
  truths:
    - "Running `focus daemon --verbose` prints the resolved config to stderr before entering the event loop"
    - "Config output includes: config path, strategy, wrap, exclude list, overlay renderer, overlay delay, overlay colors"
    - "Config output uses the same timestamp+bracket format as other verbose daemon logs"
    - "Non-verbose daemon mode prints no config (behavior unchanged)"
  artifacts:
    - path: "focus/Windows/Daemon/DaemonCommand.cs"
      provides: "Verbose config printing on daemon startup"
      contains: "verbose"
  key_links:
    - from: "focus/Windows/Daemon/DaemonCommand.cs"
      to: "focus/Windows/FocusConfig.cs"
      via: "FocusConfig.Load() and FocusConfig.GetConfigPath()"
      pattern: "config\\.(Strategy|Wrap|Exclude|OverlayRenderer|OverlayDelayMs|OverlayColors)"
---

<objective>
Print the resolved FocusConfig to stderr when the daemon starts in --verbose mode.

Purpose: When debugging daemon behavior, the user needs to know which config values are in effect without having to separately run `focus --debug config`. This is especially useful when the config file may or may not exist, or when defaults are being used.

Output: Modified DaemonCommand.cs with verbose config printing at startup.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@focus/Windows/Daemon/DaemonCommand.cs
@focus/Windows/FocusConfig.cs
@focus/Windows/Daemon/Overlay/OverlayColors.cs
@focus/Windows/Daemon/CapsLockMonitor.cs

<interfaces>
<!-- Key types and contracts the executor needs. -->

From focus/Windows/FocusConfig.cs:
```csharp
internal class FocusConfig
{
    public Strategy Strategy { get; set; } = Strategy.Balanced;
    public WrapBehavior Wrap { get; set; } = WrapBehavior.NoOp;
    public string[] Exclude { get; set; } = [];
    public OverlayColors OverlayColors { get; set; } = new();
    public string OverlayRenderer { get; set; } = "border";
    public int OverlayDelayMs { get; set; } = 0;

    public static string GetConfigPath();
    public static FocusConfig Load();
}
```

From focus/Windows/Daemon/Overlay/OverlayColors.cs:
```csharp
internal class OverlayColors
{
    public string Left  { get; set; } = "#BF4488CC";
    public string Right { get; set; } = "#BFCC4444";
    public string Up    { get; set; } = "#BF44AA66";
    public string Down  { get; set; } = "#BFCCAA33";
}
```

Existing verbose log format (from CapsLockMonitor.cs):
```csharp
Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CAPSLOCK held");
Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Direction: {modifierPrefix}{keyName} -> {directionName}");
```

Existing --debug config output format (from Program.cs, lines 162-168):
```csharp
Console.WriteLine($"Config file: {configPath}");
Console.WriteLine($"  exists: {File.Exists(configPath)}");
Console.WriteLine($"  strategy: {config.Strategy}");
Console.WriteLine($"  wrap: {config.Wrap}");
Console.WriteLine($"  exclude: [{string.Join(", ", config.Exclude.Select(p => $"\"{p}\""))}]");
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add verbose config dump on daemon startup</name>
  <files>focus/Windows/Daemon/DaemonCommand.cs</files>
  <action>
In `DaemonCommand.Run()`, immediately after the config is loaded (line 46: `var config = FocusConfig.Load();`) and gated by `if (verbose)`, add a block that prints the resolved configuration to stderr.

Use the same timestamp format as other daemon verbose logs: `[HH:mm:ss.fff]`.

Print these lines to `Console.Error.WriteLine`:
```
[HH:mm:ss.fff] Config:
[HH:mm:ss.fff]   file: {configPath}
[HH:mm:ss.fff]   exists: {true|false}
[HH:mm:ss.fff]   strategy: {config.Strategy}
[HH:mm:ss.fff]   wrap: {config.Wrap}
[HH:mm:ss.fff]   exclude: [{comma-separated quoted names}]
[HH:mm:ss.fff]   overlayRenderer: {config.OverlayRenderer}
[HH:mm:ss.fff]   overlayDelayMs: {config.OverlayDelayMs}
[HH:mm:ss.fff]   overlayColors: left={config.OverlayColors.Left} right={config.OverlayColors.Right} up={config.OverlayColors.Up} down={config.OverlayColors.Down}
```

Use a single timestamp captured once (e.g., `var ts = DateTime.Now.ToString("HH:mm:ss.fff");`) for all lines in the block so they share the same timestamp.

Get `configPath` via `FocusConfig.GetConfigPath()` and check existence via `File.Exists(configPath)`.

Format the exclude list the same way as `--debug config`: `[{string.Join(", ", config.Exclude.Select(p => $"\"{p}\""))}]`. This requires adding `using System.Linq;` at the top of the file if not already present (check first — it may already be imported via global usings).

This block MUST be placed AFTER `var config = FocusConfig.Load();` (current line 46) and BEFORE the channel creation (current line 49). It must only execute when `verbose` is true.

Do NOT move or change any other code in the file. Do NOT change the existing startup message on line 35 ("Focus daemon started. Listening for CAPSLOCK.").
  </action>
  <verify>
    Run `dotnet build focus/focus.csproj` and confirm it compiles with zero errors and zero warnings.
  </verify>
  <done>
    When `focus daemon --verbose` is run, stderr shows the full resolved config with timestamps immediately after "Focus daemon started." and before the hook/channel setup begins. Non-verbose mode is unaffected.
  </done>
</task>

</tasks>

<verification>
1. `dotnet build focus/focus.csproj` compiles cleanly (0 errors, 0 warnings)
2. Manual: run `focus daemon --verbose` and observe config printed to stderr with timestamped lines
3. Manual: run `focus daemon` (without --verbose) and confirm NO config is printed
</verification>

<success_criteria>
- DaemonCommand.cs has a verbose-gated config dump block after FocusConfig.Load()
- All config properties are printed: path, exists, strategy, wrap, exclude, overlayRenderer, overlayDelayMs, overlayColors
- Format matches existing daemon verbose log style ([HH:mm:ss.fff] prefix)
- Build succeeds with no errors or warnings
</success_criteria>

<output>
After completion, create `.planning/quick/3-when-starting-the-daemon-in-verbose-mode/3-SUMMARY.md`
</output>
