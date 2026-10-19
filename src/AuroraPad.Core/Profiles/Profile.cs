using System.Text.Json;
using System.Text.Json.Serialization;
using AuroraPad.Core.Mapping;
using AuroraPad.Core.Processing;
using AuroraPad.Core.Protocol;
using AuroraPad.Core.Triggers;

namespace AuroraPad.Core.Profiles;

public enum EmulationMode { None, Xbox360, DualShock4 }

public sealed class Profile
{
    public string Name { get; set; } = "Default";

    // Lighting
    public byte LightbarR { get; set; } = 0;
    public byte LightbarG { get; set; } = 120;
    public byte LightbarB { get; set; } = 255;
    public int LightbarBrightness { get; set; } = 100;
    public bool[] PlayerLeds { get; set; } = [false, false, true, false, false];
    public LedBrightness PlayerLedBrightness { get; set; } = LedBrightness.High;
    public MicLedMode MicLed { get; set; } = MicLedMode.Off;
    /// <summary>Lightbar colour reflects battery level instead of the fixed colour.</summary>
    public bool BatteryLightbar { get; set; }

    // Triggers
    public TriggerSettings LeftTrigger { get; set; } = new();
    public TriggerSettings RightTrigger { get; set; } = new();
    public double TriggerDeadzone { get; set; }

    // Sticks
    public StickSettings LeftStick { get; set; } = new();
    public StickSettings RightStick { get; set; } = new();

    // Rumble
    public int RumbleIntensity { get; set; } = 100;

    // Audio haptics (DualSense over USB)
    public Haptics.HapticsMode HapticsMode { get; set; } = Haptics.HapticsMode.Off;
    public int HapticsGain { get; set; } = 100;
    public int HapticsLowPassHz { get; set; } = 200;
    /// <summary>When rumble goes to the haptics, stop the classic rumble emulation.</summary>
    public bool HapticsReplaceRumble { get; set; } = true;

    // Emulation
    public EmulationMode Emulation { get; set; } = EmulationMode.Xbox360;
    public bool SwapAB { get; set; }
    /// <summary>DualShock 4 emulation: let the game drive the lightbar colour.</summary>
    public bool UseGameLightbar { get; set; } = true;

    // Keyboard / mouse
    public List<ButtonMapping> Mappings { get; set; } = [];
    public MouseSettings Mouse { get; set; } = new();
    public TouchpadMouseSettings Touchpad { get; set; } = new();
    public GyroStickSettings GyroStick { get; set; } = new();

    /// <summary>Holding this button switches every mapping to the Shift layer.</summary>
    public DualSenseButtons ShiftButton { get; set; } = DualSenseButtons.None;
    /// <summary>Two-stage triggers: analog value (0-255) where the "half pull" button fires.</summary>
    public int TriggerHalfPull { get; set; } = 110;
    /// <summary>Analog value where the "full pull" button fires.</summary>
    public int TriggerFullPull { get; set; } = 245;

    // Turbo (rapid fire) on the virtual controller
    public DualSenseButtons TurboButtons { get; set; }
    /// <summary>Presses per second, 2-30.</summary>
    public int TurboRate { get; set; } = 10;

    /// <summary>Executable names (e.g. "eldenring.exe") that activate this profile automatically.</summary>
    public List<string> Executables { get; set; } = [];

    public byte PlayerLedMask()
    {
        byte m = 0;
        for (int i = 0; i < 5 && i < PlayerLeds.Length; i++)
            if (PlayerLeds[i]) m |= (byte)(1 << i);
        return m;
    }

    public Profile Clone()
    {
        var json = JsonSerializer.Serialize(this, ProfileStore.JsonOptions);
        return JsonSerializer.Deserialize<Profile>(json, ProfileStore.JsonOptions)!;
    }
}

public sealed class AppSettings
{
    public string ActiveProfile { get; set; } = "Default";
    public bool UseIncomingUdp { get; set; } = true;
    public int IncomingUdpPort { get; set; } = 6969;
    public string IncomingUdpAddress { get; set; } = "127.0.0.1";
    public bool DisconnectEmulationOnExit { get; set; } = true;

    /// <summary>PS + button shortcuts (profile switching, mouse toggle...).</summary>
    public bool EnableShortcuts { get; set; } = true;

    /// <summary>Keep learning the gyro's resting offset whenever the controller lies still.</summary>
    public bool GyroAutoCalibrate { get; set; } = true;
    /// <summary>Tray notification when a controller's battery drops to 20% and 10%.</summary>
    public bool LowBatteryAlert { get; set; } = true;

    public bool UseOsc { get; set; }
    public string OscAddress { get; set; } = "127.0.0.1";
    public int OscPort { get; set; } = 8000;

    /// <summary>UI language: pt-BR, en-US or es.</summary>
    public string Language { get; set; } = "pt-BR";

    public bool UseDsuServer { get; set; }
    public int DsuPort { get; set; } = 26760;

    /// <summary>Switch to a game's profile while its window is in the foreground.</summary>
    public bool AutoSwitchProfiles { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public bool StartMinimized { get; set; }
    public bool LaunchOnBoot { get; set; }

    /// <summary>Hide physical DualSense controllers from games with HidHide while emulating.</summary>
    public bool UseHidHide { get; set; }

    /// <summary>Instance ids this app asked HidHide to block; cleared on exit and on the next start after a crash.</summary>
    public List<string> HiddenInstanceIds { get; set; } = [];
}

/// <summary>Stores profiles and settings as JSON under %APPDATA%\AuroraPad.</summary>
public sealed class ProfileStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Root { get; }
    private string ProfilesDir => Path.Combine(Root, "Profiles");
    private string SettingsPath => Path.Combine(Root, "settings.json");

    public ProfileStore(string? root = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Root = root ?? Path.Combine(appData, "AuroraPad");
        if (root == null) MigrateFrom(Path.Combine(appData, "DSX" + "Clone"));
        Directory.CreateDirectory(ProfilesDir);
    }

    /// <summary>Copies profiles and settings from the app's previous name on first start.</summary>
    private void MigrateFrom(string oldRoot)
    {
        if (Directory.Exists(Root) || !Directory.Exists(oldRoot)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(oldRoot, "*.json", SearchOption.AllDirectories))
            {
                var target = Path.Combine(Root, Path.GetRelativePath(oldRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }
        catch { /* start fresh if the old data can't be read */ }
    }

    public List<Profile> LoadProfiles()
    {
        var list = new List<Profile>();
        foreach (var f in Directory.EnumerateFiles(ProfilesDir, "*.json"))
        {
            try
            {
                var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(f), JsonOptions);
                if (p != null) list.Add(p);
            }
            catch { /* skip corrupt profile */ }
        }
        if (list.Count == 0)
        {
            var def = new Profile();
            Save(def);
            list.Add(def);
        }
        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Save(Profile p) =>
        File.WriteAllText(PathFor(p.Name), JsonSerializer.Serialize(p, JsonOptions));

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new();
        }
        catch { }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings s) =>
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOptions));

    private string PathFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(ProfilesDir, safe + ".json");
    }
}
