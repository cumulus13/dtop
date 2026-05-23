// File: SystemInfo.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

// Cross-platform system information helpers.
// WMI / WMIC / PowerShell are NOT used anywhere in this file.
// Windows RAM is read via GC.GetGCMemoryInfo() (.NET 5+ built-in).

using System.Runtime.InteropServices;

namespace DotnetHtop;

public static class SystemInfo
{
    /// <summary>
    /// Total physical memory in MB.
    /// Sources in priority order — no WMI, no PowerShell:
    ///   1. GC.GetGCMemoryInfo()   — .NET 5+, all platforms, most reliable
    ///   2. /proc/meminfo          — Linux fallback
    ///   3. sysctl hw.memsize      — macOS fallback
    ///   4. 8192 MB safe default
    /// </summary>
    public static ulong TotalPhysicalMemoryMb()
    {
        // 1. .NET built-in (works on Windows, Linux, macOS — no external deps)
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return (ulong)(info.TotalAvailableMemoryBytes / 1024 / 1024);
        }
        catch { }

        // 2. Linux: /proc/meminfo
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (!line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && ulong.TryParse(parts[1], out var kb))
                        return kb / 1024;
                }
            }
            catch { }
        }

        // 3. macOS: sysctl (native binary, not PowerShell)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("sysctl", "-n hw.memsize")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(1000);
                if (ulong.TryParse(output, out var bytes))
                    return bytes / 1024 / 1024;
            }
            catch { }
        }

        // 4. Safe default — 8 GB
        return 8192;
    }

    public static int LogicalCoreCount() => Environment.ProcessorCount;

    public static string OsSummary()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var ver  = Environment.OSVersion.Version;
            var name = ver.Build >= 22000 ? "Windows 11" : "Windows 10";
            return $"{name} (build {ver.Build}) [{RuntimeInformation.ProcessArchitecture}]";
        }

        var os = RuntimeInformation.OSDescription;
        if (os.Length > 40) os = os[..40].TrimEnd() + "…";
        return $"{os} [{RuntimeInformation.ProcessArchitecture}]";
    }
}
