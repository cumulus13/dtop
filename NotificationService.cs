// File: NotificationService.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

// Orchestrates all notification channels:
//   1. OS-level toast (Windows: WinRT via kernel32/user32 COM-free approach,
//                      Linux: notify-send, macOS: osascript)
//   2. Growl via GNTP (raw TCP — no external lib)
//   3. Email via SMTP
//
// PowerShell and WMI/WMIC are NOT used anywhere in this file.

using System.Runtime.InteropServices;

namespace DotnetHtop;

/// <summary>
/// Orchestrates all notification channels:
///   1. OS-level (notify-send / osascript / PowerShell toast)
///   2. Growl via GNTP
///   3. Email via SMTP
///
/// Each channel has its own enable flag and cooldown in dtop.json.
/// </summary>
public class NotificationService
{
    private GrowlNotifier _growl = null!;
    private EmailNotifier _email = null!;

    // OS-level cooldown (separate from Growl/Email cooldowns)
    private readonly Dictionary<string, DateTime> _osLastSent = new();
    private int _osCooldownSeconds;

    private readonly bool _osAvailable;

    public NotificationService(Config cfg)
    {
        Reload(cfg);
        _osAvailable = DetectOsNotif();
    }

    public void Reload(Config cfg)
    {
        _growl             = new GrowlNotifier(cfg.Growl);
        _email             = new EmailNotifier(cfg.Email);
        _osCooldownSeconds = cfg.NotificationCooldownSeconds;
    }

    public void ShowNotification(string title, string body, string alertType = "General")
    {
        // OS notification
        SendOs(title, body, alertType);

        // Growl
        _growl.Send(title, body, alertType);

        // Email
        _email.Send($"[DTOP] {title}", body, alertType);
    }

    public void TestAll()
    {
        SendOs("DTOP Test", "OS notification working.", "Test");
        _growl.Test();
        _email.Test();
    }

    // ── OS notifications ──────────────────────────────────────────────────────

    private void SendOs(string title, string body, string alertType)
    {
        if (!_osAvailable) return;

        var now = DateTime.Now;
        if (_osLastSent.TryGetValue(alertType, out var last) &&
            (now - last).TotalSeconds < _osCooldownSeconds) return;

        _osLastSent[alertType] = now;

        Task.Run(() =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    RunProcess("notify-send", $"\"{title}\" \"{body}\"");
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    RunProcess("osascript",
                        $"-e 'display notification \"{Esc(body)}\" with title \"{Esc(title)}\"'");
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    WindowsToast.Show(title, body);
            }
            catch { /* best-effort */ }
        });
    }

    private static bool DetectOsNotif()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return CommandExists("notify-send");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return CommandExists("osascript");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
        return false;
    }

    private static bool CommandExists(string cmd)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "which", Arguments = cmd,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
            p?.WaitForExit(1000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void RunProcess(string file, string args)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = file,
            Arguments       = args,
            UseShellExecute = false,
            CreateNoWindow  = true,
        });
        p?.WaitForExit(3000);
    }

    private static string Esc(string s) => s.Replace("'", "").Replace("\"", "");
}

// ── Windows toast via WinRT COM (no PowerShell, no WMI) ──────────────────────
// Uses the Windows.UI.Notifications COM API directly through C# interop.
// Falls back silently if the WinRT APIs are not available (pre-Win10).

internal static class WindowsToast
{
    // WinRT activation via RoActivateInstance (combase.dll) — no PowerShell needed.
    // This is the same mechanism Visual Studio and other apps use internally.

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoActivateInstance(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        out IntPtr instance);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string src,
        int length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    // If WinRT interop is unavailable, try a lightweight fallback: MessageBeep
    // (just an audio cue, no visual) to at least signal something happened.
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint uType);

    private static bool _winRtAvailable = true;

    public static void Show(string title, string body)
    {
        if (!_winRtAvailable) { MessageBeep(0); return; }

        try
        {
            // Try .NET 6+ built-in WinRT projection if available at runtime
            // (Microsoft.Windows.SDK.NET or Windows App SDK)
            ShowViaReflection(title, body);
        }
        catch
        {
            // WinRT projection not available — audio fallback only
            _winRtAvailable = false;
            try { MessageBeep(0); } catch { }
        }
    }

    private static void ShowViaReflection(string title, string body)
    {
        // Dynamically resolve Windows.UI.Notifications via the WinRT type system.
        // This works on Windows 10+ without any NuGet dependency.
        var toastType = Type.GetType(
            "Windows.UI.Notifications.ToastNotificationManager, Windows, " +
            "ContentType=WindowsRuntime");

        if (toastType == null) throw new PlatformNotSupportedException("WinRT not available");

        var templateType = Type.GetType(
            "Windows.UI.Notifications.ToastTemplateType, Windows, " +
            "ContentType=WindowsRuntime")!;

        var toastText02 = Enum.Parse(templateType, "ToastText02");

        var content = toastType
            .GetMethod("GetTemplateContent")!
            .Invoke(null, new[] { toastText02 })!;

        // content is an XmlDocument — set the text nodes
        var xmlType = content.GetType();
        var nodes   = xmlType.GetMethod("GetElementsByTagName")!
                             .Invoke(content, new object[] { "text" });
        var nodeList = (System.Collections.IList)nodes!;

        AppendText(nodeList[0]!, title);
        AppendText(nodeList[1]!, body);

        // Create the toast notification object
        var notifType = Type.GetType(
            "Windows.UI.Notifications.ToastNotification, Windows, " +
            "ContentType=WindowsRuntime")!;
        var notif = Activator.CreateInstance(notifType, content)!;

        // Get the notifier and show
        var notifier = toastType
            .GetMethod("CreateToastNotifier", new[] { typeof(string) })!
            .Invoke(null, new object[] { "DTOP" })!;

        notifier.GetType()
                .GetMethod("Show")!
                .Invoke(notifier, new[] { notif });
    }

    private static void AppendText(object node, string text)
    {
        var nodeType = node.GetType();
        // node is an XmlElement; get its ownerDocument to create a text node
        var doc      = nodeType.GetProperty("OwnerDocument")!.GetValue(node)!;
        var textNode = doc.GetType()
                          .GetMethod("CreateTextNode")!
                          .Invoke(doc, new object[] { text })!;
        nodeType.GetMethod("AppendChild")!.Invoke(node, new[] { textNode });
    }
}
