// File: ProcessInfo.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

using System.Diagnostics;

namespace DotnetHtop;

public record ProcessSnapshot(int Id, string Name, TimeSpan CpuTime, long MemoryBytes, int Threads);

public class ProcessInfo
{
    public int    Id           { get; init; }
    public string Name         { get; init; } = string.Empty;
    public double CpuPercent   { get; init; }   // normalized 0-100 per logical core
    public long   MemoryMb     { get; init; }
    public int    Threads      { get; init; }
    public string Status       { get; init; } = string.Empty;
}

public static class ProcessCollector
{
    /// <summary>
    /// Take a snapshot of all running processes (Id, Name, CpuTime, MemoryBytes).
    /// Silently skips processes we cannot access.
    /// </summary>
    public static Dictionary<int, ProcessSnapshot> Snapshot()
    {
        var map = new Dictionary<int, ProcessSnapshot>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                map[p.Id] = new ProcessSnapshot(
                    p.Id,
                    p.ProcessName,
                    p.TotalProcessorTime,
                    p.WorkingSet64,
                    p.Threads.Count
                );
            }
            catch { /* access denied or process exited — skip */ }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }
        return map;
    }

    /// <summary>
    /// Compute per-process CPU % from two snapshots taken <paramref name="elapsedMs"/> ms apart.
    /// Formula: (cpuDelta / (elapsedMs * coreCount)) * 100
    /// </summary>
    public static List<ProcessInfo> Compute(
        Dictionary<int, ProcessSnapshot> snap1,
        Dictionary<int, ProcessSnapshot> snap2,
        double elapsedMs,
        int cores)
    {
        var results = new List<ProcessInfo>(snap2.Count);
        double divisor = elapsedMs * cores;

        foreach (var (id, s2) in snap2)
        {
            double cpuPct = 0;
            if (snap1.TryGetValue(id, out var s1) && divisor > 0)
            {
                var deltaTicks = (s2.CpuTime - s1.CpuTime).TotalMilliseconds;
                cpuPct = Math.Clamp((deltaTicks / divisor) * 100.0, 0, 100);
            }

            results.Add(new ProcessInfo
            {
                Id         = id,
                Name       = s2.Name,
                CpuPercent = cpuPct,
                MemoryMb   = s2.MemoryBytes / 1024 / 1024,
                Threads    = s2.Threads,
                Status     = cpuPct > 0.01 ? "running" : "sleeping",
            });
        }
        return results;
    }
}
