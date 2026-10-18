using AuroraPad.Core.Processing;
using AuroraPad.Core.Protocol;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace AuroraPad.Core.Emulation;

/// <summary>Shared ViGEmBus client. Null when the driver is not installed.</summary>
public static class ViGEm
{
    private static ViGEmClient? _client;
    private static string? _error;
    private static readonly object Lock = new();

    public static ViGEmClient? Client
    {
        get
        {
            lock (Lock)
            {
                if (_client != null || _error != null) return _client;
                try { _client = new ViGEmClient(); }
                catch (Exception ex) { _error = ex.Message; }
                return _client;
            }
        }
    }

    public static bool IsAvailable => Client != null;
    public static string? Error { get { _ = Client; return _error; } }
}

/// <summary>A virtual gamepad on the ViGEm bus fed from a DualSense.</summary>
public interface IVirtualController : IDisposable
{
    string Name { get; }

    /// <summary>Raised with (large motor, small motor) when a game sends rumble.</summary>
    event Action<byte, byte>? RumbleReceived;

    /// <summary>Raised when a game sets the lightbar (DualShock 4 only).</summary>
    event Action<byte, byte, byte>? LightbarReceived;

    void Update(in PadState s, DualSenseInput raw);
}

public static class VirtualControllerFactory
{
    public static IVirtualController? TryCreate(Profiles.EmulationMode mode)
    {
        var client = ViGEm.Client;
        if (client == null) return null;
        return mode switch
        {
            Profiles.EmulationMode.Xbox360 => new Xbox360Emulator(client.CreateXbox360Controller()),
            Profiles.EmulationMode.DualShock4 => new DualShock4Emulator(client.CreateDualShock4Controller()),
            _ => null,
        };
    }
}

public sealed class Xbox360Emulator : IVirtualController
{
    private readonly IXbox360Controller _pad;
    private bool _connected;

    public string Name => "Xbox 360";
    public event Action<byte, byte>? RumbleReceived;
    public event Action<byte, byte, byte>? LightbarReceived { add { } remove { } }

    internal Xbox360Emulator(IXbox360Controller pad)
    {
        _pad = pad;
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += (_, e) => RumbleReceived?.Invoke(e.LargeMotor, e.SmallMotor);
        _pad.Connect();
        _connected = true;
    }

    public void Update(in PadState s, DualSenseInput raw)
    {
        if (!_connected) return;
        _pad.SetButtonState(Xbox360Button.A, s.Has(DualSenseButtons.Cross));
        _pad.SetButtonState(Xbox360Button.B, s.Has(DualSenseButtons.Circle));
        _pad.SetButtonState(Xbox360Button.X, s.Has(DualSenseButtons.Square));
        _pad.SetButtonState(Xbox360Button.Y, s.Has(DualSenseButtons.Triangle));
        _pad.SetButtonState(Xbox360Button.LeftShoulder, s.Has(DualSenseButtons.L1));
        _pad.SetButtonState(Xbox360Button.RightShoulder, s.Has(DualSenseButtons.R1));
        _pad.SetButtonState(Xbox360Button.Back, s.Has(DualSenseButtons.Create) || s.Has(DualSenseButtons.Touchpad));
        _pad.SetButtonState(Xbox360Button.Start, s.Has(DualSenseButtons.Options));
        _pad.SetButtonState(Xbox360Button.LeftThumb, s.Has(DualSenseButtons.L3));
        _pad.SetButtonState(Xbox360Button.RightThumb, s.Has(DualSenseButtons.R3));
        _pad.SetButtonState(Xbox360Button.Guide, s.Has(DualSenseButtons.PS));
        _pad.SetButtonState(Xbox360Button.Up, s.Up);
        _pad.SetButtonState(Xbox360Button.Down, s.Down);
        _pad.SetButtonState(Xbox360Button.Left, s.Left);
        _pad.SetButtonState(Xbox360Button.Right, s.Right);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, StickProcessing.ToXboxAxis(s.LeftX));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, StickProcessing.ToXboxAxis(s.LeftY));
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, StickProcessing.ToXboxAxis(s.RightX));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, StickProcessing.ToXboxAxis(s.RightY));
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, s.L2);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, s.R2);
        try { _pad.SubmitReport(); } catch { /* bus can disappear while running */ }
    }

    public void Dispose()
    {
        if (!_connected) return;
        _connected = false;
        try { _pad.Disconnect(); } catch { }
    }
}

public sealed class DualShock4Emulator : IVirtualController
{
    private readonly IDualShock4Controller _pad;
    private bool _connected;

    public string Name => "DualShock 4";
    public event Action<byte, byte>? RumbleReceived;
    public event Action<byte, byte, byte>? LightbarReceived;

    internal DualShock4Emulator(IDualShock4Controller pad)
    {
        _pad = pad;
        _pad.AutoSubmitReport = false;
        // Deprecated in favour of AwaitRawOutputReport (which needs raw DS4 report parsing),
        // but still delivered by ViGEmBus 1.22 and enough for rumble + lightbar.
#pragma warning disable CS0618
        _pad.FeedbackReceived += (_, e) =>
        {
            RumbleReceived?.Invoke(e.LargeMotor, e.SmallMotor);
            LightbarReceived?.Invoke(e.LightbarColor.Red, e.LightbarColor.Green, e.LightbarColor.Blue);
        };
#pragma warning restore CS0618
        _pad.Connect();
        _connected = true;
    }

    private static byte ToDs4Axis(double v, bool invert) =>
        (byte)Math.Clamp(Math.Round(128 + (invert ? -v : v) * 127.5), 0, 255);

    private bool _rawSupported = true;
    private byte _counter;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>False once the bus rejected the extended report; motion/touch are then not forwarded.</summary>
    public bool MotionForwarding => _rawSupported;

    public void Update(in PadState s, DualSenseInput raw)
    {
        if (!_connected) return;
        if (_rawSupported)
        {
            // Extended report carries gyro, accelerometer and touchpad (DS4 timestamp unit is 5.33 µs).
            var report = Ds4Protocol.BuildVirtualReport(s, raw, _counter++, (ushort)(_clock.Elapsed.TotalMilliseconds * 1000 / 5.33));
            try { _pad.SubmitRawReport(report); return; }
            catch (Exception ex) when (ex is not ObjectDisposedException) { _rawSupported = false; }
        }
        _pad.SetButtonState(DualShock4Button.Cross, s.Has(DualSenseButtons.Cross));
        _pad.SetButtonState(DualShock4Button.Circle, s.Has(DualSenseButtons.Circle));
        _pad.SetButtonState(DualShock4Button.Square, s.Has(DualSenseButtons.Square));
        _pad.SetButtonState(DualShock4Button.Triangle, s.Has(DualSenseButtons.Triangle));
        _pad.SetButtonState(DualShock4Button.ShoulderLeft, s.Has(DualSenseButtons.L1));
        _pad.SetButtonState(DualShock4Button.ShoulderRight, s.Has(DualSenseButtons.R1));
        _pad.SetButtonState(DualShock4Button.TriggerLeft, s.L2 > 0);
        _pad.SetButtonState(DualShock4Button.TriggerRight, s.R2 > 0);
        _pad.SetButtonState(DualShock4Button.Share, s.Has(DualSenseButtons.Create));
        _pad.SetButtonState(DualShock4Button.Options, s.Has(DualSenseButtons.Options));
        _pad.SetButtonState(DualShock4Button.ThumbLeft, s.Has(DualSenseButtons.L3));
        _pad.SetButtonState(DualShock4Button.ThumbRight, s.Has(DualSenseButtons.R3));
        _pad.SetButtonState(DualShock4SpecialButton.Ps, s.Has(DualSenseButtons.PS));
        _pad.SetButtonState(DualShock4SpecialButton.Touchpad, s.Has(DualSenseButtons.Touchpad));
        _pad.SetDPadDirection(DPadDirection(s));
        // DS4 axes: 0-255, Y grows downwards.
        _pad.SetAxisValue(DualShock4Axis.LeftThumbX, ToDs4Axis(s.LeftX, false));
        _pad.SetAxisValue(DualShock4Axis.LeftThumbY, ToDs4Axis(s.LeftY, true));
        _pad.SetAxisValue(DualShock4Axis.RightThumbX, ToDs4Axis(s.RightX, false));
        _pad.SetAxisValue(DualShock4Axis.RightThumbY, ToDs4Axis(s.RightY, true));
        _pad.SetSliderValue(DualShock4Slider.LeftTrigger, s.L2);
        _pad.SetSliderValue(DualShock4Slider.RightTrigger, s.R2);
        try { _pad.SubmitReport(); } catch { }
    }

    private static DualShock4DPadDirection DPadDirection(in PadState s) => (s.Up, s.Down, s.Left, s.Right) switch
    {
        (true, _, true, _) => DualShock4DPadDirection.Northwest,
        (true, _, _, true) => DualShock4DPadDirection.Northeast,
        (_, true, true, _) => DualShock4DPadDirection.Southwest,
        (_, true, _, true) => DualShock4DPadDirection.Southeast,
        (true, _, _, _) => DualShock4DPadDirection.North,
        (_, true, _, _) => DualShock4DPadDirection.South,
        (_, _, true, _) => DualShock4DPadDirection.West,
        (_, _, _, true) => DualShock4DPadDirection.East,
        _ => DualShock4DPadDirection.None,
    };

    public void Dispose()
    {
        if (!_connected) return;
        _connected = false;
        try { _pad.Disconnect(); } catch { }
    }
}
