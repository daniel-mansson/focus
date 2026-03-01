using System.Text.Json;
using System.Text.Json.Serialization;
using Focus.Windows.Daemon.Overlay;

namespace Focus.Windows;

internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection, EdgeMatching, EdgeProximity, AxisOnly }
internal enum WrapBehavior { NoOp, Wrap, Beep }

internal class FocusConfig
{
    public Strategy Strategy { get; set; } = Strategy.Balanced;
    public WrapBehavior Wrap { get; set; } = WrapBehavior.NoOp;
    public string[] Exclude { get; set; } = [];
    public OverlayColors OverlayColors { get; set; } = new();
    public string OverlayRenderer { get; set; } = "border";
    public int OverlayDelayMs { get; set; } = 0;  // CFG-06: default 0 per user decision (REQUIREMENTS.md says ~150ms but user overrode to 0)

    public static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "focus", "config.json");
    }

    public static FocusConfig Load()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
            return new FocusConfig();

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
            };
            return JsonSerializer.Deserialize<FocusConfig>(json, options) ?? new FocusConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[focus] Warning: config parse error ({ex.Message}); using defaults.");
            return new FocusConfig();
        }
    }

    public static void WriteDefaults(string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
        };
        var json = JsonSerializer.Serialize(new FocusConfig(), options);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
