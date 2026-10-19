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

// "latency [none|xbox|ds4]": report rate, handler cost and how stale processed reports are.
if (args.Length > 0 && args[0] == "latency")
{
    var mode = args.Length > 1 ? args[1] switch { "xbox" => EmulationMode.Xbox360, "ds4" => EmulationMode.DualShock4, _ => EmulationMode.None } : EmulationMode.None;
    using var mgr = new DeviceManager();
    mgr.Start();
    Thread.Sleep(500);
    var pad = mgr.Devices[0];
    var prof = args.Length > 2 && args[2] == "mine" ? new ProfileStore().LoadProfiles().First(p => p.Name == new ProfileStore().LoadSettings().ActiveProfile) : new Profile { Emulation = mode };
    if (args.Length > 2 && args[2] == "mine") Console.WriteLine($"profile {prof.Name}: emu={prof.Emulation} haptics={prof.HapticsMode} touchpad={prof.Touchpad.Enabled} mouse={prof.Mouse.Source} mappings={prof.Mappings.Count}");
    using var s = new ControllerSession(pad, prof);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    int count = 0; double total = 0, max = 0; uint firstTs = 0; double firstWall = 0; double worstLag = 0;
    pad.InputReceived += (_, input) =>
    {
        // Sensor timestamp ticks are ~0.33 µs on DualSense; compare device time vs wall time to see backlog growth.
        double wall = sw.Elapsed.TotalSeconds;
        if (count == 0) { firstTs = input.SensorTimestamp; firstWall = wall; }
        double devElapsed = (input.SensorTimestamp - firstTs) / 3_000_000.0;
        worstLag = Math.Max(worstLag, (wall - firstWall) - devElapsed);
        count++;
    };
    // Measure the session's own handler cost by timing a parallel subscriber placed after it.
    double last = 0;
    pad.InputReceived += (_, _) => { double now = sw.Elapsed.TotalMilliseconds; if (last > 0) { var d = now - last; total += d; max = Math.Max(max, d); } last = now; };
    Thread.Sleep(5000);
    Console.WriteLine($"mode={mode} reports={count} rate={count / 5.0:F0}/s avgInterval={total / Math.Max(1, count - 1):F2}ms maxInterval={max:F1}ms backlogGrowth={worstLag * 1000:F0}ms");
    return 0;
}

// "inputlog [seconds]": read-only dump of what the controller sends (rate, report type, touch events).
if (args.Length > 0 && args[0] == "inputlog")
{
    int secs = args.Length > 1 ? int.Parse(args[1]) : 5;
    using var mgr = new DeviceManager();
    mgr.Start();
    Thread.Sleep(500);
    if (mgr.Devices.Count == 0) { Console.WriteLine("no controller"); return 1; }
    var ctl = mgr.Devices[0];
    Console.WriteLine($"{ctl.Model} via {ctl.Connection}");
    int n = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
    bool lastT = false; DualSenseInput? last = null;
    var events = new List<string>();
    ctl.InputReceived += (_, i) =>
    {
        n++; last = i;
        if (i.Touch1.Active != lastT)
        {
            lock (events) events.Add($"{sw.Elapsed.TotalMilliseconds,7:F0}ms touch {(i.Touch1.Active ? "DOWN" : "up  ")} at ({i.Touch1.X},{i.Touch1.Y}) id={i.Touch1.Id} click={i.IsPressed(DualSenseButtons.Touchpad)}");
            lastT = i.Touch1.Active;
        }
    };
    Thread.Sleep(secs * 1000);
    var l = last!;
    Console.WriteLine($"reports/s: {n / (double)secs:F0}");
    Console.WriteLine($"LS=({l.LeftX},{l.LeftY}) RS=({l.RightX},{l.RightY}) L2={l.L2} R2={l.R2} buttons={l.Buttons}");
    Console.WriteLine($"gyro=({l.GyroX},{l.GyroY},{l.GyroZ}) accel=({l.AccelX},{l.AccelY},{l.AccelZ}) battery={l.BatteryPercent}% {l.Charging}");
    Console.WriteLine($"touch1={l.Touch1} touch2={l.Touch2}");
    lock (events) foreach (var e in events.Take(60)) Console.WriteLine(e);
    return 0;
}

// "btspeaker": Bluetooth speaker report (0x36) generator against a fake controller (nothing is sent).
if (args.Length > 0 && args[0] == "btspeaker")
{
    var got36 = new List<(double Ms, byte[] Data)>();
    var clk = System.Diagnostics.Stopwatch.StartNew();
    var fakeState = new byte[47]; fakeState[0] = 0x0F; fakeState[44] = 0xAB; // pretend lightbar red = 0xAB
    var eng = AuroraPad.Core.Haptics.HapticsEngine.CreateBluetooth(r => { lock (got36) got36.Add((clk.Elapsed.TotalMilliseconds, (byte[])r.Clone())); }, "fake", () => fakeState);
    eng.Configure(AuroraPad.Core.Haptics.HapticsMode.Off, 1.0, 200, speaker: true, speakerVolume: 0.8);
    Thread.Sleep(3000);
    eng.Dispose();
    List<(double Ms, byte[] Data)> all; lock (got36) all = got36.Where(g => g.Data[0] == 0x36).ToList();
    var r0 = all[^1].Data;
    uint crc = 0xFFFFFFFF;
    foreach (var b in new byte[] { 0xA2 }.Concat(r0.Take(394))) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1; }
    crc = ~crc;
    var iv = all.Zip(all.Skip(1), (a, b) => b.Ms - a.Ms).ToList();
    Console.WriteLine($"0x36 reports in 1.5 s: {all.Count}, size {r0.Length}, speaker error: {eng.SpeakerError ?? "none"}");
    Console.WriteLine($"headers: cfg {r0[2]:X2}/{r0[3]} state {r0[11]:X2}/{r0[12]} haptics {r0[76]:X2}/{r0[77]} speaker {r0[142]:X2}/{r0[143]}");
    Console.WriteLine($"state copied: lightbar byte = 0x{r0[13 + 44]:X2} (expected AB)");
    Console.WriteLine($"opus frame: first bytes {Convert.ToHexString(r0, 144, 8)}, non-zero bytes {r0.Skip(144).Take(200).Count(b => b != 0)}");
    Console.WriteLine($"interval avg {iv.Average():F2} ms, max {iv.Max():F2}; span-based period {(all[^1].Ms - all[0].Ms) / (all.Count - 1):F3} ms, first at {all[0].Ms:F0} ms; CRC ok: {BitConverter.ToUInt32(r0, 394) == crc}");
    return 0;
}
// "bthaptics": Bluetooth haptics report generator against a fake controller (nothing is sent).
if (args.Length > 0 && args[0] == "bthaptics")
{
    var btReports = new List<(double Ms, byte[] Data)>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var engine = AuroraPad.Core.Haptics.HapticsEngine.CreateBluetooth(r => { lock (btReports) btReports.Add((sw.Elapsed.TotalMilliseconds, (byte[])r.Clone())); }, "fake");
    engine.Configure(AuroraPad.Core.Haptics.HapticsMode.RumbleToHaptics, 1.0, 200);
    engine.SetRumble(255, 128);
    Thread.Sleep(1000);
    engine.Dispose();
    List<(double Ms, byte[] Data)> got; lock (btReports) got = btReports.ToList();
    var r0 = got[^1].Data;
    // Recompute the CRC the way the controller checks it: CRC-32 over 0xA2 + bytes 0..136.
    uint crc = 0xFFFFFFFF;
    foreach (var b in new byte[] { 0xA2 }.Concat(r0.Take(137))) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1; }
    crc = ~crc;
    var intervals = got.Zip(got.Skip(1), (a, b) => b.Ms - a.Ms).ToList();
    var samples = r0.Skip(13).Take(64).Select(b => (int)(sbyte)b).ToArray();
    Console.WriteLine($"reports in 1 s: {got.Count} (expected ~94), size {r0.Length}, id 0x{r0[0]:X2}");
    Console.WriteLine($"header: {Convert.ToHexString(r0, 1, 12)}  (seq nibble, 0x91 07 FE 00 00 00 00 FF ctr, 0x92 40)");
    Console.WriteLine($"interval avg {intervals.Average():F2} ms, min {intervals.Min():F2}, max {intervals.Max():F2}");
    Console.WriteLine($"samples L/R range: {samples.Min()}..{samples.Max()}  first 8: {string.Join(",", samples.Take(8))}");
    Console.WriteLine($"CRC ok: {BitConverter.ToUInt32(r0, 137) == crc}");
    return 0;
}
// "touchgate": simulated touches through the touchpad mouse lock; no real input is sent.
if (args.Length > 0 && args[0] == "touchgate")
{
    int sent = 0;
    AuroraPad.Core.Mapping.InputSimulator.TestSink = _ => Interlocked.Increment(ref sent);
    var settings = new AuroraPad.Core.Mapping.TouchpadMouseSettings { AutoLockSeconds = 1 };
    var tp = new AuroraPad.Core.Mapping.TouchpadMouse();
    tp.ActiveChanged += on => Console.WriteLine($"   -> touchpad mouse {(on ? "ATIVO" : "travado")}");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    ushort x = 900;
    int jitter = int.TryParse(Environment.GetEnvironmentVariable("JITTER"), out var jv) ? jv : 0;
    var rnd = new Random(1);
    Console.WriteLine($"(tremor simulado: ±{jitter} unidades por leitura)");
    void Feed(bool touching, int ms, bool moving = false, bool click = false)
    {
        var end = sw.Elapsed.TotalMilliseconds + ms;
        while (sw.Elapsed.TotalMilliseconds < end)
        {
            if (moving) x += 6;
            // Real fingers are never perfectly still: a few units of jitter on every report (env JITTER).
            int j = jitter == 0 ? 0 : rnd.Next(-jitter, jitter + 1);
            var inp = new DualSenseInput { Touch1 = new TouchPoint(touching, 1, (ushort)(x + j), (ushort)(500 + rnd.Next(-jitter, jitter + 1))), Buttons = click ? DualSenseButtons.Touchpad : DualSenseButtons.None };
            tp.Process(inp, settings, sw.Elapsed.TotalSeconds);
            Thread.Sleep(4);
        }
    }
    Console.WriteLine("1) Deslizar com o touchpad travado:"); sent = 0; Feed(true, 200, true); Feed(false, 50); Console.WriteLine($"   eventos de mouse: {sent}");
    Console.WriteLine("2) Dois toques (140 ms cada, 180 ms entre eles):"); sent = 0; Feed(true, 140); Feed(false, 180); Feed(true, 140); Feed(false, 100); Console.WriteLine($"   cliques/eventos indesejados: {sent}");
    Console.WriteLine("3) Deslizar com ele ativo:"); sent = 0; Feed(true, 200, true); Feed(false, 50); Console.WriteLine($"   eventos de mouse: {sent}");
    Console.WriteLine("4) Ficar 1,3 s sem tocar:"); Feed(false, 1300);
    Console.WriteLine("5) Segurar o dedo 700 ms e já deslizar:"); sent = 0; Feed(true, 700); Feed(true, 200, true); Feed(false, 50); Console.WriteLine($"   eventos de mouse ao deslizar: {sent}");
    Console.WriteLine("6) Segurar o clique do touchpad 5,2 s:"); Feed(true, 5200, click: true);
    Console.WriteLine("7) Soltar o clique mas deixar o dedo apoiado 1 s (não pode religar):"); Feed(true, 1000);
    Console.WriteLine("8) Tirar o dedo e dar dois toques:"); Feed(false, 100); Feed(true, 140); Feed(false, 180); Feed(true, 140); Feed(false, 100);
    Console.WriteLine("9) Um toque só com ele ativo (deve clicar):"); sent = 0; Feed(true, 140); Feed(false, 100); Console.WriteLine($"   eventos (2 = clique descer+subir): {sent}");
    AuroraPad.Core.Mapping.InputSimulator.TestSink = null;
    return 0;
}

// "activators": simulated presses through the mapping engine; no real input is sent.
if (args.Length > 0 && args[0] == "activators")
{
    var log = new List<string>();
    var t0 = DateTime.UtcNow;
    AuroraPad.Core.Mapping.InputSimulator.TestSink = e => { lock (log) log.Add($"{(DateTime.UtcNow - t0).TotalMilliseconds,5:F0}ms {e}"); };
    var engine = new AuroraPad.Core.Mapping.MappingEngine();
    var input0 = DualSenseInput.Neutral;

    void Run(string title, AuroraPad.Core.Mapping.ButtonMapping map, params (bool Down, int Ms)[] seq)
    {
        var prof = new Profile { Mappings = [map], Touchpad = { Enabled = false } };
        lock (log) log.Clear();
        t0 = DateTime.UtcNow;
        var injectedSeen = DualSenseButtons.None;
        foreach (var (down, ms) in seq)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                var inp = new DualSenseInput { Buttons = down ? DualSenseButtons.Cross : DualSenseButtons.None };
                var (_, inj) = engine.Process(inp, prof);
                injectedSeen |= inj;
                Thread.Sleep(4);
            }
        }
        Thread.Sleep(80);
        lock (log) Console.WriteLine($"{title}: {(log.Count == 0 ? "(nada enviado)" : string.Join(" | ", log))}{(injectedSeen != 0 ? $"  [jogo recebe: {injectedSeen}]" : "")}");
        engine.ReleaseAll();
    }

    var key = new AuroraPad.Core.Mapping.ButtonMapping { Button = DualSenseButtons.Cross, Target = AuroraPad.Core.Mapping.MappingTarget.Keyboard, VirtualKey = 0x41 };
    AuroraPad.Core.Mapping.ButtonMapping With(Action<AuroraPad.Core.Mapping.ButtonMapping> f) { var m = new AuroraPad.Core.Mapping.ButtonMapping { Button = key.Button, Target = key.Target, VirtualKey = key.VirtualKey }; f(m); return m; }

    Run("Press, toque curto       ", With(_ => { }), (true, 100), (false, 50));
    Run("Tap, toque curto         ", With(m => m.Activator = AuroraPad.Core.Mapping.PressActivator.Tap), (true, 100), (false, 50));
    Run("Tap, segurado 600ms      ", With(m => m.Activator = AuroraPad.Core.Mapping.PressActivator.Tap), (true, 600), (false, 50));
    Run("DoubleTap, 1 toque       ", With(m => m.Activator = AuroraPad.Core.Mapping.PressActivator.DoubleTap), (true, 80), (false, 400));
    Run("DoubleTap, 2 toques      ", With(m => m.Activator = AuroraPad.Core.Mapping.PressActivator.DoubleTap), (true, 80), (false, 100), (true, 80), (false, 100));
    Run("LongPress, toque curto   ", With(m => m.Activator = AuroraPad.Core.Mapping.PressActivator.LongPress), (true, 100), (false, 100));
    Run("LongPress, segurado 700ms", With(m => m.Activator = AuroraPad.Core.Mapping.PressActivator.LongPress), (true, 700), (false, 50));
    Run("Toggle, 2 apertos        ", With(m => m.Toggle = true), (true, 80), (false, 150), (true, 80), (false, 50));
    AuroraPad.Core.Mapping.InputSimulator.TestSink = null;
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
