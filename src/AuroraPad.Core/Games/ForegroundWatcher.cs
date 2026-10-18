using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AuroraPad.Core.Games;

/// <summary>Polls the foreground window and reports when its executable changes.</summary>
public sealed class ForegroundWatcher : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly Timer _timer;
    private readonly int _ownPid = Environment.ProcessId;
    private string? _current;

    /// <summary>Raised with the lower-case executable name, e.g. "eldenring.exe".</summary>
    public event Action<string>? ForegroundChanged;

    public string? Current => _current;

    public ForegroundWatcher(TimeSpan interval) =>
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, interval);

    public static string? GetForegroundExecutable(int ignorePid = -1)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == ignorePid) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return (p.ProcessName + ".exe").ToLowerInvariant();
        }
        catch { return null; }
    }

    private void Poll()
    {
        // Focusing our own window keeps the current game profile.
        var exe = GetForegroundExecutable(_ownPid);
        if (exe == null || exe == _current) return;
        _current = exe;
        ForegroundChanged?.Invoke(exe);
    }

    public void Dispose() => _timer.Dispose();
}
