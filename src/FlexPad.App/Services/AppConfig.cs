using System.Text.Json;
using System.Text.Json.Serialization;
using FlexPad.Core;

namespace FlexPad.App.Services;

/// <summary>
/// Persisted settings. The JSON names are the ones the original Python flexpad used, so an existing
/// <c>config.json</c> — the radio address, every button, the knob bindings — is picked up as-is.
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("flex_host")] public string FlexHost { get; set; } = "";
    [JsonPropertyName("flex_port")] public int FlexPort { get; set; } = RadioClient.DefaultPort;
    [JsonPropertyName("columns")] public int Columns { get; set; } = 4;
    [JsonPropertyName("stop_on_error")] public bool StopOnError { get; set; } = true;
    [JsonPropertyName("auto_check_updates")] public bool CheckUpdatesAtStartup { get; set; }
    [JsonPropertyName("flexcontrol")] public KnobConfig Knob { get; set; } = new();
    [JsonPropertyName("buttons")] public List<ButtonConfig> Buttons { get; set; } = new();

    // Window state. Positions only, not sizes, except the main window whose size is the grid.
    [JsonPropertyName("window")] public WindowConfig Window { get; set; } = new();

    /// <summary>Fill anything a hand-edited or older file left out.</summary>
    public void Normalize()
    {
        if (Columns < 1) Columns = 1;
        if (FlexPort <= 0) FlexPort = RadioClient.DefaultPort;
        Knob ??= new KnobConfig();
        Knob.Normalize();
        Buttons ??= new List<ButtonConfig>();
        foreach (var b in Buttons)
        {
            b.Label ??= "?";
            b.Commands ??= new List<string>();
        }
        Window ??= new WindowConfig();
    }

    /// <summary>The buttons a fresh install starts with: the transverter case this app exists for.</summary>
    public static List<ButtonConfig> DefaultButtons() => new()
    {
        new("2m USB", "F1", "#2d6a4f",
            "# 2 m transverter on XVTA. The XVTR band must already be defined in SmartSDR.",
            "slice tune {slice} 144.200", "slice set {slice} mode=USB",
            "slice set {slice} rxant=XVTA txant=XVTA", "filt {slice} 150 2900"),
        new("70cm USB", "F2", "#2d6a4f",
            "slice tune {slice} 432.100", "slice set {slice} mode=USB",
            "slice set {slice} rxant=XVTB txant=XVTB", "filt {slice} 150 2900"),
        new("20m USB", "F3", null,
            "slice tune {slice} 14.250", "slice set {slice} mode=USB",
            "slice set {slice} rxant=ANT1 txant=ANT1", "filt {slice} 100 2900"),
        new("40m LSB", "F4", null,
            "slice tune {slice} 7.200", "slice set {slice} mode=LSB",
            "slice set {slice} rxant=ANT1 txant=ANT1", "filt {slice} -2900 -100"),
        new("ANT1", null, "#264653", "# Antenna only, on whichever slice is active.", "slice set {slice} rxant=ANT1 txant=ANT1"),
        new("ANT2", null, "#264653", "slice set {slice} rxant=ANT2 txant=ANT2"),
        new("XVTA", null, "#264653", "slice set {slice} rxant=XVTA txant=XVTA"),
        new("XVTB", null, "#264653", "slice set {slice} rxant=XVTB txant=XVTB"),
        new("TX -> A", null, null, "slice set {A} tx=1"),
        new("TX -> B", null, null, "slice set {B} tx=1"),
        new("2nd RX 70cm", null, null,
            "# Open a second slice on the 70 cm transverter, leaving the active one alone.",
            "# ant= only sets the RX port; the TX port comes from the band's last use, so pin both.",
            "slice create freq=432.100 ant=XVTB mode=USB", "wait 0.5",
            "slice set {B} rxant=XVTB txant=XVTB", "filt {B} 150 2900"),
        new("Close B", null, null, "slice remove {B}"),
    };
}

public sealed class ButtonConfig
{
    public ButtonConfig() { }

    public ButtonConfig(string label, string? key, string? color, params string[] commands)
    {
        Label = label;
        Key = key;
        Color = color;
        Commands = commands.ToList();
    }

    [JsonPropertyName("label")] public string Label { get; set; } = "?";
    [JsonPropertyName("key")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Key { get; set; }
    [JsonPropertyName("color")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Color { get; set; }
    [JsonPropertyName("commands")] public List<string> Commands { get; set; } = new();

    public ButtonConfig Clone() => new() { Label = Label, Key = Key, Color = Color, Commands = Commands.ToList() };
}

public sealed class KnobConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("port")] public string Port { get; set; } = "";
    [JsonPropertyName("invert")] public bool Invert { get; set; }
    [JsonPropertyName("steps")] public List<int> Steps { get; set; } = new() { 10, 100, 1000, 10000 };
    [JsonPropertyName("bindings")] public Dictionary<string, string> Bindings { get; set; } = new();

    public void Normalize()
    {
        Port ??= "";
        if (Steps is null || Steps.Count == 0) Steps = new List<int> { 10, 100, 1000, 10000 };
        Bindings ??= new Dictionary<string, string>();
        var defaults = new Dictionary<string, string>
        {
            ["S"] = KnobPolicy.ActionStep, ["L"] = KnobPolicy.ActionNextSlice, ["C"] = KnobPolicy.ActionMute,
        };
        foreach (var code in KnobProtocol.Events)
            if (!Bindings.ContainsKey(code)) Bindings[code] = defaults.TryGetValue(code, out var d) ? d : "";
    }
}

public sealed class WindowConfig
{
    [JsonPropertyName("x")] public double? X { get; set; }
    [JsonPropertyName("y")] public double? Y { get; set; }
    [JsonPropertyName("width")] public double? Width { get; set; }
    [JsonPropertyName("height")] public double? Height { get; set; }
    [JsonPropertyName("console_x")] public double? ConsoleX { get; set; }
    [JsonPropertyName("console_y")] public double? ConsoleY { get; set; }
    [JsonPropertyName("console_open")] public bool ConsoleOpen { get; set; }
    [JsonPropertyName("setup_x")] public double? SetupX { get; set; }
    [JsonPropertyName("setup_y")] public double? SetupY { get; set; }
    [JsonPropertyName("setup_tab")] public int SetupTab { get; set; }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// <c>%AppData%\flexpad</c> on Windows, <c>~/.config/flexpad</c> elsewhere — lower-case on purpose,
    /// because that is where the Python flexpad kept its settings and this build inherits them.
    /// Public so uninstall can name what's in here; it removes files it names, never the directory.
    /// </summary>
    public static string DataDir
    {
        get
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "flexpad");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigFilePath => System.IO.Path.Combine(DataDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFilePath)) ?? Fresh();
                cfg.Normalize();
                return cfg;
            }
        }
        catch
        {
            // Unreadable: keep it as config.json.bak rather than run with defaults the next Save
            // would overwrite it with — that file holds every button the user wrote.
            AtomicFile.Backup(ConfigFilePath);
        }
        return Fresh();
    }

    private static AppConfig Fresh()
    {
        var cfg = new AppConfig { Buttons = AppConfig.DefaultButtons() };
        cfg.Normalize();
        return cfg;
    }

    public static void Save(AppConfig config)
    {
        try { AtomicFile.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(config, Options)); }
        catch { /* best effort */ }
    }
}
