// File: Renderer.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

using System.Text;

namespace DotnetHtop;

public static class Renderer
{
    // ── ANSI ──────────────────────────────────────────────────────────────────
    private static string  CursorTo(int row) => $"\x1b[{row + 1};1H";
    private const  string  EraseEol   = "\x1b[K";
    private const  string  Reset      = "\x1b[0m";
    private const  string  Bold       = "\x1b[1m";
    private const  string  HideCursor = "\x1b[?25l";

    // 24-bit foreground
    private static string Fg(int r, int g, int b) => $"\x1b[38;2;{r};{g};{b}m";
    // 24-bit background
    private static string Bg(int r, int g, int b) => $"\x1b[48;2;{r};{g};{b}m";

    // Parse "#RRGGBB" → (r,g,b). Returns null for "none"/empty.
    private static (int r, int g, int b)? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) ||
            hex.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return null;
        try
        {
            return (
                Convert.ToInt32(hex[..2], 16),
                Convert.ToInt32(hex[2..4], 16),
                Convert.ToInt32(hex[4..6], 16)
            );
        }
        catch { return null; }
    }

    private static string FgHex(string hex)
    {
        var c = ParseHex(hex);
        return c.HasValue ? Fg(c.Value.r, c.Value.g, c.Value.b) : CWhite;
    }

    private static string BgHex(string hex)
    {
        var c = ParseHex(hex);
        return c.HasValue ? Bg(c.Value.r, c.Value.g, c.Value.b) : string.Empty;
    }

    // Default palette
    private static readonly string CWhite  = Fg(210, 210, 210);
    private static readonly string CDim    = Fg( 90,  90,  90);
    private static readonly string CCyan   = Fg( 70, 190, 190);
    private static readonly string CGreen  = Fg( 70, 190,  90);
    private static readonly string CYellow = Fg(210, 170,  40);
    private static readonly string COrange = Fg(200, 100,  25);
    private static readonly string CRed    = Fg(200,  55,  55);
    private static readonly string CBlue   = Fg( 70, 130, 200);

    // ── Layout ────────────────────────────────────────────────────────────────
    private const int ColPid     = 7;
    private const int ColName    = 28;
    private const int ColCpu     = 8;
    private const int ColMem     = 9;
    private const int ColThreads = 7;
    private const int ColStatus  = 9;
    private const int BarWidth   = 26;
    private const int HeaderRows = 11;

    // ── Diff buffers ──────────────────────────────────────────────────────────
    private static string[] _prevPlain = Array.Empty<string>();
    private static readonly List<(string plain, string ansi)> _lines = new(128);
    private static readonly StringBuilder _sb  = new(512);
    private static readonly StringBuilder _out = new(64 * 1024);

    // ── Public ────────────────────────────────────────────────────────────────

    public static void RenderFrame(
        AppState state, double avgCpu, double memPct,
        long usedMemMb, IEnumerable<ProcessInfo> sorted, int total)
    {
        _lines.Clear();
        int winW    = Math.Max(80, Console.WindowWidth);
        int winH    = Math.Max(10, Console.WindowHeight);
        int maxRows = Math.Max(3, winH - HeaderRows - 1);

        BuildHeader(state, winW);
        BuildSystemBars(avgCpu, memPct, usedMemMb, state.TotalMemoryMb);
        BuildColumnHeader(state.SortColumn, state.SortDescending);
        BuildProcessRows(sorted, state.Config, maxRows, state.TotalMemoryMb);
        BuildFooter(total, maxRows, state.Config.LoadedFrom, state.Paused,
                    state.StartTime, state.LastReloadTime, state.LastReloadSource);

        if (_prevPlain.Length < _lines.Count)
            _prevPlain = new string[_lines.Count + 8];

        _out.Clear();
        _out.Append(HideCursor);

        for (int i = 0; i < _lines.Count; i++)
        {
            var (plain, ansi) = _lines[i];
            if (plain == _prevPlain[i]) continue;
            _out.Append(CursorTo(i));
            _out.Append(ansi);
            _out.Append(EraseEol);
            _prevPlain[i] = plain;
        }

        if (_out.Length > HideCursor.Length)
            Console.Write(_out.ToString());
    }

    public static void Invalidate() => Array.Clear(_prevPlain, 0, _prevPlain.Length);

    // ── Line helpers ──────────────────────────────────────────────────────────

    private static void L(string plain, string ansi) => _lines.Add((plain, ansi));

    private static void L(string plain, Action<StringBuilder> build)
    {
        _sb.Clear();
        build(_sb);
        _lines.Add((plain, _sb.ToString()));
    }

    // ── Header ────────────────────────────────────────────────────────────────

    private static void BuildHeader(AppState state, int w)
    {
        var sep = new string('\u2500', Math.Min(w - 1, 100));

        L(sep, CDim + sep + Reset);

        var os = SystemInfo.OsSummary();
        L($"DTOP {os}", sb =>
        {
            sb.Append("  " + Bold + CWhite + "DTOP" + Reset);
            sb.Append(CDim + " \u2014 .NET Process Monitor  ");
            sb.Append(CCyan + os + Reset);
        });

        L(sep, CDim + sep + Reset);

        var statsPlain = $"Cores:{state.Cores} RAM:{state.TotalMemoryMb:N0}MB";
        L(statsPlain, sb =>
        {
            sb.Append("  " + CDim + "Cores ");
            sb.Append(CWhite + $"{state.Cores}");
            sb.Append(CDim + "  |  RAM ");
            sb.Append(CWhite + $"{state.TotalMemoryMb:N0} MB");
            sb.Append(Reset);
        });

        var notifPlain = $"notif:{state.NotificationsEnabled} growl:{state.GrowlEnabled} email:{state.EmailEnabled}";
        L(notifPlain, sb =>
        {
            sb.Append("  " + CDim + "Notif: ");
            Indicator(sb, state.NotificationsEnabled, "ON", "OFF");
            sb.Append(CDim + "  Growl: ");
            Indicator(sb, state.GrowlEnabled, "ON", "OFF");
            sb.Append(CDim + "  Email: ");
            Indicator(sb, state.EmailEnabled, "ON", "OFF");
            if (state.Paused)
                sb.Append("  " + CYellow + "\u23f8 PAUSED" + Reset);
            sb.Append(Reset);
        });

        L(sep, CDim + sep + Reset);

        const string k1 = "  Sort: C-cpu  M-mem  I-pid  E-name  H-threads  S-status  |  A-asc  D-desc";
        L(k1, CDim + k1 + Reset);

        const string k2 = "  Toggle: N-notif  G-growl  L-email  |  P-pause  T-test  R-reload  Q-quit";
        L(k2, CDim + k2 + Reset);

        L(sep, CDim + sep + Reset);
    }

    private static void Indicator(StringBuilder sb, bool on, string y, string n)
    {
        sb.Append(on ? CGreen + y + Reset : CRed + n + Reset);
    }

    // ── System bars ───────────────────────────────────────────────────────────

    private static void BuildSystemBars(double cpu, double mem, long usedMb, ulong totalMb)
    {
        var cpuCol   = PctColor(cpu);
        var cpuPlain = $"CPU {BarPlain(cpu, BarWidth)} {cpu,5:F1}%";
        L(cpuPlain, sb =>
        {
            sb.Append("  " + CDim + "CPU ");
            Bar(sb, cpu, BarWidth, cpuCol);
            sb.Append(" " + cpuCol + $"{cpu,5:F1}%" + Reset);
        });

        var memCol   = PctColor(mem);
        var memPlain = $"MEM {BarPlain(mem, BarWidth)} {mem,5:F1}% ({usedMb}/{totalMb})";
        L(memPlain, sb =>
        {
            sb.Append("  " + CDim + "MEM ");
            Bar(sb, mem, BarWidth, memCol);
            sb.Append(" " + memCol + $"{mem,5:F1}%" + Reset);
            sb.Append(CDim + $"  ({usedMb:N0} / {totalMb:N0} MB)" + Reset);
        });
    }

    // ── Column header ─────────────────────────────────────────────────────────

    private static void BuildColumnHeader(SortColumn col, bool desc)
    {
        string Arrow(SortColumn c) => col == c ? (desc ? " \u25bc" : " \u25b2") : "  ";

        var plain = $"COL pid{Arrow(SortColumn.Pid)} name{Arrow(SortColumn.Name)} cpu{Arrow(SortColumn.Cpu)} mem{Arrow(SortColumn.Memory)} thr{Arrow(SortColumn.Threads)} stat{Arrow(SortColumn.Status)}";

        L(plain, sb =>
        {
            sb.Append("  ");
            ColHead(sb, "PID",          ColPid,     col == SortColumn.Pid,     desc);
            sb.Append(' ');
            ColHead(sb, "Process Name", -ColName,   col == SortColumn.Name,    desc);
            sb.Append(' ');
            ColHead(sb, "CPU %",        ColCpu,     col == SortColumn.Cpu,     desc);
            sb.Append("  ");
            ColHead(sb, "Mem MB",       ColMem,     col == SortColumn.Memory,  desc);
            sb.Append("  ");
            ColHead(sb, "Threads",      ColThreads, col == SortColumn.Threads, desc);
            sb.Append("  ");
            ColHead(sb, "Status",       -ColStatus, col == SortColumn.Status,  desc);
            sb.Append(Reset);
        });

        var rule = "  " + new string('\u2500', 92);
        L(rule, CDim + rule + Reset);
    }

    private static void ColHead(StringBuilder sb, string label, int width, bool active, bool desc)
    {
        var arrow   = active ? (desc ? "\u25bc" : "\u25b2") : "";
        var content = label + arrow;
        var text    = width < 0 ? content.PadRight(-width) : content.PadLeft(width);
        sb.Append(active ? CWhite + Bold + text + Reset : CDim + text + Reset);
    }

    // ── Process rows ──────────────────────────────────────────────────────────

    private static void BuildProcessRows(
        IEnumerable<ProcessInfo> procs, Config cfg, int maxRows, ulong totalMb)
    {
        int written = 0;
        foreach (var p in procs)
        {
            if (written >= maxRows) break;

            var memPct = totalMb > 0 ? (p.MemoryMb / (double)totalMb) * 100.0 : 0;

            // ── Row highlight: check cpu rules first, then memory rules ────────
            var highlight = MatchRowHighlight(p.CpuPercent, memPct, cfg.RowHighlights);

            // ── Per-column text colors (used when no row bg is active) ─────────
            var cpuCol = highlight is null
                ? AnsiForUsage(p.CpuPercent, cfg.CpuThresholds)
                : FgHex(highlight.Fg);
            var memCol = highlight is null
                ? AnsiForUsage(memPct, cfg.MemoryThresholds)
                : FgHex(highlight.Fg);
            var stCol = highlight is null
                ? (p.Status == "running" ? CGreen : CDim)
                : FgHex(highlight.Fg);
            var dimCol  = highlight is null ? CDim   : FgHex(highlight.Fg);
            var nameCol = highlight is null ? CWhite : FgHex(highlight.Fg);

            // Background for the entire row (empty string = no background)
            var rowBg = highlight is not null ? BgHex(highlight.Bg) : string.Empty;

            var pid  = $"{p.Id,ColPid}";
            var name = $"{p.Name.Truncate(ColName),-ColName}";
            var cpu  = $"{p.CpuPercent,ColCpu:F2}%";
            var mem  = $"{p.MemoryMb,ColMem:N0}";
            var thr  = $"{p.Threads,ColThreads}";
            var st   = $"{p.Status,-ColStatus}";

            // Plain key: include highlight fg+bg so a rule change redraws the row
            var hlKey = highlight is null ? "" : $"|hl:{highlight.Fg}/{highlight.Bg}";
            var plain = $"{pid}|{name}|{cpu}|{mem}|{thr}|{st}{hlKey}";

            L(plain, sb =>
            {
                // Apply row background first if set
                sb.Append(rowBg);

                sb.Append("  ");
                sb.Append(dimCol  + pid  + " ");
                sb.Append(nameCol + name + " ");
                sb.Append(cpuCol  + cpu  + "  ");
                sb.Append(memCol  + mem  + "  ");
                sb.Append(dimCol  + thr  + "  ");
                sb.Append(stCol   + st   + Reset);
            });
            written++;
        }

        for (int i = written; i < maxRows; i++)
            L($"__blank_{i}", "");
    }

    /// <summary>
    /// Find the first matching RowHighlight rule.
    /// CPU rules are checked before memory rules so they take priority.
    /// Within each metric group, rules are checked in config order (first match wins).
    /// </summary>
    private static RowHighlight? MatchRowHighlight(
        double cpuPct, double memPct, List<RowHighlight> rules)
    {
        // CPU rules first
        foreach (var r in rules)
        {
            if (!r.Metric.Equals("cpu", StringComparison.OrdinalIgnoreCase)) continue;
            if (cpuPct >= r.Min && cpuPct <= r.Max) return r;
        }
        // Memory rules second
        foreach (var r in rules)
        {
            if (!r.Metric.Equals("memory", StringComparison.OrdinalIgnoreCase)) continue;
            if (memPct >= r.Min && memPct <= r.Max) return r;
        }
        return null;
    }

    // ── Footer ────────────────────────────────────────────────────────────────

    private static void BuildFooter(int total, int maxRows, string cfgPath,
        bool paused, DateTime start, DateTime lastReload, string reloadSource)
    {
        var elapsed = DateTime.Now - start;
        var uptime  = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        var cfg     = cfgPath.Length > 28 ? "\u2026" + cfgPath[^27..] : cfgPath;
        var shown   = Math.Min(total, maxRows);

        var reloadTag = string.Empty;
        if (lastReload != DateTime.MinValue && (DateTime.Now - lastReload).TotalSeconds < 8)
            reloadTag = $"  \u27f3 reloaded ({reloadSource})";

        var line = $"  {shown}/{total} procs  |  uptime {uptime}  |  cfg: {cfg}{reloadTag}";
        L(line + reloadTag, CDim + line + CGreen + reloadTag + Reset);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static void Bar(StringBuilder sb, double pct, int w, string color)
    {
        var f = (int)Math.Round(Math.Clamp(pct / 100.0, 0, 1) * w);
        sb.Append(CDim + "[" + color);
        sb.Append(new string('\u2588', f));
        sb.Append(CDim + new string('\u2591', w - f) + "]" + Reset);
    }

    private static string BarPlain(double pct, int w)
    {
        var f = (int)Math.Round(Math.Clamp(pct / 100.0, 0, 1) * w);
        return "[" + new string('#', f) + new string('.', w - f) + "]";
    }

    private static string PctColor(double p) => p switch
    {
        >= 80 => CRed, >= 50 => COrange, >= 20 => CYellow, _ => CGreen,
    };

    // Map ColorMapping hex → ANSI for bar/column value text
    private static string AnsiForUsage(double pct, List<ColorMapping> thresholds)
    {
        var m = thresholds
            .OrderByDescending(t => t.Threshold)
            .FirstOrDefault(t => pct >= t.Threshold);
        if (m is null) return CWhite;
        return m.Color.ToUpperInvariant() switch
        {
            "#FF0000"              => CRed,
            "#FF8800" or "#FFA500" => COrange,
            "#FFFF00"              => CYellow,
            "#00FF00" or "#008000" => CGreen,
            "#00FFFF" or "#008080" => CCyan,
            "#0000FF" or "#000080" => CBlue,
            _                      => FgHex(m.Color),   // full 24-bit fallback
        };
    }
}
