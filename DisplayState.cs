// File: DisplayState.cs
// Shared toggle-override state for all keyboard-controlled display flags.
//
// Tri-state int:  -1 = follow config (default)
//                  0 = force OFF
//                  1 = force ON
//
// volatile int is atomically readable/writable on x86/x64 (bool? is not — CS0677).
// All writes come from the keyboard thread; all reads come from the render thread.
// No lock needed: worst case is one stale frame, which is invisible.

namespace DotnetHtop;

public static class DisplayState
{
    // ── Visible sections ──────────────────────────────────────────────────────
    public static volatile int OvHeader    = -1;   // [1] cores / notif / sort keys
    public static volatile int OvSysBar    = -1;   // [2] CPU + MEM bars
    public static volatile int OvSparkline = -1;   // [3] sparkline history under bars
    public static volatile int OvColHeader = -1;   // [4] column header row
    public static volatile int OvHint      = -1;   // [5] keyboard hint bar at bottom

    // ── Toggle helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Cycle: -1 (follow config) → !cfgValue as 0/1 → -1 (follow config) → …
    /// First press overrides; second press returns to config.
    /// </summary>
    public static int Cycle(int current, bool cfgValue)
        => current != -1 ? -1 : (cfgValue ? 0 : 1);

    /// <summary>Resolve effective bool: override wins over config.</summary>
    public static bool Eff(int ov, bool cfgValue)
        => ov == -1 ? cfgValue : ov == 1;

    /// <summary>True when a keyboard override is active.</summary>
    public static bool IsOverridden(int ov) => ov != -1;

    // ── Minimal mode ─────────────────────────────────────────────────────────
    // [M] collapses header, sparkline, column header, and hint to bars-only.
    // [M] again restores all overrides to -1 (follow config).

    public static void ToggleMinimal(AppDisplayConfig cfg)
    {
        bool anyDetail = Eff(OvHeader,    cfg.ShowHeader)
                      || Eff(OvSparkline, cfg.ShowSparkline)
                      || Eff(OvColHeader, cfg.ShowColumnHeader)
                      || Eff(OvHint,      cfg.ShowHint);

        if (anyDetail)
        {
            // Collapse
            OvHeader    = 0;
            OvSparkline = 0;
            OvColHeader = 0;
            OvHint      = 0;
            // Keep SysBar and process list always visible in minimal mode
        }
        else
        {
            // Restore to config
            OvHeader    = -1;
            OvSparkline = -1;
            OvColHeader = -1;
            OvHint      = -1;
        }
    }
}
