using AuroraPad.Core.Protocol;
using HidSharp;

namespace AuroraPad.Core.Devices;

public enum ControllerModel { DualSense, DualSenseEdge, DualShock4 }

public enum ConnectionType { Usb, Bluetooth }

/// <summary>One physical Sony controller (DualSense, DualSense Edge or DualShock 4): runs a read loop and pushes output reports.</summary>
public sealed class DualSenseDevice : IDisposable
{
    public const int SonyVid = 0x054C;
    public const int DualSensePid = 0x0CE6;
    public const int DualSenseEdgePid = 0x0DF2;
    public const int DualShock4V1Pid = 0x05C4;
    public const int DualShock4V2Pid = 0x09CC;
    public const int DualShock4DonglePid = 0x0BA0;

    public static bool IsSupportedPid(int pid) =>
        pid is DualSensePid or DualSenseEdgePid or DualShock4V1Pid or DualShock4V2Pid or DualShock4DonglePid;

    public bool IsDualShock4 => Model == ControllerModel.DualShock4;

    private readonly HidDevice _hid;
    private readonly HidStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeLock = new();
    private readonly Thread _readThread;
    private byte _btSequence;
    private bool _bootLedsReleased;
    private volatile bool _outputDirty = true;
    private readonly Timer _outputTimer;

    public string DevicePath => _hid.DevicePath;
    public string Serial { get; }
    public ControllerModel Model { get; }
    public ConnectionType Connection { get; }
    public OutputState Output { get; } = new();
    public DualSenseInput Input { get; private set; } = DualSenseInput.Neutral;
    public bool IsConnected { get; private set; } = true;

    /// <summary>Raised on the read thread for every decoded report.</summary>
    public event Action<DualSenseDevice, DualSenseInput>? InputReceived;
    public event Action<DualSenseDevice>? Disconnected;

    private DualSenseDevice(HidDevice hid, HidStream stream, ConnectionType connection)
    {
        _hid = hid;
        _stream = stream;
        Connection = connection;
        Model = hid.ProductID switch { DualSenseEdgePid => ControllerModel.DualSenseEdge, DualSensePid => ControllerModel.DualSense, _ => ControllerModel.DualShock4 };
        Serial = TryGetSerial(hid);

        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = $"{Model} read {Serial}" };
        // Periodic refresh keeps state applied even if the controller drops a report.
        _outputTimer = new Timer(_ => FlushOutput(force: false), null, 0, 16);
    }

    public static DualSenseDevice? TryOpen(HidDevice hid)
    {
        if (hid.VendorID != SonyVid || !IsSupportedPid(hid.ProductID))
            return null;
        // Skip virtual pads (ViGEm DS4, DSX's VirtualPad) so we never read our own output back.
        if (DeviceTree.IsVirtual(hid.DevicePath)) return null;

        int inputLen;
        try { inputLen = hid.GetMaxInputReportLength(); } catch { return null; }
        // The DualSense exposes several HID collections; only the gamepad one has input reports.
        if (inputLen < 10) return null;

        var config = new OpenConfiguration();
        config.SetOption(OpenOption.Exclusive, false);
        config.SetOption(OpenOption.Interruptible, true);
        if (!hid.TryOpen(config, out HidStream stream)) return null;
        stream.ReadTimeout = Timeout.Infinite;

        var connection = inputLen >= 78 ? ConnectionType.Bluetooth : ConnectionType.Usb;
        if (connection == ConnectionType.Bluetooth)
        {
            // Reading the calibration feature report switches BT controllers to full reports
            // (0x31 on DualSense via report 0x05, 0x11 on DualShock 4 via report 0x02).
            try
            {
                var feature = new byte[hid.GetMaxFeatureReportLength()];
                feature[0] = hid.ProductID is DualSensePid or DualSenseEdgePid ? (byte)0x05 : (byte)0x02;
                stream.GetFeature(feature);
            }
            catch { /* controller still works in simple mode */ }
        }

        var dev = new DualSenseDevice(hid, stream, connection);
        dev._readThread.Start();
        return dev;
    }

    private static string TryGetSerial(HidDevice hid)
    {
        try
        {
            var s = hid.GetSerialNumber();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        catch { }
        // string.GetHashCode is randomised per process; use FNV-1a so the id is stable across runs.
        uint h = 2166136261;
        foreach (char c in hid.DevicePath.ToUpperInvariant()) h = (h ^ c) * 16777619;
        return h.ToString("X8");
    }

    private void ReadLoop()
    {
        var buffer = new byte[_hid.GetMaxInputReportLength()];
        bool bt = Connection == ConnectionType.Bluetooth;
        while (!_cts.IsCancellationRequested)
        {
            int n;
            try { n = _stream.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { continue; }
            catch { break; }
            if (n <= 0) continue;

            var input = IsDualShock4 ? Ds4Protocol.ParseInput(buffer.AsSpan(0, n), bt) : InputParser.Parse(buffer.AsSpan(0, n), bt);
            if (input == null) continue;
            Input = input;
            try { InputReceived?.Invoke(this, input); } catch { /* listener errors must not kill the loop */ }
        }
        IsConnected = false;
        Disconnected?.Invoke(this);
    }

    /// <summary>Current 47-byte state block (lightbar, LEDs, triggers, rumble, audio path) for report 0x36.</summary>
    public byte[] BuildStateBlock()
    {
        var block = new byte[47];
        OutputReportBuilder.WriteCommon(Output, block, releaseBootLeds: false, bluetooth: true);
        return block;
    }

    /// <summary>Sends a raw output report (e.g. Bluetooth haptics 0x32), serialized with the state reports.</summary>
    public void WriteReport(byte[] report)
    {
        if (!IsConnected) return;
        lock (_writeLock) _stream.Write(report);
    }

    /// <summary>Mark output as changed; it is sent on the next tick.</summary>
    public void MarkOutputDirty() => _outputDirty = true;

    public void FlushOutput(bool force)
    {
        if (!IsConnected || (!force && !_outputDirty)) return;
        lock (_writeLock)
        {
            _outputDirty = false;
            var report = IsDualShock4
                ? Ds4Protocol.BuildOutput(Output, Connection == ConnectionType.Bluetooth)
                : OutputReportBuilder.Build(Output, Connection == ConnectionType.Bluetooth, ref _btSequence, releaseBootLeds: !_bootLedsReleased);
            try
            {
                _stream.Write(report);
                _bootLedsReleased = true;
            }
            catch
            {
                _outputDirty = true;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _outputTimer.Dispose();
        try
        {
            // Leave the controller in a neutral state.
            Output.RumbleLeft = Output.RumbleRight = 0;
            Output.LeftTrigger = Triggers.TriggerEffect.Off();
            Output.RightTrigger = Triggers.TriggerEffect.Off();
            FlushOutput(force: true);
        }
        catch { }
        try { _stream.Dispose(); } catch { }
        IsConnected = false;
    }
}
