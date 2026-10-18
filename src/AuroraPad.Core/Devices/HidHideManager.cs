using Nefarius.Drivers.HidHide;

namespace AuroraPad.Core.Devices;

/// <summary>
/// Hides physical controllers from games through the HidHide driver (if installed), so a game
/// only sees the virtual pad. This app's executable is whitelisted so it can still read them.
/// </summary>
public sealed class HidHideManager
{
    private readonly HidHideControlService? _service;
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    public bool IsInstalled { get; }
    public string? LastError { get; private set; }
    public IReadOnlyCollection<string> HiddenIds => _hidden;

    public HidHideManager()
    {
        try
        {
            _service = new HidHideControlService();
            IsInstalled = _service.IsInstalled;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsInstalled = false;
        }
    }

    /// <summary>
    /// Converts a HID device path (\\?\hid#vid_054c&amp;pid_0ce6&amp;...#{guid}) to the
    /// device instance id HidHide expects (HID\VID_054C&amp;PID_0CE6&amp;...).
    /// </summary>
    public static string ToInstanceId(string devicePath)
    {
        var s = devicePath;
        if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) s = s[4..];
        int guid = s.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0) s = s[..guid];
        return s.Replace('#', '\\').ToUpperInvariant();
    }

    public bool Hide(string devicePath, string appPath)
    {
        if (!IsInstalled || _service == null) return false;
        var id = ToInstanceId(devicePath);
        try
        {
            if (!_service.ApplicationPaths.Contains(appPath, StringComparer.OrdinalIgnoreCase))
                _service.AddApplicationPath(appPath);
            if (!_service.BlockedInstanceIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                _service.AddBlockedInstanceId(id);
            if (!_service.IsActive) _service.IsActive = true;
            _hidden.Add(id);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>Removes the given instance ids (or everything this app hid) from the block list.</summary>
    public void Unhide(IEnumerable<string>? ids = null)
    {
        if (!IsInstalled || _service == null) return;
        foreach (var id in (ids ?? _hidden).ToList())
        {
            try { _service.RemoveBlockedInstanceId(id); } catch (Exception ex) { LastError = ex.Message; }
            _hidden.Remove(id);
        }
    }
}
