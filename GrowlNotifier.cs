// File: GrowlNotifier.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace DotnetHtop;

/// <summary>
/// Sends Growl notifications via raw GNTP/1.0 over TCP.
///
/// WHY NOT Growl.Connector NuGet?
///   The official Growl.Connector package only targets .NET Framework (net40).
///   It does NOT support .NET 5/6/7/8. Attempting to install it on a net8.0
///   project will either fail or require unsafe compatibility shims.
///   This raw TCP implementation is the correct, dependency-free approach for
///   modern .NET and is fully compatible with:
///     - Growl for Windows  (growlforwindows.com)
///     - Growl for macOS    (growl.info)
///     - Any GNTP-compatible server
///
/// ICON SUPPORT:
///   Place dtop.png next to the executable (or dtop.json).
///   The icon is read once, base64-encoded, and embedded in the GNTP header.
///   Growl will cache it after the first registration.
/// </summary>
public class GrowlNotifier
{
    private readonly GrowlConfig _cfg;
    private readonly Dictionary<string, DateTime> _lastSent = new();

    // Icon loaded once at startup
    private readonly byte[]? _iconBytes;
    private readonly string  _iconId;

    // Notification type names registered with Growl
    private const string TypeAlert  = "Alert";
    private const string TypeCpu    = "CPU";
    private const string TypeMemory = "Memory";
    private const string TypeTest   = "Test";

    private static readonly string[] AllTypes = [TypeAlert, TypeCpu, TypeMemory, TypeTest];

    private bool _registered = false;

    public GrowlNotifier(GrowlConfig cfg)
    {
        _cfg     = cfg;
        _iconId  = Guid.NewGuid().ToString("N");
        _iconBytes = LoadIcon();
    }

    public bool IsEnabled => _cfg.Enabled;

    public void Send(string title, string body, string alertType = TypeAlert)
    {
        if (!_cfg.Enabled) return;

        var now = DateTime.Now;
        if (_lastSent.TryGetValue(alertType, out var last) &&
            (now - last).TotalSeconds < _cfg.CooldownSeconds)
            return;

        _lastSent[alertType] = now;
        Task.Run(() => SendGntp(title, body, alertType));
    }

    public void Test() => Send("DTOP Test", "Growl is connected and working.", TypeTest);

    // ── GNTP implementation ───────────────────────────────────────────────────

    private void SendGntp(string title, string body, string alertType)
    {
        try
        {
            // Register app + notification types (only needs to happen once,
            // but re-registering is harmless and ensures icon is fresh)
            if (!_registered)
            {
                var reg = BuildRegister();
                SendAndReceive(reg);
                _registered = true;
            }

            var notify = BuildNotify(title, body, alertType);
            SendAndReceive(notify);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Growl] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// REGISTER message — tells Growl about our app and its notification types.
    /// If we have an icon, it is sent as inline binary data using GNTP's
    /// {identifier} / Content-Length block syntax.
    /// </summary>
    private string BuildRegister()
    {
        var sb = new StringBuilder();

        if (_iconBytes is not null)
        {
            // GNTP with inline binary resource
            sb.AppendLine("GNTP/1.0 REGISTER NONE");
            sb.AppendLine($"Application-Name: {_cfg.AppName}");
            sb.AppendLine($"Application-Icon: x-growl-resource://{_iconId}");
            sb.AppendLine($"Notifications-Count: {AllTypes.Length}");
            sb.AppendLine();

            foreach (var t in AllTypes)
            {
                sb.AppendLine($"Notification-Name: {t}");
                sb.AppendLine($"Notification-Display-Name: {_cfg.AppName} — {t}");
                sb.AppendLine($"Notification-Icon: x-growl-resource://{_iconId}");
                sb.AppendLine("Notification-Enabled: True");
                sb.AppendLine();
            }

            // Binary resource block
            sb.AppendLine($"Identifier: {_iconId}");
            sb.AppendLine($"Length: {_iconBytes.Length}");
            sb.AppendLine();
            // Raw bytes follow — handled in BuildPayload()
        }
        else
        {
            // No icon — plain text only
            sb.AppendLine("GNTP/1.0 REGISTER NONE");
            sb.AppendLine($"Application-Name: {_cfg.AppName}");
            sb.AppendLine($"Notifications-Count: {AllTypes.Length}");
            sb.AppendLine();

            foreach (var t in AllTypes)
            {
                sb.AppendLine($"Notification-Name: {t}");
                sb.AppendLine($"Notification-Display-Name: {_cfg.AppName} — {t}");
                sb.AppendLine("Notification-Enabled: True");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// NOTIFY message — the actual notification.
    /// </summary>
    private string BuildNotify(string title, string body, string alertType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GNTP/1.0 NOTIFY NONE");
        sb.AppendLine($"Application-Name: {_cfg.AppName}");
        sb.AppendLine($"Notification-Name: {alertType}");
        sb.AppendLine($"Notification-ID: {Guid.NewGuid()}");
        sb.AppendLine($"Notification-Title: {title}");
        sb.AppendLine($"Notification-Text: {body}");
        sb.AppendLine("Notification-Sticky: False");
        sb.AppendLine("Notification-Priority: 0");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Build the final byte payload.
    /// For REGISTER with icon: headers (UTF-8) + CRLF + icon bytes + CRLF CRLF
    /// For everything else: just the UTF-8 headers.
    /// </summary>
    private byte[] BuildPayload(string message, bool includeIcon)
    {
        var headerBytes = Encoding.UTF8.GetBytes(message);

        if (!includeIcon || _iconBytes is null)
            return headerBytes;

        // Layout: <headers>\r\n<icon bytes>\r\n\r\n
        using var ms = new MemoryStream();
        ms.Write(headerBytes);
        ms.Write(_iconBytes);
        ms.Write("\r\n\r\n"u8);
        return ms.ToArray();
    }

    private void SendAndReceive(string message)
    {
        bool includeIcon = _iconBytes is not null && message.Contains("REGISTER");
        var payload = BuildPayload(message, includeIcon);

        using var client = new TcpClient();
        client.Connect(_cfg.Host, _cfg.Port);
        client.SendTimeout    = 5000;
        client.ReceiveTimeout = 5000;

        using var stream = client.GetStream();
        stream.Write(payload, 0, payload.Length);
        stream.Flush();

        // Drain response (required by GNTP protocol)
        var buf = new byte[4096];
        try { stream.Read(buf, 0, buf.Length); } catch { }
    }

    // ── Icon loading ──────────────────────────────────────────────────────────

    private byte[]? LoadIcon()
    {
        // Search for dtop.png in the same locations as dtop.json
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "dtop.png"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dtop.png"),
            "dtop.png",
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var bytes = File.ReadAllBytes(path);
                Console.Error.WriteLine($"[Growl] Icon loaded: {path} ({bytes.Length:N0} bytes)");
                return bytes;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Growl] Could not load icon at {path}: {ex.Message}");
            }
        }

        Console.Error.WriteLine("[Growl] No dtop.png found — notifications will have no icon.");
        return null;
    }
}
