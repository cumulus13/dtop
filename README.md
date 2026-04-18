# DTOP — .NET Process Monitor

<p align="center">
  <img src="https://raw.githubusercontent.com/cumulus13/dtop/master/logo.png" alt="Logo" width="320">
</p>


A lightweight, cross-platform terminal process monitor written in **C# / .NET 8**.
Displays real-time CPU, memory, thread count, and status for all running processes
with smooth diff-based rendering, configurable color thresholds, and multi-channel
alerting via OS notifications, Growl (GNTP), and email.

```
──────────────────────────────────────────────────────────────────────────────────
  DTOP — .NET Process Monitor  Windows 11 (build 22621) [X64]
──────────────────────────────────────────────────────────────────────────────────
  Cores 8  |  RAM 16,384 MB
  Notif: ON  Growl: OFF  Email: OFF
──────────────────────────────────────────────────────────────────────────────────
  Sort: C-cpu  M-mem  I-pid  E-name  H-threads  S-status  |  A-asc  D-desc
  Toggle: N-notif  G-growl  L-email  |  P-pause  T-test  R-reload  Q-quit
──────────────────────────────────────────────────────────────────────────────────
  CPU [████████░░░░░░░░░░░░░░░░░░]  28.4%
  MEM [█████████████████░░░░░░░░░]  68.1%  (11,140 / 16,384 MB)

  PID     Process Name                 CPU %▼    Mem MB    Threads  Status
  ────────────────────────────────────────────────────────────────────────────────
     1623  webpack                      22.31%       380        6    running
     1500  java                          8.70%       512       28    running
      701  python3                      12.40%        95        2    running
      312  nginx                         1.20%        24        4    running
```

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) or later
- **Windows 10+**, **macOS**, or **Linux** (any modern distro)
- A terminal that supports ANSI escape codes and UTF-8
  - Windows: Windows Terminal (recommended), PowerShell 7, or cmd.exe on Win10+
  - macOS: Terminal.app, iTerm2
  - Linux: any VTE-based terminal (GNOME Terminal, Konsole, Alacritty, etc.)

---

## Quick Start

```bash
# Clone or extract the project
cd DotnetHtop

# Run directly (auto-restores packages)
dotnet run
```

---

## Build

### Development
```bash
dotnet build
dotnet run
```

### Self-contained release binary

Produces a single executable with no .NET runtime dependency on the target machine.

```bash
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained true -o ./out

# Windows ARM64
dotnet publish -c Release -r win-arm64 --self-contained true -o ./out

# macOS x64 (Intel)
dotnet publish -c Release -r osx-x64 --self-contained true -o ./out

# macOS ARM64 (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained true -o ./out

# Linux x64
dotnet publish -c Release -r linux-x64 --self-contained true -o ./out

# Linux ARM64 (Raspberry Pi, cloud VMs)
dotnet publish -c Release -r linux-arm64 --self-contained true -o ./out
```

Then run:
```bash
./out/dtop          # Linux / macOS
out\dtop.exe        # Windows
```

---

## Keyboard Controls

### Sorting

Press a sort key once to sort by that column descending.
Press it again to flip to ascending. The active column shows a ▼ or ▲ arrow.

| Key | Sort by |
|-----|---------|
| `C` | CPU % |
| `M` | Memory (MB) |
| `I` | PID |
| `E` | Process name |
| `H` | Thread count |
| `S` | Status |
| `A` | Force ascending |
| `D` | Force descending |
| `↑` / `↓` | Toggle sort direction |

### Actions

| Key | Action |
|-----|--------|
| `P` | Pause / resume live updates |
| `N` | Toggle all notifications on/off |
| `G` | Toggle Growl notifications on/off |
| `L` | Toggle email notifications on/off |
| `T` | Send a test notification on all enabled channels |
| `R` | Reload `dtop.json` from disk (no restart needed) |
| `Q` | Quit |

---

## Configuration

DTOP looks for `dtop.json` in these locations, in order:

1. Same directory as the executable (`AppContext.BaseDirectory/dtop.json`)
2. Home directory (`~/.dtop.json`)
3. Current working directory (`./dtop.json`)

The first file found wins. If no file is found, built-in defaults are used.

### Full reference: `dtop.json`

```jsonc
{
  // How often to refresh process data (milliseconds)
  "refreshIntervalMs": 1000,

  // Maximum number of process rows shown on screen
  "maxProcessRows": 40,

  // Alert threshold: average CPU % across all cores
  "cpuHighThreshold": 80.0,

  // Alert threshold: used memory as % of total RAM
  "memoryHighThresholdPercent": 80.0,

  // Minimum seconds between OS-level (desktop) notifications of the same type
  "notificationCooldownSeconds": 30,

  // ── Growl (GNTP) ──────────────────────────────────────────────────────────
  "growl": {
    // Set to true to enable Growl alerts
    "enabled": false,

    // Growl host — use "localhost" if Growl is on the same machine
    "host": "localhost",

    // Growl GNTP port (default 23053)
    "port": 23053,

    // Growl password (leave empty if not set in Growl preferences)
    "password": "",

    // App name shown in Growl
    "appName": "DTOP",

    // Minimum seconds between Growl alerts of the same type (CPU / Memory)
    "cooldownSeconds": 60
  },

  // ── Email (SMTP) ──────────────────────────────────────────────────────────
  "email": {
    // Set to true to enable email alerts
    "enabled": false,

    // SMTP server hostname
    "smtpHost": "smtp.gmail.com",

    // SMTP port (587 = STARTTLS, 465 = SSL, 25 = plain)
    "smtpPort": 587,

    // Use SSL/TLS
    "useSsl": true,

    // SMTP login username
    "username": "your@gmail.com",

    // SMTP password or app-specific password (Gmail: use App Passwords)
    "password": "your-app-password",

    // From address (defaults to username if blank)
    "from": "your@gmail.com",

    // Recipient address (supports a single address)
    "to": "alert@example.com",

    // Minimum seconds between emails of the same alert type
    // (prevents repeated emails during a sustained high-CPU period)
    "cooldownSeconds": 300,

    // Hard cap: maximum emails sent per hour across all alert types
    "maxPerHour": 6
  },

  // ── Color thresholds ──────────────────────────────────────────────────────
  // Each entry maps a minimum value to a hex color.
  // The highest matching threshold wins.
  // Supported colors: #FF0000 #FF8800 #FFA500 #FFFF00 #00FF00
  //                   #008000 #00FFFF #008080 #0000FF #000080 #FFFFFF

  "cpuThresholds": [
    { "threshold": 0,  "color": "#FFFFFF" },
    { "threshold": 20, "color": "#FFFF00" },
    { "threshold": 50, "color": "#FF8800" },
    { "threshold": 80, "color": "#FF0000" }
  ],

  "memoryThresholds": [
    { "threshold": 0,  "color": "#FFFFFF" },
    { "threshold": 30, "color": "#00FFFF" },
    { "threshold": 60, "color": "#FF8800" },
    { "threshold": 85, "color": "#FF0000" }
  ]
}
```

### Gmail setup

Gmail requires an **App Password** when 2FA is enabled (standard passwords are rejected).

1. Go to [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords)
2. Create a new app password (name it "DTOP")
3. Paste the 16-character password into `dtop.json` → `email.password`

### Growl setup

#### Why no Growl.Connector NuGet package?

The official `Growl.Connector` NuGet package targets **.NET Framework 4.0 only**.
It is **not compatible** with .NET 8. Adding it will produce:

```
error NU1202: Package Growl.Connector is not compatible with
net8.0 (.NETCoreApp,Version=v8.0)
```

DTOP instead uses its own **raw GNTP/1.0 TCP implementation** built into
`GrowlNotifier.cs`. No NuGet install is needed — it works out of the box.

#### Setup steps

1. Install [Growl for Windows](https://www.growlforwindows.com) or [Growl for macOS](https://growl.info)
2. Open Growl preferences → Security → confirm GNTP is enabled on port `23053`
3. Set `growl.enabled: true` in `dtop.json`
4. If you set a Growl password, add it to `growl.password`
5. Run DTOP and press `T` to send a test notification

#### Icon support

Place `dtop.png` next to the executable (same folder as `dtop.json`).
The `.csproj` copies it to the build output automatically during `dotnet build`.
The icon is read at startup, embedded as binary in the GNTP REGISTER message,
and shown in Growl for every notification type.

If `dtop.png` is not found, notifications still work — just without an icon.

---

## Notification Channels

DTOP supports three independent notification channels. Each has its own
enable flag and cooldown so they don't interfere with each other.

| Channel | Toggle key | Config section | Cooldown key |
|---------|-----------|----------------|--------------|
| OS desktop | `N` | `notificationCooldownSeconds` (top-level) | per-type |
| Growl (GNTP) | `G` | `growl` | `growl.cooldownSeconds` |
| Email (SMTP) | `L` | `email` | `email.cooldownSeconds` + `maxPerHour` |

Alert types: **CPU** (fired when average CPU across all cores exceeds `cpuHighThreshold`)
and **Memory** (fired when used RAM exceeds `memoryHighThresholdPercent`).

Each type has its own cooldown timer, so a CPU alert won't reset the memory
alert timer and vice versa.

---

## Project Structure

```
DotnetHtop/
├── DotnetHtop.csproj       Project file — .NET 8, cross-platform
├── Program.cs              Entry point, AppState, display loop, keyboard thread,
│                           sort logic, alert dispatch
├── Config.cs               JSON config model (GrowlConfig, EmailConfig,
│                           ColorMapping) + file loader
├── ProcessInfo.cs          ProcessSnapshot record, ProcessCollector:
│                           Snapshot() + Compute() with correct CPU delta math
├── Renderer.cs             Diff-based terminal renderer — builds frames into a
│                           StringBuilder, only writes changed lines per frame
├── NotificationService.cs  Orchestrates OS + Growl + Email channels
├── GrowlNotifier.cs        Raw GNTP/1.0 over TCP — no NuGet dependency
├── EmailNotifier.cs        SMTP email with per-type cooldown + hourly cap
├── SystemInfo.cs           Cross-platform RAM detection and OS summary
├── ColorConsole.cs         Fallback colored console helpers (used pre-render)
├── StringExtensions.cs     Truncate() string helper
└── dtop.json               Full example config with all options documented
```

---

## How the renderer works

Most terminal monitors flicker because they redraw the entire screen every frame
(`Console.Clear()` or writing every line). DTOP uses a **diff renderer**:

1. Each frame, all lines are built into a `(plain, ansi)` pair list.
   The `plain` string is the change-detection key (no ANSI codes).
2. Each line is compared against the previous frame's plain text.
3. **Only lines that changed** are written to stdout, using absolute cursor
   positioning (`\x1b[{row};1H`) so only those rows update.
4. The entire output for a frame is assembled into one `StringBuilder` and
   sent in **a single `Console.Write()` call** — the terminal receives the
   whole diff atomically.

In a typical idle frame, fewer than 5 lines change out of 50+, so the
terminal does almost no work — which is why it looks smooth.

---

## CPU % calculation

The original code used `elapsed / 10.0` which produces meaningless numbers.
The correct formula is:

```
cpuPercent = (cpuTimeDelta.TotalMilliseconds / (elapsedMs × coreCount)) × 100
```

- `cpuTimeDelta` — difference in `TotalProcessorTime` between two snapshots
- `elapsedMs` — actual wall-clock milliseconds between the two snapshots
  (measured with `Stopwatch`, not assumed from `Thread.Sleep`)
- `coreCount` — `Environment.ProcessorCount` (logical cores)

This gives 0–100% per process regardless of how many cores the machine has.

---

## Platform notes

| Feature | Windows | macOS | Linux |
|---------|---------|-------|-------|
| Process CPU/mem | ✅ | ✅ | ✅ |
| Total RAM detection | GCMemoryInfo → WMI | GCMemoryInfo → sysctl | GCMemoryInfo → /proc/meminfo |
| ANSI colors | Win10+ (ENABLE_VIRTUAL_TERMINAL_PROCESSING) | ✅ | ✅ |
| OS desktop notif | PowerShell toast | osascript | notify-send |
| Growl | ✅ (GNTP TCP) | ✅ (GNTP TCP) | ✅ (GNTP TCP) |
| Email | ✅ | ✅ | ✅ |

On Windows, ANSI escape processing is enabled automatically at startup via
`SetConsoleMode` with `ENABLE_VIRTUAL_TERMINAL_PROCESSING`. This works on
Windows 10 build 1607+ and Windows 11. Older Windows versions will fall back
to plain output without color.

---

## Troubleshooting

**Block characters (█ ░) show as `?` or boxes**
Your terminal is not using UTF-8. Set `chcp 65001` in cmd.exe before running,
or switch to Windows Terminal which uses UTF-8 by default.

**Colors not showing on Windows cmd.exe**
Run in Windows Terminal or PowerShell 7 instead. Legacy `cmd.exe` on older
Windows versions does not support ANSI escape codes.

**Growl test notification not arriving**
Check that Growl is running and GNTP is enabled in Growl preferences.
Check that `growl.port` matches (default 23053). If Growl is on a remote
machine, make sure the firewall allows inbound TCP on that port.

**Email not sending**
Check SMTP credentials. For Gmail, use an App Password (not your account
password). Check that `smtpPort` and `useSsl` match your provider.
Errors are written to stderr — run `dotnet run 2> errors.txt` to capture them.

**High CPU usage from DTOP itself**
Increase `refreshIntervalMs` (e.g. `2000` for 2-second refresh). The process
list collection involves two snapshots 1 second apart by default.

---

## License

MIT — do whatever you want with it.


## 👤 Author
        
[Hadi Cahyadi](mailto:cumulus13@gmail.com)
    

[![Buy Me a Coffee](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/cumulus13)

[![Donate via Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/cumulus13)
 
[Support me on Patreon](https://www.patreon.com/cumulus13)