// File: SystemMetrics.cs
// Accurate system-wide CPU% and RAM% for the system bars.
//
// WHY NOT use process.Sum(CpuPercent)?
//   ProcessCollector.Compute normalizes each process to 0-100% per core.
//   Summing then dividing by cores double-normalizes, giving ~10% at real 68%.
//   This class reads the OS idle-time counters directly — same source as
//   Task Manager, Process Hacker, and CpuMonitor.
//
// CPU: GetSystemTimes (kernel32) idle-time delta.
//      KernelTime includes IdleTime.
//      busy  = (dKernel - dIdle) + dUser
//      total = dKernel + dUser
//      CPU%  = busy / total * 100
//
// RAM: PerformanceCounter "Memory / Available MBytes"
//      used% = (total - available) / total * 100

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DotnetHtop;

[SupportedOSPlatform("windows")]
public static class SystemMetrics
{
    // ── GetSystemTimes P/Invoke ───────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    private static ulong ToU64(FILETIME ft) => ((ulong)ft.High << 32) | ft.Low;

    // CPU state
    private static ulong _lastIdle   = 0;
    private static ulong _lastKernel = 0;
    private static ulong _lastUser   = 0;
    private static bool  _cpuReady   = false;

    // Fallback when GetSystemTimes is unavailable
    private static PerformanceCounter? _cpuFallback;
    private static bool                _useGst = true;

    // RAM counter
    private static PerformanceCounter? _ramAvail;
    private static ulong               _totalRamBytes;

    // Smoothing buffers (short rolling average — same anti-flicker as CpuMonitor)
    private static readonly Queue<double> _cpuBuf = new();
    private static readonly Queue<double> _ramBuf = new();

    // Last computed values (used by Renderer)
    public static double CpuPercent { get; private set; }
    public static double RamPercent { get; private set; }
    public static ulong  TotalRamBytes => _totalRamBytes;

    // ── Initialise ────────────────────────────────────────────────────────────

    public static void Init(int smoothWindow = 3)
    {
        // CPU — first call stores baseline
        double probe = ReadGst();
        if (probe < -1)   // -2 = API failed
        {
            _useGst = false;
            try   { _cpuFallback = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); }
            catch { _cpuFallback = new PerformanceCounter("Processor", "% Processor Time", "_Total"); }
            _cpuFallback.NextValue();
        }

        // RAM
        _ramAvail      = new PerformanceCounter("Memory", "Available MBytes");
        _totalRamBytes = GetTotalRamBytes();

        // Let counters settle + accumulate a real CPU delta
        Thread.Sleep(1000);

        // Seed with real first reading so display starts correct
        double seedCpu = _useGst
            ? Math.Max(0, ReadGst())
            : Math.Clamp(_cpuFallback!.NextValue(), 0f, 100f);
        double seedRam = ReadRamPct();

        for (int i = 0; i < smoothWindow; i++)
        {
            _cpuBuf.Enqueue(seedCpu);
            _ramBuf.Enqueue(seedRam);
        }
        CpuPercent = seedCpu;
        RamPercent = seedRam;
    }

    // ── Update (call once per display tick) ───────────────────────────────────

    public static void Update(int smoothWindow)
    {
        double rawCpu = _useGst
            ? ReadGst()
            : (_cpuFallback != null ? Math.Clamp(_cpuFallback.NextValue(), 0f, 100f) : 0);

        // -1 = first-call sentinel (baseline stored), hold previous value
        // -2 = API failed mid-run, switch to fallback
        if (rawCpu == -2)
        {
            _useGst = false;
            try { _cpuFallback ??= new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); }
            catch { }
            rawCpu = CpuPercent;
        }
        else if (rawCpu == -1)
        {
            rawCpu = CpuPercent;
        }

        double rawRam = ReadRamPct();

        CpuPercent = PushSmooth(_cpuBuf, rawCpu, smoothWindow);
        RamPercent = PushSmooth(_ramBuf, rawRam, smoothWindow);
    }

    // ── GetSystemTimes ────────────────────────────────────────────────────────

    private static double ReadGst()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return -2;

        ulong ci = ToU64(idle), ck = ToU64(kernel), cu = ToU64(user);

        if (!_cpuReady)
        {
            _lastIdle = ci; _lastKernel = ck; _lastUser = cu;
            _cpuReady = true;
            return -1;  // baseline stored, discard this reading
        }

        ulong di = ci - _lastIdle, dk = ck - _lastKernel, du = cu - _lastUser;
        _lastIdle = ci; _lastKernel = ck; _lastUser = cu;

        ulong total = dk + du;
        if (total == 0) return 0;

        // KernelTime includes IdleTime → busy = (kernel-idle) + user
        return Math.Clamp((double)((dk - di) + du) / total * 100.0, 0.0, 100.0);
    }

    // ── RAM ───────────────────────────────────────────────────────────────────

    private static double ReadRamPct()
    {
        if (_ramAvail == null || _totalRamBytes == 0) return 0;
        float  availMb = _ramAvail.NextValue();
        double totalMb = _totalRamBytes / (1024.0 * 1024.0);
        return Math.Clamp((totalMb - availMb) / totalMb * 100.0, 0, 100);
    }

    private static ulong GetTotalRamBytes()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return (ulong)info.TotalAvailableMemoryBytes;
        }
        catch { }
        return 0;
    }

    // ── Smoothing ─────────────────────────────────────────────────────────────

    private static double PushSmooth(Queue<double> q, double val, int window)
    {
        q.Enqueue(val);
        while (q.Count > Math.Max(1, window)) q.Dequeue();
        double sum = 0;
        foreach (var v in q) sum += v;
        return sum / q.Count;
    }

    // ── RAM label (for display) ───────────────────────────────────────────────

    public static string RamLabel()
    {
        if (_totalRamBytes == 0) return "";
        double totalGb = _totalRamBytes / (1024.0 * 1024.0 * 1024.0);
        double usedGb  = totalGb * RamPercent / 100.0;
        return $"{usedGb:F1}/{totalGb:F0} GB";
    }
}
