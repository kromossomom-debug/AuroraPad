using HidSharp;

namespace AuroraPad.Core.Devices;

/// <summary>Finds connected DualSense controllers and tracks hot-plug events.</summary>
public sealed class DeviceManager : IDisposable
{
    private readonly object _lock = new();
    private readonly List<DualSenseDevice> _devices = [];
    private bool _disposed;

    public event Action<DualSenseDevice>? DeviceConnected;
    public event Action<DualSenseDevice>? DeviceDisconnected;

    public IReadOnlyList<DualSenseDevice> Devices
    {
        get { lock (_lock) return _devices.ToList(); }
    }

    public void Start()
    {
        DeviceList.Local.Changed += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        if (_disposed) return;
        var found = DeviceList.Local.GetHidDevices(DualSenseDevice.SonyVid)
            .Where(d => DualSenseDevice.IsSupportedPid(d.ProductID))
            .ToList();

        var added = new List<DualSenseDevice>();
        lock (_lock)
        {
            foreach (var hid in found)
            {
                if (_devices.Any(d => d.DevicePath == hid.DevicePath)) continue;
                var dev = DualSenseDevice.TryOpen(hid);
                if (dev == null) continue;
                // Same controller over USB and BT at once: keep the first one.
                if (_devices.Any(d => d.Serial == dev.Serial)) { dev.Dispose(); continue; }
                dev.Disconnected += OnDisconnected;
                _devices.Add(dev);
                added.Add(dev);
            }
        }
        foreach (var d in added) DeviceConnected?.Invoke(d);
    }

    private void OnDisconnected(DualSenseDevice dev)
    {
        bool removed;
        lock (_lock) removed = _devices.Remove(dev);
        if (removed)
        {
            dev.Dispose();
            DeviceDisconnected?.Invoke(dev);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        List<DualSenseDevice> copy;
        lock (_lock) { copy = _devices.ToList(); _devices.Clear(); }
        foreach (var d in copy) d.Dispose();
    }
}
