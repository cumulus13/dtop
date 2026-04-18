// File: ConfigWatcher.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

namespace DotnetHtop;

/// <summary>
/// Watches dtop.json for file changes using OS kernel notifications.
///
/// Resource cost: ZERO CPU when idle.
/// - Windows: ReadDirectoryChangesW (kernel callback, no polling)
/// - Linux:   inotify              (kernel callback, no polling)
/// - macOS:   FSEvents             (kernel callback, no polling)
///
/// A single Timer fires once 500ms after the last change event (debounce).
/// The timer is reused — not recreated — on each event, so there is never
/// more than one pending timer alive at a time.
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    private readonly AppState          _state;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer             _debounce;   // reused, never recreated
    private readonly object            _lock = new();
    private const    int               DebounceMs = 500;

    private DateTime _lastWriteTime = DateTime.MinValue;
    private bool     _disposed      = false;

    public ConfigWatcher(AppState state)
    {
        _state = state;

        // Create the debounce timer once — stopped (Timeout.Infinite).
        // OnChanged arms it; Reload() runs when it fires.
        _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);

        var path = ResolveWatchPath(state.Config.LoadedFrom);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var file = Path.GetFileName(path);

        _watcher = new FileSystemWatcher(dir, file)
        {
            // Only the events we care about — keeps kernel notification list small
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,

            // 8KB internal buffer — more than enough for a single-file watch.
            // Default is 8KB anyway, but being explicit prevents accidental
            // global changes from affecting this watcher.
            InternalBufferSize = 8192,

            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged; // file created fresh while app is running
        _watcher.Renamed += OnChanged; // atomic-save editors (vim, VS Code, Notepad++)
        // Error handler so a watcher failure doesn't crash the app silently
        _watcher.Error   += OnWatcherError;
    }

    // ── Event handler — runs on OS thread pool, must be fast ─────────────────

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Arm (or re-arm) the debounce timer.
        // Change() on an existing Timer is thread-safe and allocation-free —
        // no new object is created, the existing OS timer is just reset.
        lock (_lock)
        {
            if (!_disposed)
                _debounce.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // FileSystemWatcher can fail if the watched directory is deleted or
        // the buffer overflows. Log and attempt to re-enable.
        Console.Error.WriteLine($"[ConfigWatcher] Watcher error: {e.GetException().Message}");
        try
        {
            lock (_lock)
            {
                if (!_disposed)
                    _watcher.EnableRaisingEvents = true;
            }
        }
        catch { /* best-effort recovery */ }
    }

    // ── Reload — runs on thread pool 500ms after last change ─────────────────

    private void Reload()
    {
        if (_disposed) return;

        try
        {
            var path = _state.Config.LoadedFrom;

            // Fall back to cwd if the path is a placeholder
            if (!File.Exists(path))
                path = ResolveWatchPath(path);
            if (!File.Exists(path)) return;

            // Guard against duplicate events for the same write
            var wt = File.GetLastWriteTime(path);
            if (wt == _lastWriteTime) return;
            _lastWriteTime = wt;

            var newConfig = Config.Load();

            // All assignments below are either reference swaps (atomic on 64-bit)
            // or bool writes (always atomic). The render loop reads these fields
            // but never holds a lock while doing so — worst case it renders with
            // the old config for one frame, which is harmless.
            _state.Config       = newConfig;
            _state.GrowlEnabled = newConfig.Growl.Enabled;
            _state.EmailEnabled = newConfig.Email.Enabled;
            _state.Notifications?.Reload(newConfig);

            Renderer.Invalidate();

            _state.LastReloadTime   = DateTime.Now;
            _state.LastReloadSource = "auto";
        }
        catch (IOException)
        {
            // File still being written — the watcher will fire again shortly
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigWatcher] Reload failed: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ResolveWatchPath(string loadedFrom)
    {
        // If we loaded from a real file, watch that exact file.
        // Otherwise default to ./dtop.json so creating the file picks it up.
        if (!string.IsNullOrWhiteSpace(loadedFrom) && File.Exists(loadedFrom))
            return loadedFrom;
        return Path.Combine(Directory.GetCurrentDirectory(), "dtop.json");
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        lock (_lock) { _disposed = true; }
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
