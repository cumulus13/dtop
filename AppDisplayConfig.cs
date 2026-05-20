// File: AppDisplayConfig.cs
// Display section for dtop.json — controls which sections are visible by default.
// All fields can also be toggled at runtime with keyboard shortcuts (except ShowTitle).

using System.Text.Json.Serialization;

namespace DotnetHtop;

public class AppDisplayConfig
{
    /// <summary>"=== DTOP ===" title line. Config-only — no keyboard shortcut.</summary>
    [JsonPropertyName("showTitle")]
    public bool ShowTitle { get; set; } = true;

    /// <summary>Cores / notif / sort-key hint rows. Toggle: [1]</summary>
    [JsonPropertyName("showHeader")]
    public bool ShowHeader { get; set; } = true;

    /// <summary>CPU + MEM block-bar rows. Toggle: [2]</summary>
    [JsonPropertyName("showSysBars")]
    public bool ShowSysBars { get; set; } = true;

    /// <summary>Sparkline history under the system bars. Toggle: [3]</summary>
    [JsonPropertyName("showSparkline")]
    public bool ShowSparkline { get; set; } = true;

    /// <summary>Column header row above the process list. Toggle: [4]</summary>
    [JsonPropertyName("showColumnHeader")]
    public bool ShowColumnHeader { get; set; } = true;

    /// <summary>Keyboard hint bar at the bottom. Toggle: [K] or [5]</summary>
    [JsonPropertyName("showHint")]
    public bool ShowHint { get; set; } = true;

    /// <summary>Number of sparkline history samples to keep (width in chars).</summary>
    [JsonPropertyName("sparklineLength")]
    public int SparklineLength { get; set; } = 60;

    /// <summary>Width of the system bar in characters.</summary>
    [JsonPropertyName("barWidth")]
    public int BarWidth { get; set; } = 36;

    /// <summary>Samples to average for bar display (1 = raw, 2-4 = anti-flicker).</summary>
    [JsonPropertyName("smoothWindow")]
    public int SmoothWindow { get; set; } = 3;
}
