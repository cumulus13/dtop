// File: Config.cs (updated)
// Adds Display section for per-section show/hide toggles and bar settings.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetHtop;

public class ColorMapping
{
    [JsonPropertyName("threshold")] public double Threshold { get; set; }
    [JsonPropertyName("color")]     public string Color     { get; set; } = "#FFFFFF";
}

public class RowHighlight
{
    [JsonPropertyName("min")]    public double Min    { get; set; } = 0;
    [JsonPropertyName("max")]    public double Max    { get; set; } = 100;
    [JsonPropertyName("fg")]     public string Fg     { get; set; } = "#FFFFFF";
    [JsonPropertyName("bg")]     public string Bg     { get; set; } = "none";
    [JsonPropertyName("metric")] public string Metric { get; set; } = "cpu";
}

public class GrowlConfig
{
    [JsonPropertyName("enabled")]         public bool   Enabled         { get; set; } = false;
    [JsonPropertyName("host")]            public string Host            { get; set; } = "localhost";
    [JsonPropertyName("port")]            public int    Port            { get; set; } = 23053;
    [JsonPropertyName("password")]        public string Password        { get; set; } = "";
    [JsonPropertyName("appName")]         public string AppName         { get; set; } = "DTOP";
    [JsonPropertyName("cooldownSeconds")] public int    CooldownSeconds { get; set; } = 60;
}

public class EmailConfig
{
    [JsonPropertyName("enabled")]         public bool   Enabled         { get; set; } = false;
    [JsonPropertyName("smtpHost")]        public string SmtpHost        { get; set; } = "smtp.gmail.com";
    [JsonPropertyName("smtpPort")]        public int    SmtpPort        { get; set; } = 587;
    [JsonPropertyName("useSsl")]          public bool   UseSsl          { get; set; } = true;
    [JsonPropertyName("username")]        public string Username        { get; set; } = "";
    [JsonPropertyName("password")]        public string Password        { get; set; } = "";
    [JsonPropertyName("from")]            public string From            { get; set; } = "";
    [JsonPropertyName("to")]              public string To              { get; set; } = "";
    [JsonPropertyName("cooldownSeconds")] public int    CooldownSeconds { get; set; } = 300;
    [JsonPropertyName("maxPerHour")]      public int    MaxPerHour      { get; set; } = 6;
}

public class Config
{
    [JsonPropertyName("refreshIntervalMs")]
    public int RefreshIntervalMs { get; set; } = 1000;

    [JsonPropertyName("maxProcessRows")]
    public int MaxProcessRows { get; set; } = 40;

    [JsonPropertyName("cpuHighThreshold")]
    public double CpuHighThreshold { get; set; } = 80.0;

    [JsonPropertyName("memoryHighThresholdPercent")]
    public double MemoryHighThresholdPercent { get; set; } = 80.0;

    [JsonPropertyName("notificationCooldownSeconds")]
    public int NotificationCooldownSeconds { get; set; } = 30;

    [JsonPropertyName("defaultForegroundColor")]
    public string DefaultForegroundColor { get; set; } = "#FFFFFF";

    // ── NEW: display section ──────────────────────────────────────────────────
    [JsonPropertyName("display")]
    public AppDisplayConfig Display { get; set; } = new();

    [JsonPropertyName("growl")]
    public GrowlConfig Growl { get; set; } = new();

    [JsonPropertyName("email")]
    public EmailConfig Email { get; set; } = new();

    [JsonPropertyName("cpuThresholds")]
    public List<ColorMapping> CpuThresholds { get; set; } =
    [
        new() { Threshold = 0,  Color = "#FFFFFF" },
        new() { Threshold = 20, Color = "#FFFF00" },
        new() { Threshold = 50, Color = "#FF8800" },
        new() { Threshold = 80, Color = "#FF0000" },
    ];

    [JsonPropertyName("memoryThresholds")]
    public List<ColorMapping> MemoryThresholds { get; set; } =
    [
        new() { Threshold = 0,  Color = "#FFFFFF" },
        new() { Threshold = 30, Color = "#00FFFF" },
        new() { Threshold = 60, Color = "#FF8800" },
        new() { Threshold = 85, Color = "#FF0000" },
    ];

    [JsonPropertyName("rowHighlights")]
    public List<RowHighlight> RowHighlights { get; set; } =
    [
        new() { Metric="cpu", Min=95, Max=100, Fg="#FFFFFF", Bg="#CC0000" },
        new() { Metric="cpu", Min=85, Max=94,  Fg="#000000", Bg="#DDAA00" },
        new() { Metric="cpu", Min=75, Max=84,  Fg="#FFFFFF", Bg="#884488" },
        new() { Metric="cpu", Min=65, Max=74,  Fg="#000000", Bg="#228822" },
        new() { Metric="cpu", Min=55, Max=64,  Fg="#000000", Bg="#119999" },
        new() { Metric="memory", Min=90, Max=100, Fg="#FFFFFF", Bg="#CC0000" },
        new() { Metric="memory", Min=75, Max=89,  Fg="#000000", Bg="#DDAA00" },
    ];

    [JsonIgnore]
    public string LoadedFrom { get; set; } = string.Empty;

    public static Config Load()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "dtop.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dtop.json"),
            "dtop.json",
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var json = File.ReadAllText(path);
                var cfg  = JsonSerializer.Deserialize<Config>(json, opts) ?? new Config();
                cfg.LoadedFrom = path;
                return cfg;
            }
            catch (Exception ex)
            {
                ColorConsole.WriteWarning($"  Could not parse config at {path}: {ex.Message}");
            }
        }
        return new Config { LoadedFrom = "(defaults)" };
    }

    public void Save(string path)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
    }
}
