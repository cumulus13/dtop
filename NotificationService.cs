// File: NotificationService.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

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
    private bool          _osNotifAvailable;
    private GrowlNotifier _growl = null!;
    private EmailNotifier _email = null!;

    // OS-level cooldown (separate from Growl/Email cooldowns)
    private readonly Dictionary<string, DateTime> _osLastSent = new();
    private int _osCooldownSeconds;

    public NotificationService(Config cfg)
    {
        Reload(cfg);
        _osNotifAvailable = DetectOsNotif();
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
        if (!_osNotifAvailable) return;

        var now = DateTime.Now;
        if (_osLastSent.TryGetValue(alertType, out var last) &&
            (now - last).TotalSeconds < _osCooldownSeconds)
            return;

        _osLastSent[alertType] = now;
        Task.Run(() =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    Run("notify-send", $"\"{title}\" \"{body}\"");
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Run("osascript", $"-e 'display notification \"{body}\" with title \"{title}\"'");
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    RunPowerShellToast(title, body);
            }
            catch { }
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
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            });
            p?.WaitForExit(1000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void Run(string file, string args)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = file, Arguments = args,
            UseShellExecute = false, CreateNoWindow = true,
        });
        p?.WaitForExit(3000);
    }

    private static void RunPowerShellToast(string title, string body)
    {
        var script = $@"
[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]|Out-Null
$t=([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$x=[Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent($t)
$x.GetElementsByTagName('text')[0].AppendChild($x.CreateTextNode('{title.Replace("'","")}'))|Out-Null
$x.GetElementsByTagName('text')[1].AppendChild($x.CreateTextNode('{body.Replace("'","")}'))|Out-Null
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('DTOP').Show([Windows.UI.Notifications.ToastNotification]::new($x))
".Trim();
        Run("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"");
    }
}
