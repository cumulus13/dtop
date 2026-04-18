// File: EmailNotifier.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

using System.Net;
using System.Net.Mail;

namespace DotnetHtop;

/// <summary>
/// Sends alert emails via SMTP with strict rate-limiting:
///   - Per-type cooldown (e.g. 5 min between CPU alerts)
///   - Global hourly cap (e.g. max 6 emails/hour total)
/// All settings come from dtop.json → "email" section.
/// </summary>
public class EmailNotifier
{
    private readonly EmailConfig _cfg;

    // Per alert-type last-sent tracking
    private readonly Dictionary<string, DateTime> _lastSent = new();

    // Rolling window for hourly cap
    private readonly Queue<DateTime> _sentTimes = new();

    public EmailNotifier(EmailConfig cfg)
    {
        _cfg = cfg;
    }

    public bool IsEnabled => _cfg.Enabled;

    public void Send(string subject, string body, string alertType = "General")
    {
        if (!_cfg.Enabled) return;
        if (string.IsNullOrWhiteSpace(_cfg.SmtpHost) ||
            string.IsNullOrWhiteSpace(_cfg.Username)  ||
            string.IsNullOrWhiteSpace(_cfg.To))
        {
            Console.Error.WriteLine("[Email] Not configured — check dtop.json email section.");
            return;
        }

        var now = DateTime.Now;

        // Per-type cooldown
        if (_lastSent.TryGetValue(alertType, out var last) &&
            (now - last).TotalSeconds < _cfg.CooldownSeconds)
            return;

        // Hourly cap — evict entries older than 1 hour
        while (_sentTimes.Count > 0 && (now - _sentTimes.Peek()).TotalHours > 1)
            _sentTimes.Dequeue();

        if (_sentTimes.Count >= _cfg.MaxPerHour)
        {
            Console.Error.WriteLine($"[Email] Hourly cap ({_cfg.MaxPerHour}) reached — skipping.");
            return;
        }

        _lastSent[alertType] = now;
        _sentTimes.Enqueue(now);

        // Fire and forget
        Task.Run(() => SendMail(subject, body, now));
    }

    public void Test() =>
        Send("[DTOP Test] Email notification", "DTOP email notifications are working.", "Test");

    private void SendMail(string subject, string body, DateTime timestamp)
    {
        try
        {
            using var smtp = new SmtpClient(_cfg.SmtpHost, _cfg.SmtpPort)
            {
                EnableSsl   = _cfg.UseSsl,
                Credentials = new NetworkCredential(_cfg.Username, _cfg.Password),
                Timeout     = 10_000,
            };

            var from = string.IsNullOrWhiteSpace(_cfg.From) ? _cfg.Username : _cfg.From;

            var mail = new MailMessage
            {
                From       = new MailAddress(from, "DTOP Monitor"),
                Subject    = subject,
                Body       = $"{body}\n\nTimestamp: {timestamp:yyyy-MM-dd HH:mm:ss}\nHost: {Environment.MachineName}",
                IsBodyHtml = false,
            };
            mail.To.Add(_cfg.To);

            smtp.Send(mail);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Email] Send failed: {ex.Message}");
        }
    }
}
