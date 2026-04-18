// File: Program.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotnetHtop;

public enum SortColumn { Cpu, Memory, Pid, Name, Threads, Status }

public sealed class AppState
{
    public Config               Config               { get; set; } = new();
    public SortColumn           SortColumn           { get; set; } = SortColumn.Cpu;
    public bool                 SortDescending       { get; set; } = true;
    public bool                 NotificationsEnabled { get; set; } = true;
    public bool                 GrowlEnabled         { get; set; } = true;
    public bool                 EmailEnabled         { get; set; } = true;
    public bool                 Paused               { get; set; } = false;
    public bool                 ShouldExit           { get; set; } = false;
    public DateTime             LastCpuAlert         { get; set; } = DateTime.MinValue;
    public DateTime             LastMemAlert         { get; set; } = DateTime.MinValue;
    public NotificationService? Notifications        { get; set; }
    public ulong                TotalMemoryMb        { get; set; }
    public int                  Cores                { get; set; }
    public DateTime             StartTime            { get; set; } = DateTime.Now;

    // Set by ConfigWatcher or manual R key — shown in footer
    public DateTime             LastReloadTime       { get; set; } = DateTime.MinValue;
    public string               LastReloadSource     { get; set; } = string.Empty;
}

public static class Program
{
    private static int _lastWinW = 0;
    private static int _lastWinH = 0;

    public static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        EnableAnsiOnWindows();
        Console.Write("\x1b[?25l\x1b[2J\x1b[H");

        var cfg = Config.Load();

        var state = new AppState
        {
            Config        = cfg,
            TotalMemoryMb = SystemInfo.TotalPhysicalMemoryMb(),
            Cores         = SystemInfo.LogicalCoreCount(),
            GrowlEnabled  = cfg.Growl.Enabled,
            EmailEnabled  = cfg.Email.Enabled,
        };

        try { state.Notifications = new NotificationService(cfg); }
        catch (Exception ex) { ColorConsole.WriteWarning($"Notification init failed: {ex.Message}"); }

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; state.ShouldExit = true; };

        // ── Start file watcher — auto-reload on dtop.json save ────────────────
        using var watcher = new ConfigWatcher(state);

        // ── Keyboard thread ───────────────────────────────────────────────────
        new Thread(() => KeyboardLoop(state)) { IsBackground = true, Name = "KeyboardThread" }.Start();

        DisplayLoop(state);

        Console.Write("\x1b[?25h\x1b[2J\x1b[H");
        ColorConsole.WriteSuccess("DTOP closed. Goodbye!");
    }

    // ── Display Loop ──────────────────────────────────────────────────────────

    private static void DisplayLoop(AppState state)
    {
        var sw = Stopwatch.StartNew();

        while (!state.ShouldExit)
        {
            if (Console.WindowWidth != _lastWinW || Console.WindowHeight != _lastWinH)
            {
                _lastWinW = Console.WindowWidth;
                _lastWinH = Console.WindowHeight;
                Console.Write("\x1b[2J");
                Renderer.Invalidate();
            }

            if (state.Paused)
            {
                Renderer.RenderFrame(state, 0, 0, 0, Enumerable.Empty<ProcessInfo>(), 0);
                Thread.Sleep(150);
                continue;
            }

            var snap1 = ProcessCollector.Snapshot();
            sw.Restart();

            var slices = Math.Max(1, state.Config.RefreshIntervalMs / 50);
            for (int i = 0; i < slices && !state.ShouldExit && !state.Paused; i++)
                Thread.Sleep(50);

            if (state.ShouldExit) break;

            sw.Stop();
            var snap2     = ProcessCollector.Snapshot();
            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var processes = ProcessCollector.Compute(snap1, snap2, elapsedMs, state.Cores);

            var avgCpu    = Math.Min(100, processes.Sum(p => p.CpuPercent) / state.Cores);
            var usedMemMb = processes.Sum(p => p.MemoryMb);
            var memPct    = state.TotalMemoryMb > 0
                ? Math.Min(100, (usedMemMb / (double)state.TotalMemoryMb) * 100.0) : 0;

            CheckAlerts(state, avgCpu, memPct, usedMemMb);

            var sorted = Sort(processes, state);
            Renderer.RenderFrame(state, avgCpu, memPct, usedMemMb, sorted, processes.Count);
        }
    }

    // ── Sort ──────────────────────────────────────────────────────────────────

    private static IEnumerable<ProcessInfo> Sort(List<ProcessInfo> list, AppState state)
    {
        bool desc = state.SortDescending;
        return state.SortColumn switch
        {
            SortColumn.Cpu     => desc ? list.OrderByDescending(p => p.CpuPercent) : list.OrderBy(p => p.CpuPercent),
            SortColumn.Memory  => desc ? list.OrderByDescending(p => p.MemoryMb)   : list.OrderBy(p => p.MemoryMb),
            SortColumn.Pid     => desc ? list.OrderByDescending(p => p.Id)         : list.OrderBy(p => p.Id),
            SortColumn.Name    => desc ? list.OrderByDescending(p => p.Name)       : list.OrderBy(p => p.Name),
            SortColumn.Threads => desc ? list.OrderByDescending(p => p.Threads)    : list.OrderBy(p => p.Threads),
            SortColumn.Status  => desc ? list.OrderByDescending(p => p.Status)     : list.OrderBy(p => p.Status),
            _                  => list.OrderByDescending(p => p.CpuPercent),
        };
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    private static void KeyboardLoop(AppState state)
    {
        while (!state.ShouldExit)
        {
            if (!Console.KeyAvailable) { Thread.Sleep(30); continue; }
            var key = Console.ReadKey(intercept: true);
            HandleKey(key.KeyChar, key.Key, state);
        }
    }

    private static void HandleKey(char ch, ConsoleKey key, AppState state)
    {
        void SetSort(SortColumn col)
        {
            if (state.SortColumn == col) state.SortDescending = !state.SortDescending;
            else { state.SortColumn = col; state.SortDescending = true; }
            Renderer.Invalidate();
        }

        switch (char.ToUpperInvariant(ch))
        {
            case 'Q': state.ShouldExit = true;                                   break;
            case 'C': SetSort(SortColumn.Cpu);                                   break;
            case 'M': SetSort(SortColumn.Memory);                                break;
            case 'I': SetSort(SortColumn.Pid);                                   break;
            case 'E': SetSort(SortColumn.Name);                                  break;
            case 'H': SetSort(SortColumn.Threads);                               break;
            case 'S': SetSort(SortColumn.Status);                                break;
            case 'D': state.SortDescending = true;  Renderer.Invalidate();      break;
            case 'A': state.SortDescending = false; Renderer.Invalidate();      break;
            case 'P': state.Paused = !state.Paused; Renderer.Invalidate();      break;
            case 'N':
                state.NotificationsEnabled = !state.NotificationsEnabled;
                Renderer.Invalidate();
                break;
            case 'G':
                state.GrowlEnabled = !state.GrowlEnabled;
                state.Config.Growl.Enabled = state.GrowlEnabled;
                state.Notifications?.Reload(state.Config);
                Renderer.Invalidate();
                break;
            case 'L':
                state.EmailEnabled = !state.EmailEnabled;
                state.Config.Email.Enabled = state.EmailEnabled;
                state.Notifications?.Reload(state.Config);
                Renderer.Invalidate();
                break;
            case 'T': state.Notifications?.TestAll();                            break;
            case 'R':
                // Manual reload — same as auto but marks source as "manual"
                state.Config = Config.Load();
                state.GrowlEnabled = state.Config.Growl.Enabled;
                state.EmailEnabled = state.Config.Email.Enabled;
                state.Notifications?.Reload(state.Config);
                state.LastReloadTime   = DateTime.Now;
                state.LastReloadSource = "manual (R key)";
                Renderer.Invalidate();
                break;
        }

        if (key == ConsoleKey.UpArrow)   { state.SortDescending = false; Renderer.Invalidate(); }
        if (key == ConsoleKey.DownArrow) { state.SortDescending = true;  Renderer.Invalidate(); }
    }

    // ── Alerts ────────────────────────────────────────────────────────────────

    private static void CheckAlerts(AppState state, double avgCpu, double memPct, long usedMemMb)
    {
        if (!state.NotificationsEnabled || state.Notifications is null) return;
        var now = DateTime.Now;
        var cd  = TimeSpan.FromSeconds(state.Config.NotificationCooldownSeconds);

        if (avgCpu > state.Config.CpuHighThreshold && now - state.LastCpuAlert > cd)
        {
            state.Notifications.ShowNotification(
                "CPU High", $"Average CPU at {avgCpu:F1}% across {state.Cores} cores", "CPU");
            state.LastCpuAlert = now;
        }
        if (memPct > state.Config.MemoryHighThresholdPercent && now - state.LastMemAlert > cd)
        {
            state.Notifications.ShowNotification(
                "Memory High", $"{memPct:F1}% — {usedMemMb:N0} / {state.TotalMemoryMb:N0} MB", "Memory");
            state.LastMemAlert = now;
        }
    }

    // ── Windows ANSI ──────────────────────────────────────────────────────────

    private static void EnableAnsiOnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var h = GetStdHandle(-11);
            GetConsoleMode(h, out var m);
            SetConsoleMode(h, m | 0x0004);
        }
        catch { }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr h, out uint m);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr h, uint m);
}
