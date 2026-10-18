using CommunityToolkit.Mvvm.ComponentModel;
using AuroraPad.App.Localization;
using AuroraPad.Core;
using AuroraPad.Core.Devices;
using AuroraPad.Core.Protocol;

namespace AuroraPad.App.ViewModels;

/// <summary>One connected controller as shown in the UI; refreshed by a UI timer.</summary>
public sealed partial class ControllerViewModel : ObservableObject
{
    public ControllerSession Session { get; }

    public ControllerViewModel(ControllerSession session) => Session = session;

    public string Name => Session.Device.Model switch { ControllerModel.DualSenseEdge => "DualSense Edge", ControllerModel.DualShock4 => "DualShock 4", _ => "DualSense" };
    public string ConnectionKind => Session.Device.Connection == ConnectionType.Bluetooth ? "Bluetooth" : "USB";
    public string ConnectionLabel => L.F("ctl.connection", ConnectionKind);
    public string Title => $"#{Session.Index + 1}  {Name}";

    [ObservableProperty] private int _battery;
    [ObservableProperty] private string _batteryLabel = "";
    [ObservableProperty] private string _emulationLabel = "";
    [ObservableProperty] private string _hapticsLabel = "";

    // Live input
    [ObservableProperty] private double _leftX, _leftY, _rightX, _rightY;
    [ObservableProperty] private int _l2, _r2;
    [ObservableProperty] private bool _cross, _circle, _square, _triangle;
    [ObservableProperty] private bool _l1, _r1, _l3, _r3;
    [ObservableProperty] private bool _up, _down, _left, _right;
    [ObservableProperty] private bool _create, _options, _ps, _touchpad, _mute;
    [ObservableProperty] private string _gyro = "", _accel = "";
    [ObservableProperty] private bool _touch1Active, _touch2Active;
    [ObservableProperty] private double _touch1X, _touch1Y, _touch2X, _touch2Y;

    // ---- Controller illustration (DualSenseView, 600x400 canvas) ----------
    [ObservableProperty] private double _lCapLeft, _lCapTop, _rCapLeft, _rCapTop;
    [ObservableProperty] private double _l2Height, _r2Height;
    [ObservableProperty] private double _t1Left, _t1Top, _t2Left, _t2Top;
    [ObservableProperty] private bool _led1On, _led2On, _led3On, _led4On, _led5On, _micOn;
    [ObservableProperty] private System.Windows.Media.Brush _lightbarBrush = System.Windows.Media.Brushes.Black;
    [ObservableProperty] private System.Windows.Media.Color _lightbarColor;
    public bool IsDualShock4 => Session.Device.IsDualShock4;
    public bool IsDualSense => !Session.Device.IsDualShock4;

    private void RefreshIllustration(DualSenseInput i)
    {
        const double travel = 14, cap = 21;
        LCapLeft = 228 + (i.LeftX - 127.5) / 127.5 * travel - cap;
        LCapTop = 212 + (i.LeftY - 127.5) / 127.5 * travel - cap;
        RCapLeft = 372 + (i.RightX - 127.5) / 127.5 * travel - cap;
        RCapTop = 212 + (i.RightY - 127.5) / 127.5 * travel - cap;
        L2Height = i.L2 / 255.0 * 30;
        R2Height = i.R2 / 255.0 * 30;
        T1Left = 215 + i.Touch1.X / 1920.0 * 170 - 6; T1Top = 56 + i.Touch1.Y / 1080.0 * 96 - 6;
        T2Left = 215 + i.Touch2.X / 1920.0 * 170 - 6; T2Top = 56 + i.Touch2.Y / 1080.0 * 96 - 6;

        var o = Session.Device.Output;
        var c = System.Windows.Media.Color.FromRgb(o.LightbarR, o.LightbarG, o.LightbarB);
        if (c != LightbarColor)
        {
            LightbarColor = c;
            var b = new System.Windows.Media.SolidColorBrush(c);
            b.Freeze();
            LightbarBrush = b;
        }
        byte leds = IsDualSense ? o.PlayerLeds : (byte)0;
        Led1On = (leds & 1) != 0; Led2On = (leds & 2) != 0; Led3On = (leds & 4) != 0; Led4On = (leds & 8) != 0; Led5On = (leds & 16) != 0;
        MicOn = IsDualSense && o.MicLed != MicLedMode.Off;
    }

    public void Refresh()
    {
        var i = Session.Device.Input;
        RefreshIllustration(i);
        Battery = i.BatteryPercent;
        BatteryLabel = i.Charging switch
        {
            ChargingState.Charging => L.F("ctl.charging", i.BatteryPercent),
            ChargingState.Full => L.T("ctl.full"),
            _ => $"{i.BatteryPercent}%",
        };
        EmulationLabel = Session.EmulationPaused ? L.T("ctl.emuPaused")
            : Session.IsEmulating ? L.F("ctl.emulating", Session.EmulatorName)
            : Session.EmulationError != null ? L.F("ctl.emuError", Session.EmulationError)
            : L.T("ctl.noEmu");
        if (!Session.MappingEnabled) EmulationLabel += "  •  " + L.T("ctl.kmPaused");
        HapticsLabel = Session.HapticsDevice != null ? L.F("ctl.haptics", Session.HapticsDevice)
            : Session.HapticsError != null ? L.F("ctl.hapticsError", Session.HapticsError) : "";

        // Stick dots move inside a 100x100 box.
        LeftX = i.LeftX / 255.0 * 100; LeftY = i.LeftY / 255.0 * 100;
        RightX = i.RightX / 255.0 * 100; RightY = i.RightY / 255.0 * 100;
        L2 = i.L2; R2 = i.R2;
        Cross = i.IsPressed(DualSenseButtons.Cross); Circle = i.IsPressed(DualSenseButtons.Circle);
        Square = i.IsPressed(DualSenseButtons.Square); Triangle = i.IsPressed(DualSenseButtons.Triangle);
        L1 = i.IsPressed(DualSenseButtons.L1); R1 = i.IsPressed(DualSenseButtons.R1);
        L3 = i.IsPressed(DualSenseButtons.L3); R3 = i.IsPressed(DualSenseButtons.R3);
        Up = i.DPadUp; Down = i.DPadDown; Left = i.DPadLeft; Right = i.DPadRight;
        Create = i.IsPressed(DualSenseButtons.Create); Options = i.IsPressed(DualSenseButtons.Options);
        Ps = i.IsPressed(DualSenseButtons.PS); Touchpad = i.IsPressed(DualSenseButtons.Touchpad);
        Mute = i.IsPressed(DualSenseButtons.Mute);
        Gyro = $"X {i.GyroX,6}   Y {i.GyroY,6}   Z {i.GyroZ,6}";
        Accel = $"X {i.AccelX,6}   Y {i.AccelY,6}   Z {i.AccelZ,6}";
        // Touchpad is 1920x1080; the preview canvas is 240x120.
        Touch1Active = i.Touch1.Active; Touch1X = i.Touch1.X / 1920.0 * 240; Touch1Y = i.Touch1.Y / 1080.0 * 120;
        Touch2Active = i.Touch2.Active; Touch2X = i.Touch2.X / 1920.0 * 240; Touch2Y = i.Touch2.Y / 1080.0 * 120;
    }
}
