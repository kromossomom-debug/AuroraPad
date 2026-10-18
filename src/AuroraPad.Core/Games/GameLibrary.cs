using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AuroraPad.Core.Games;

public enum GameStore { Steam, Epic, Gog }

public sealed record InstalledGame(GameStore Store, string Name, string InstallDir, string? Executable)
{
    public string StoreLabel => Store switch { GameStore.Steam => "Steam", GameStore.Epic => "Epic", _ => "GOG" };
}

/// <summary>Finds installed games from Steam, Epic Games Store and GOG Galaxy.</summary>
public static partial class GameLibrary
{
    public static List<InstalledGame> Scan()
    {
        var games = new List<InstalledGame>();
        try { games.AddRange(ScanSteam()); } catch { }
        try { games.AddRange(ScanEpic()); } catch { }
        try { games.AddRange(ScanGog()); } catch { }
        return games
            .GroupBy(g => g.InstallDir.TrimEnd('\\').ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ---- Steam ---------------------------------------------------------

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPath();

    [GeneratedRegex("\"(name|installdir)\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AcfField();

    private static readonly string[] NonGames = ["Steamworks Common Redistributables", "Steam Linux Runtime", "Proton", "SteamVR"];

    private static IEnumerable<InstalledGame> ScanSteam()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                    ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (steam == null) yield break;
        steam = steam.Replace('/', '\\');

        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string? name = null, dir = null;
                foreach (Match m in AcfField().Matches(File.ReadAllText(acf)))
                {
                    if (m.Groups[1].Value.Equals("name", StringComparison.OrdinalIgnoreCase)) name ??= m.Groups[2].Value;
                    else dir ??= m.Groups[2].Value;
                }
                if (name == null || dir == null || NonGames.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase))) continue;
                var full = Path.Combine(apps, "common", dir);
                if (!Directory.Exists(full)) continue;
                yield return new InstalledGame(GameStore.Steam, name, full, GuessExecutable(full));
            }
        }
    }

    // ---- Epic ----------------------------------------------------------

    private static IEnumerable<InstalledGame> ScanEpic()
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) yield break;
        foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
        {
            InstalledGame? game = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var root = doc.RootElement;
                var name = root.GetProperty("DisplayName").GetString();
                var dir = root.GetProperty("InstallLocation").GetString();
                var exe = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;
                if (name != null && dir != null && Directory.Exists(dir))
                    game = new InstalledGame(GameStore.Epic, name, dir,
                        !string.IsNullOrEmpty(exe) ? Path.Combine(dir, exe) : GuessExecutable(dir));
            }
            catch { }
            if (game != null) yield return game;
        }
    }

    // ---- GOG -----------------------------------------------------------

    private static IEnumerable<InstalledGame> ScanGog()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (key == null) yield break;
        foreach (var id in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(id);
            var name = g?.GetValue("gameName") as string;
            var dir = g?.GetValue("path") as string;
            var exe = g?.GetValue("exe") as string;
            if (name == null || dir == null || !Directory.Exists(dir)) continue;
            yield return new InstalledGame(GameStore.Gog, name, dir, exe != null && File.Exists(exe) ? exe : GuessExecutable(dir));
        }
    }

    // ---- Helpers -------------------------------------------------------

    private static readonly string[] IgnoredExe =
        ["unins", "setup", "crash", "report", "redist", "vcredist", "dxsetup", "launcher_helper", "ue4prereq", "dotnet", "install", "updater", "cefsharp", "easyanticheat", "be_service"];

    /// <summary>Picks the most likely game executable: the largest .exe up to two folders deep.</summary>
    public static string? GuessExecutable(string dir)
    {
        try
        {
            var candidates = new List<FileInfo>();
            void Collect(string d, int depth)
            {
                foreach (var f in Directory.EnumerateFiles(d, "*.exe"))
                {
                    var n = Path.GetFileName(f).ToLowerInvariant();
                    if (!IgnoredExe.Any(n.Contains)) candidates.Add(new FileInfo(f));
                }
                if (depth < 2)
                    foreach (var sub in Directory.EnumerateDirectories(d))
                    {
                        var sn = Path.GetFileName(sub).ToLowerInvariant();
                        if (sn.Contains("redist") || sn is "directx" or "support" or "__installer" or "installers" or "tools" or "engine") continue;
                        Collect(sub, depth + 1);
                    }
            }
            Collect(dir, 0);
            // Launchers are often the biggest file but rarely the window that ends up in the foreground.
            return candidates
                .OrderBy(f => f.Name.Contains("launcher", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }
}
