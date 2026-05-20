// File: Renderer.cs
// Replaces the old ██░░-style bar with a sub-character precision block bar
// (▏▎▍▌▋▊▉█) and adds a rolling sparkline history row (▁▂▃▄▅▆▇█).
//
// All display sections are gated by DisplayState overrides so keyboard
// shortcuts take effect on the very next frame.

using System.Text;

namespace DotnetHtop;

public static class Renderer
{
    // ════════════════════════════════════════════════════════════════════════════
    //  Unicode
    // ════════════════════════════════════════════════════════════════════════════

    // Sub-character bar fill: index 0 = space, 1-7 = eighths, 8 = full block
    // At barWidth=36 this gives 288 distinct positions (vs 36 with full blocks).
    private static readonly string[] BlockFill =
        { " ", "▏", "▎", "▍", "▌", "▋", "▊", "▉", "█" };

    // Sparkline height steps
    private static readonly string[] SparkChar =
        { " ", "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

    // ════════════════════════════════════════════════════════════════════════════
    //  ANSI helpers
    // ════════════════════════════════════════════════════════════════════════════

    private static string  CursorTo(int row) => $"\x1b[{row + 1};1H";
    private const  string  EraseEol   = "\x1b[K";
    private const  string  Reset      = "\x1b[0m";
    private const  string  Bold       = "\x1b[1m";
    private const  string  HideCursor = "\x1b[?25l";

    private static string Fg(int r, int g, int b) => $"\x1b[38;2;{r};{g};{b}m";
    private static string Bg(int r, int g, int b) => $"\x1b[48;2;{r};{g};{b}m";

    private static (int r, int g, int b)? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) ||
            hex.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return null;
        try { return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16)); }
        catch { return null; }
    }

    private static string FgHex(string hex)
        => ParseHex(hex) is { } c ? Fg(c.r, c.g, c.b) : CWhite;

    private static string BgHex(string hex)
        => ParseHex(hex) is { } c ? Bg(c.r, c.g, c.b) : string.Empty;

    // Default palette
    private static readonly string CWhite  = Fg(210, 210, 210);
    private static readonly string CDim    = Fg( 90,  90,  90);
    private static readonly string CCyan   = Fg( 70, 190, 190);
    private static readonly string CGreen  = Fg( 70, 190,  90);
    private static readonly string CYellow = Fg(210, 170,  40);
    private static readonly string COrange = Fg(200, 100,  25);
    private static readonly string CRed    = Fg(200,  55,  55);
    private static readonly string CBlue   = Fg( 70, 130, 200);

    // ════════════════════════════════════════════════════════════════════════════
    //  Rolling sparkline buffers
    // ════════════════════════════════════════════════════════════════════════════

    // Kept as module-level state so they survive between frames.
    // Updated every frame with the latest averaged value.
    private static readonly Queue<double> _cpuHistory = new();
    private static readonly Queue<double> _memHistory = new();

    // Short rolling window for anti-flicker smoothing
    private static readonly Queue<double> _cpuSmooth = new();
    private static readonly Queue<double> _memSmooth = new();

    // ════════════════════════════════════════════════════════════════════════════
    //  Layout
    // ════════════════════════════════════════════════════════════════════════════

    private const int ColPid     = 7;
    private const int ColName    = 28;
    private const int ColCpu     = 8;
    private const int ColMem     = 9;
    private const int ColThreads = 7;
    private const int ColStatus  = 9;

    // ════════════════════════════════════════════════════════════════════════════
    //  Diff buffers
    // ════════════════════════════════════════════════════════════════════════════

    private static string[] _prevPlain = Array.Empty<string>();
    private static readonly List<(string plain, string ansi)> _lines = new(128);
    private static readonly StringBuilder _sb  = new(512);
    private static readonly StringBuilder _out = new(64 * 1024);

    // ════════════════════════════════════════════════════════════════════════════
    //  Public entry point
    // ════════════════════════════════════════════════════════════════════════════

    public static void RenderFrame(
        AppState state, double avgCpu, double memPct,
        long usedMemMb, IEnumerable<ProcessInfo> sorted, int total)
    {
        var  disp    = state.Config.Display;
        int  winW    = Math.Max(80, Console.WindowWidth);
        int  winH    = Math.Max(10, Console.WindowHeight);

        // ── Update smoothing buffers ──────────────────────────────────────────
        int sw = disp.SmoothWindow;
        double smoothCpu = PushSmooth(_cpuSmooth, avgCpu, sw);
        double smoothMem = PushSmooth(_memSmooth, memPct, sw);

        // History always gets the smoothed value (cleaner sparkline)
        PushHistory(_cpuHistory, smoothCpu, disp.SparklineLength);
        PushHistory(_memHistory, smoothMem, disp.SparklineLength);

        // ── Resolve effective display flags ───────────────────────────────────
        bool showTitle  = disp.ShowTitle;          // config-only, no override
        bool showHdr    = DisplayState.Eff(DisplayState.OvHeader,    disp.ShowHeader);
        bool showBars   = DisplayState.Eff(DisplayState.OvSysBar,    disp.ShowSysBars);
        bool showSpark  = DisplayState.Eff(DisplayState.OvSparkline, disp.ShowSparkline);
        bool showColHdr = DisplayState.Eff(DisplayState.OvColHeader, disp.ShowColumnHeader);
        bool showHint   = DisplayState.Eff(DisplayState.OvHint,      disp.ShowHint);

        // Header rows changes when sections toggle → recalculate dynamically
        int headerRows = (showHdr    ? 7 : 0)   // sep + title + sep + stats + notif + sep + keys
                       + (showBars   ? 2 : 0)   // cpu bar + mem bar
                       + (showSpark  ? 2 : 0)   // cpu sparkline + mem sparkline
                       + 1                       // separator before process list
                       + (showColHdr ? 2 : 0)   // column header + rule
                       + (showHint   ? 1 : 0);  // hint bar (counted below)

        int maxRows = Math.Max(3, winH - headerRows - 2);

        _lines.Clear();

        // ── Title (config-only) ───────────────────────────────────────────────
        if (showTitle)
            BuildTitle(state, winW);

        // ── Header (cores / notif / sort keys) ────────────────────────────────
        if (showHdr)
            BuildHeader(state, winW, disp);

        // ── System bars ───────────────────────────────────────────────────────
        if (showBars)
            BuildSystemBars(smoothCpu, smoothMem, usedMemMb, state.TotalMemoryMb, disp);

        // ── Sparkline history ─────────────────────────────────────────────────
        if (showSpark)
            BuildSparklines(smoothCpu, smoothMem, disp);

        // ── Separator ─────────────────────────────────────────────────────────
        var sep = CDim + new string('─', Math.Min(winW - 1, 100)) + Reset;
        L("|sep|", sep);

        // ── Column header ─────────────────────────────────────────────────────
        if (showColHdr)
            BuildColumnHeader(state.SortColumn, state.SortDescending);

        // ── Process rows ──────────────────────────────────────────────────────
        BuildProcessRows(sorted, state.Config, maxRows, state.TotalMemoryMb);

        // ── Footer ────────────────────────────────────────────────────────────
        BuildFooter(total, maxRows, state.Config.LoadedFrom, state.Paused,
                    state.StartTime, state.LastReloadTime, state.LastReloadSource);

        // ── Hint bar ──────────────────────────────────────────────────────────
        if (showHint)
            BuildHintBar(state.Config, disp);

        // ── Diff-render ───────────────────────────────────────────────────────
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

    // ════════════════════════════════════════════════════════════════════════════
    //  Smoothing helpers
    // ════════════════════════════════════════════════════════════════════════════

    private static double PushSmooth(Queue<double> q, double val, int window)
    {
        q.Enqueue(val);
        while (q.Count > Math.Max(1, window)) q.Dequeue();
        double sum = 0; foreach (var v in q) sum += v;
        return sum / q.Count;
    }

    private static void PushHistory(Queue<double> q, double val, int max)
    {
        q.Enqueue(val);
        while (q.Count > max) q.Dequeue();
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Section builders
    // ════════════════════════════════════════════════════════════════════════════

    private static void BuildTitle(AppState state, int w)
    {
        var sep = new string('─', Math.Min(w - 1, 100));
        L("|sep0|", CDim + sep + Reset);

        var os = SystemInfo.OsSummary();
        L($"DTOP {os}", sb =>
        {
            sb.Append("  " + Bold + CWhite + "DTOP" + Reset);
            sb.Append(CDim + " — .NET Process Monitor  ");
            sb.Append(CCyan + os + Reset);
        });
    }

    private static void BuildHeader(AppState state, int w, AppDisplayConfig disp)
    {
        var sep = new string('─', Math.Min(w - 1, 100));
        L("|sep1|", CDim + sep + Reset);

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
            Indicator(sb, state.NotificationsEnabled);
            sb.Append(CDim + "  Growl: ");
            Indicator(sb, state.GrowlEnabled);
            sb.Append(CDim + "  Email: ");
            Indicator(sb, state.EmailEnabled);
            if (state.Paused)
                sb.Append("  " + CYellow + "⏸ PAUSED" + Reset);
            sb.Append(Reset);
        });

        L("|sep2|", CDim + sep + Reset);

        const string k1 = "  Sort: C-cpu  M-mem  I-pid  E-name  H-threads  S-status  |  A-asc  D-desc";
        L(k1, CDim + k1 + Reset);

        const string k2 = "  Toggle: N-notif  G-growl  L-email  |  P-pause  T-test  R-reload  Q-quit";
        L(k2, CDim + k2 + Reset);

        // Display-section shortcut hint
        const string k3 = "  View:   1-header  2-bars  3-sparkline  4-cols  K-hint  M-minimal";
        L(k3, CDim + k3 + Reset);
    }

    private static void BuildSystemBars(
        double cpu, double mem, long usedMb, ulong totalMb, AppDisplayConfig disp)
    {
        int bw = disp.BarWidth;

        // CPU
        var cpuAnsi = PctAnsi(cpu);
        var cpuPlain = $"CPU {cpu,5:F1}%";
        L(cpuPlain + BarPlainKey(cpu, bw), sb =>
        {
            sb.Append("  " + CDim + "CPU  ");
            AppendBar(sb, cpu, bw, cpuAnsi);
            sb.Append("  " + cpuAnsi + $"{cpu,5:F1}%" + Reset);
            sb.Append("  " + PctLabel(cpu));
        });

        // MEM
        var memAnsi = PctAnsi(mem);
        var memExtra = totalMb > 0
            ? $"{usedMb / 1024.0:F1}/{totalMb / 1024.0:F0} GB"
            : "";
        var memPlain = $"MEM {mem,5:F1}% {memExtra}";
        L(memPlain + BarPlainKey(mem, bw), sb =>
        {
            sb.Append("  " + CDim + "MEM  ");
            AppendBar(sb, mem, bw, memAnsi);
            sb.Append("  " + memAnsi + $"{mem,5:F1}%" + Reset);
            if (memExtra.Length > 0)
                sb.Append("  " + CDim + memExtra + Reset);
        });
    }

    private static void BuildSparklines(double cpu, double mem, AppDisplayConfig disp)
    {
        // CPU sparkline
        var cpuAnsi = PctAnsi(cpu);
        L("cpuspark:" + _cpuHistory.Count, sb =>
        {
            sb.Append("  " + CDim + "     ");
            AppendSparkline(sb, _cpuHistory, disp.SparklineLength, cpuAnsi);
        });

        // MEM sparkline
        var memAnsi = PctAnsi(mem);
        L("memspark:" + _memHistory.Count, sb =>
        {
            sb.Append("  " + CDim + "     ");
            AppendSparkline(sb, _memHistory, disp.SparklineLength, memAnsi);
        });
    }

    private static void BuildColumnHeader(SortColumn col, bool desc)
    {
        string Arrow(SortColumn c) => col == c ? (desc ? " ▼" : " ▲") : "  ";

        var plain = $"COL pid{Arrow(SortColumn.Pid)} name cpu mem thr stat";
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

        var rule = "  " + new string('─', 92);
        L(rule, CDim + rule + Reset);
    }

    private static void BuildProcessRows(
        IEnumerable<ProcessInfo> procs, Config cfg, int maxRows, ulong totalMb)
    {
        int written = 0;
        foreach (var p in procs)
        {
            if (written >= maxRows) break;

            double memPct = totalMb > 0 ? (p.MemoryMb / (double)totalMb) * 100.0 : 0;

            var highlight = MatchRowHighlight(p.CpuPercent, memPct, cfg.RowHighlights);

            var cpuCol  = highlight is null ? AnsiForUsage(p.CpuPercent, cfg.CpuThresholds) : FgHex(highlight.Fg);
            var memCol  = highlight is null ? AnsiForUsage(memPct, cfg.MemoryThresholds)    : FgHex(highlight.Fg);
            var stCol   = highlight is null ? (p.Status == "running" ? CGreen : CDim)       : FgHex(highlight.Fg);
            var dimCol  = highlight is null ? CDim   : FgHex(highlight.Fg);
            var nameCol = highlight is null ? CWhite : FgHex(highlight.Fg);
            var rowBg   = highlight is not null ? BgHex(highlight.Bg) : string.Empty;

            var pid  = $"{p.Id,ColPid}";
            var name = $"{p.Name.Truncate(ColName),-ColName}";
            var cpu  = $"{p.CpuPercent,ColCpu:F2}%";
            var mem  = $"{p.MemoryMb,ColMem:N0}";
            var thr  = $"{p.Threads,ColThreads}";
            var st   = $"{p.Status,-ColStatus}";
            var hlKey = highlight is null ? "" : $"|hl:{highlight.Fg}/{highlight.Bg}";
            var plain = $"{pid}|{name}|{cpu}|{mem}|{thr}|{st}{hlKey}";

            L(plain, sb =>
            {
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

    private static void BuildFooter(int total, int maxRows, string cfgPath,
        bool paused, DateTime start, DateTime lastReload, string reloadSource)
    {
        var elapsed = DateTime.Now - start;
        var uptime  = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        var cfg     = cfgPath.Length > 28 ? "…" + cfgPath[^27..] : cfgPath;
        var shown   = Math.Min(total, maxRows);

        var reloadTag = string.Empty;
        if (lastReload != DateTime.MinValue && (DateTime.Now - lastReload).TotalSeconds < 8)
            reloadTag = $"  ⟳ reloaded ({reloadSource})";

        var line = $"  {shown}/{total} procs  |  uptime {uptime}  |  cfg: {cfg}";
        L(line + reloadTag, CDim + line + CGreen + reloadTag + Reset);
    }

    private static void BuildHintBar(Config cfg, AppDisplayConfig disp)
    {
        static string K(string k, string lbl, bool on)
        {
            string kc = on ? "38;2;210;210;210" : "38;2;70;70;70";
            string lc = on ? "38;2;120;165;120" : "38;2;60;60;60";
            return $"\x1b[{kc}m[{k}]\x1b[0m\x1b[{lc}m{lbl}\x1b[0m";
        }

        bool anyDetail = DisplayState.Eff(DisplayState.OvHeader,    disp.ShowHeader)
                      || DisplayState.Eff(DisplayState.OvSparkline, disp.ShowSparkline)
                      || DisplayState.Eff(DisplayState.OvColHeader, disp.ShowColumnHeader);

        var hint = "  "
            + K("1", "Header ",  DisplayState.Eff(DisplayState.OvHeader,    disp.ShowHeader))    + "  "
            + K("2", "Bars ",    DisplayState.Eff(DisplayState.OvSysBar,    disp.ShowSysBars))   + "  "
            + K("3", "Spark ",   DisplayState.Eff(DisplayState.OvSparkline, disp.ShowSparkline)) + "  "
            + K("4", "Cols ",    DisplayState.Eff(DisplayState.OvColHeader, disp.ShowColumnHeader)) + "  "
            + K("K", "Hint ",    true)             + "  "
            + K("M", "Minimal ", !anyDetail)        + "  "
            + "\x1b[38;2;80;80;80m[Q]Quit\x1b[0m";

        L("|hint|" + anyDetail, hint);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Bar rendering  — sub-character precision
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Append a block-element bar to sb.
    /// Each character cell = 8 sub-units → 288 positions for barWidth=36.
    /// The percentage shown in text and the bar width are driven by the same
    /// smoothed value so they always agree.
    /// </summary>
    private static void AppendBar(StringBuilder sb, double val, int bw, string color)
    {
        double filled  = val / 100.0 * (bw * 8.0);
        int    full    = (int)(filled / 8);
        int    partial = (int)(filled % 8);
        int    empty   = Math.Max(0, bw - full - (partial > 0 ? 1 : 0));

        sb.Append(CDim + "[");
        sb.Append(color);
        sb.Append(new string('█', Math.Min(full, bw)));
        if (partial > 0 && full < bw) sb.Append(BlockFill[partial]);
        if (empty   > 0)              sb.Append(new string(' ', empty));
        sb.Append(CDim + "]" + Reset);
    }

    /// <summary>Plain-text key for diff buffer (no ANSI).</summary>
    private static string BarPlainKey(double val, int bw)
    {
        int full = (int)(val / 100.0 * bw);
        return "|" + new string('#', full) + new string('.', bw - full);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Sparkline
    // ════════════════════════════════════════════════════════════════════════════

    private static void AppendSparkline(StringBuilder sb, Queue<double> hist, int maxLen, string color)
    {
        sb.Append(color);
        int pad = maxLen - hist.Count;
        if (pad > 0) sb.Append(new string(' ', pad));
        foreach (var v in hist)
            sb.Append(SparkChar[Math.Clamp((int)(v / 100.0 * 8), 0, 8)]);
        sb.Append(Reset);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Shared helpers
    // ════════════════════════════════════════════════════════════════════════════

    private static void ColHead(StringBuilder sb, string label, int width, bool active, bool desc)
    {
        var arrow   = active ? (desc ? "▼" : "▲") : "";
        var content = label + arrow;
        var text    = width < 0 ? content.PadRight(-width) : content.PadLeft(width);
        sb.Append(active ? CWhite + Bold + text + Reset : CDim + text + Reset);
    }

    private static void Indicator(StringBuilder sb, bool on)
        => sb.Append(on ? CGreen + "ON" + Reset : CRed + "OFF" + Reset);

    /// <summary>ANSI colour for a percentage value (matches the block bar colour).</summary>
    private static string PctAnsi(double p) => p switch
    {
        >= 95 => Fg(255,  0,  80),   // hot pink — CRITICAL
        >= 85 => Fg(230, 60,  60),   // red      — VERY HIGH
        >= 70 => Fg(255,140,   0),   // orange   — HIGH
        >= 55 => Fg(255,220,   0),   // yellow   — ELEVATED
        >= 35 => Fg(160,230,  80),   // lime     — MODERATE
        >= 15 => Fg( 80,220, 120),   // green    — NORMAL
        _     => Fg(100,180, 255),   // blue     — IDLE
    };

    /// <summary>Short label text for a percentage.</summary>
    private static string PctLabel(double p) => p switch
    {
        >= 95 => CRed    + "CRITICAL"  + Reset,
        >= 85 => CRed    + "VERY HIGH" + Reset,
        >= 70 => COrange + "HIGH"      + Reset,
        >= 55 => CYellow + "ELEVATED"  + Reset,
        >= 35 => CGreen  + "MODERATE"  + Reset,
        >= 15 => CGreen  + "NORMAL"    + Reset,
        _     => CCyan   + "IDLE"      + Reset,
    };

    private static RowHighlight? MatchRowHighlight(
        double cpuPct, double memPct, List<RowHighlight> rules)
    {
        foreach (var r in rules)
            if (r.Metric.Equals("cpu", StringComparison.OrdinalIgnoreCase) &&
                cpuPct >= r.Min && cpuPct <= r.Max) return r;
        foreach (var r in rules)
            if (r.Metric.Equals("memory", StringComparison.OrdinalIgnoreCase) &&
                memPct >= r.Min && memPct <= r.Max) return r;
        return null;
    }

    private static string AnsiForUsage(double pct, List<ColorMapping> thresholds)
    {
        var m = thresholds.OrderByDescending(t => t.Threshold).FirstOrDefault(t => pct >= t.Threshold);
        if (m is null) return CWhite;
        return m.Color.ToUpperInvariant() switch
        {
            "#FF0000"              => CRed,
            "#FF8800" or "#FFA500" => COrange,
            "#FFFF00"              => CYellow,
            "#00FF00" or "#008000" => CGreen,
            "#00FFFF" or "#008080" => CCyan,
            "#0000FF" or "#000080" => CBlue,
            _                      => FgHex(m.Color),
        };
    }

    // ── Line helpers ──────────────────────────────────────────────────────────

    private static void L(string plain, string ansi) => _lines.Add((plain, ansi));

    private static void L(string plain, Action<StringBuilder> build)
    {
        _sb.Clear();
        build(_sb);
        _lines.Add((plain, _sb.ToString()));
    }
}
