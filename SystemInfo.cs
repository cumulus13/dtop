// File: SystemInfo.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

using System.Runtime.InteropServices;

namespace DotnetHtop;

public static class SystemInfo
{
    /// <summary>
    /// Total physical memory in MB. Cross-platform: .NET 5+ GC info,
    /// fallback to /proc/meminfo on Linux, sysctl on macOS, then a safe default.
    /// </summary>
    public static ulong TotalPhysicalMemoryMb()
    {
        // Best: .NET built-in (works on all platforms, .NET 5+)
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return (ulong)(info.TotalAvailableMemoryBytes / 1024 / 1024);
        }
        catch { }

        // Linux fallback: /proc/meminfo
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (!line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && ulong.TryParse(parts[1], out var kb))
                        return kb / 1024;
                }
            }
            catch { }
        }

        // macOS fallback: sysctl
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("sysctl", "-n hw.memsize")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(1000);
                if (ulong.TryParse(output, out var bytes))
                    return bytes / 1024 / 1024;
            }
            catch { }
        }

#if WINDOWS
        // Windows fallback via WMI
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var kb = Convert.ToUInt64(obj["TotalVisibleMemorySize"]);
                return kb / 1024;
            }
        }
        catch { }
#endif

        return 8192; // safe fallback: 8 GB
    }

    public static int LogicalCoreCount() => Environment.ProcessorCount;

    public static string OsSummary()
    {
        var os = RuntimeInformation.OSDescription;

        // Windows: "Microsoft Windows 10.0.22621" → "Windows 11 (X64)"
        // Trim long build strings to just major name + arch
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Extract version number to determine Windows 10 vs 11
            var ver = Environment.OSVersion.Version;
            var name = ver.Build >= 22000 ? "Windows 11" : "Windows 10";
            return $"{name} (build {ver.Build}) [{RuntimeInformation.ProcessArchitecture}]";
        }

        // Linux/macOS: truncate at 40 chars to avoid overflow
        if (os.Length > 40) os = os[..40].TrimEnd() + "…";
        return $"{os} [{RuntimeInformation.ProcessArchitecture}]";
    }
}
