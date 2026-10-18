using AuroraPad.Core;
using AuroraPad.Core.Devices;
using AuroraPad.Core.Profiles;
using AuroraPad.Core.Protocol;
using AuroraPad.Core.Triggers;
using AuroraPad.Core.Udp;

// "dsu [port]": act as an emulator against a running AuroraPad DSU server.
if (args.Length > 0 && args[0] == "dsu")
    return DsuClientTest.Run(args.Length > 1 ? int.Parse(args[1]) : 26760);

// "hidhide": check the device path → instance id conversion.
if (args.Length > 0 && args[0] == "hidhide")
{
    foreach (var hid in HidSharp.DeviceList.Local.GetHidDevices(DualSenseDevice.SonyVid))
        Console.WriteLine($"{hid.DevicePath}\n  -> {HidHideManager.ToInstanceId(hid.DevicePath)}");
    Console.WriteLine($"HidHide installed: {new HidHideManager().IsInstalled}");
    return 0;
}

// "tree": virtual-device filter on every Sony HID device.
if (args.Length > 0 && args[0] == "tree")
{
    foreach (var hid in HidSharp.DeviceList.Local.GetHidDevices(DualSenseDevice.SonyVid))
    {
        Console.WriteLine($"{hid.ProductID:X4} virtual={DeviceTree.IsVirtual(hid.DevicePath)}");
        foreach (var a in DeviceTree.Ancestors(hid.DevicePath)) Console.WriteLine("   " + a);
    }
    return 0;
}

// "library": scan installed games.
if (args.Length > 0 && args[0] == "library")
{
    var games = AuroraPad.Core.Games.GameLibrary.Scan();
    foreach (var g in games) Console.WriteLine($"[{g.StoreLabel}] {g.Name} -> {g.Executable}");
    Console.WriteLine($"total {games.Count}");
    return 0;
}

// "macro": parser checks (no input is sent).
if (args.Length > 0 && args[0] == "macro")
{
    foreach (var text in new[] { "ctrl+c", "w hold 300, wait 50, space", "lclick, wait 30, lclick", "ctrl+banana", "wait abc" })
    {
        bool ok = AuroraPad.Core.Mapping.Macro.TryParse(text, out var m, out var err);
        Console.WriteLine($"\"{text}\" -> {(ok ? $"{m!.Steps.Count} passos: " + string.Join(" | ", m.Steps) : "erro: " + err)}");
    }
    return 0;
}

// "osc": send one controller frame of OSC to a local listener and decode it.
if (args.Length > 0 && args[0] == "osc")
{
    using var listener = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 9123));
    listener.Client.ReceiveTimeout = 2000;
    using var mgr = new DeviceManager();
    mgr.Start();
    Thread.Sleep(500);
    using var s = new ControllerSession(mgr.Devices[0], new Profile { Emulation = EmulationMode.None });
    using var osc = new AuroraPad.Core.Osc.OscSender();
    osc.Attach(s);
    osc.Start("127.0.0.1", 9123);
    var seen = new SortedSet<string>();
    var until = DateTime.UtcNow.AddSeconds(1);
    while (DateTime.UtcNow < until)
    {
        var ep = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
        var msg = listener.Receive(ref ep);
        int end = Array.IndexOf(msg, (byte)0);
        seen.Add(System.Text.Encoding.ASCII.GetString(msg, 0, end));
    }
    osc.Stop();
    Console.WriteLine($"OSC messages sent: {osc.MessagesSent}; addresses: {string.Join(" ", seen)}");
    return 0;
}

// "haptics": one second of rumble → haptics through the DualSense sound card.
if (args.Length > 0 && args[0] == "haptics")
{
    var engine = AuroraPad.Core.Haptics.HapticsEngine.TryCreate(0, out var err);
    if (engine == null)
    {
        Console.WriteLine("haptics unavailable: " + err);
        Console.WriteLine(AuroraPad.Core.Haptics.HapticsEngine.LastException);
        using var en = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        foreach (var d in en.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
            Console.WriteLine($"  render: {d.FriendlyName}");
        return 1;
    }
    Console.WriteLine("haptics device: " + engine.DeviceName + " / " + engine.MixFormatDescription);
    engine.Configure(AuroraPad.Core.Haptics.HapticsMode.RumbleToHaptics, 1.0, 200);
    engine.SetRumble(220, 160);
    Thread.Sleep(1000);
    engine.SetRumble(0, 0);
    Thread.Sleep(200);
    engine.Dispose();
    Console.WriteLine("haptics done");
    return 0;
}

// Hardware smoke test: reads input, then drives lightbar, player LEDs, a trigger effect and rumble.
using var manager = new DeviceManager();
manager.Start();
Thread.Sleep(500);

var devices = manager.Devices;
Console.WriteLine($"Controllers: {devices.Count}");
if (devices.Count == 0) return 1;

var dev = devices[0];
Console.WriteLine($"  {dev.Model} via {dev.Connection}, serial {dev.Serial}");

int reports = 0;
dev.InputReceived += (_, _) => Interlocked.Increment(ref reports);
Thread.Sleep(1000);
var i = dev.Input;
Console.WriteLine($"Reports/s: {reports}");
Console.WriteLine($"LS=({i.LeftX},{i.LeftY}) RS=({i.RightX},{i.RightY}) L2={i.L2} R2={i.R2} DPad={i.DPad} Buttons={i.Buttons}");
Console.WriteLine($"Gyro=({i.GyroX},{i.GyroY},{i.GyroZ}) Accel=({i.AccelX},{i.AccelY},{i.AccelZ}) Battery={i.BatteryPercent}% {i.Charging}");
Console.WriteLine($"Touch1={i.Touch1} Touch2={i.Touch2}");

var profile = new Profile
{
    LightbarR = 255, LightbarG = 0, LightbarB = 0,
    PlayerLeds = [false, true, false, true, false],
    RightTrigger = new TriggerSettings { Mode = TriggerMode.Resistance, Start = 0, Strength = 8 },
    LeftTrigger = new TriggerSettings { Mode = TriggerMode.Vibration, Start = 0, Strength = 8, Frequency = 30 },
    Emulation = EmulationMode.None,
};
using var session = new ControllerSession(dev, profile);
byte seq = 0;
Console.WriteLine("Output report: " + Convert.ToHexString(OutputReportBuilder.Build(dev.Output, dev.Connection == ConnectionType.Bluetooth, ref seq, false)));
Console.WriteLine("Lightbar RED, player LEDs 2+4, R2 rigid, L2 vibrating, rumble 1s...");
session.SetTestRumble(200, 200);
Thread.Sleep(1000);
session.SetTestRumble(0, 0);
Thread.Sleep(int.TryParse(Environment.GetEnvironmentVariable("PROBE_HOLD_MS"), out var hold) ? hold : 4000);

// UDP mod packet, parsed locally: lightbar green.
var udp = new UdpModServer(() => [session]);
udp.Handle("""{"instructions":[{"type":2,"parameters":[0,0,255,0]}]}""");
Console.WriteLine($"After UDP RGB: {dev.Output.LightbarR},{dev.Output.LightbarG},{dev.Output.LightbarB}");
Console.WriteLine("Response: " + udp.BuildResponse());
Thread.Sleep(1500);
return 0;
