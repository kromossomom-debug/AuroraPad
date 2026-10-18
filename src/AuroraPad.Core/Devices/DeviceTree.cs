using System.Runtime.InteropServices;
using System.Text;

namespace AuroraPad.Core.Devices;

/// <summary>
/// Walks the PnP device tree (cfgmgr32) to tell physical controllers from virtual ones
/// created by ViGEmBus or other Nefarius emulation buses. Without this, our own virtual
/// DualShock 4 would be picked up again as an input controller.
/// </summary>
public static class DeviceTree
{
    private const int CrSuccess = 0;
    private const int CmDrpHardwareId = 0x00000002;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, int flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int length, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, int property, out int regType,
        byte[]? buffer, ref int length, int flags);

    private static readonly string[] VirtualMarkers = ["VIGEM", "NEFARIUS", "VIRTUALPAD", "HIDGUARDIAN"];

    /// <summary>True when the HID device or any ancestor belongs to a known virtual gamepad bus.</summary>
    public static bool IsVirtual(string devicePath)
    {
        try
        {
            var instanceId = HidHideManager.ToInstanceId(devicePath);
            if (CM_Locate_DevNodeW(out uint node, instanceId, 0) != CrSuccess) return false;
            for (int depth = 0; depth < 8; depth++)
            {
                foreach (var id in Identifiers(node))
                    if (VirtualMarkers.Any(m => id.Contains(m, StringComparison.OrdinalIgnoreCase)))
                        return true;
                if (CM_Get_Parent(out uint parent, node, 0) != CrSuccess) break;
                node = parent;
            }
        }
        catch { /* on failure treat as physical */ }
        return false;
    }

    /// <summary>Instance ids from the device up to the root, for diagnostics.</summary>
    public static List<string> Ancestors(string devicePath)
    {
        var list = new List<string>();
        if (CM_Locate_DevNodeW(out uint node, HidHideManager.ToInstanceId(devicePath), 0) != CrSuccess) return list;
        for (int depth = 0; depth < 8; depth++)
        {
            list.Add(string.Join(" | ", Identifiers(node)));
            if (CM_Get_Parent(out uint parent, node, 0) != CrSuccess) break;
            node = parent;
        }
        return list;
    }

    private static IEnumerable<string> Identifiers(uint node)
    {
        var sb = new StringBuilder(512);
        if (CM_Get_Device_IDW(node, sb, sb.Capacity, 0) == CrSuccess) yield return sb.ToString();

        int len = 0;
        CM_Get_DevNode_Registry_PropertyW(node, CmDrpHardwareId, out _, null, ref len, 0);
        if (len <= 0) yield break;
        var buf = new byte[len];
        if (CM_Get_DevNode_Registry_PropertyW(node, CmDrpHardwareId, out _, buf, ref len, 0) != CrSuccess) yield break;
        foreach (var hw in Encoding.Unicode.GetString(buf, 0, len).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            yield return hw;
    }
}
