using System.IO;
using System.Windows;
using System.Windows.Threading;
using AuroraPad.App.Localization;
using AuroraPad.Core.Profiles;

namespace AuroraPad.App;

public partial class App : Application
{
    public static string CrashLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraPad", "crash.log");

    public static ProfileStore Store { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrash(args.ExceptionObject as Exception);

        // The language must be loaded before any window or view model builds its strings.
        Store = new ProfileStore();
        Settings = Store.LoadSettings();
        L.Load(Settings.Language);
        base.OnStartup(e);
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        MessageBox.Show(e.Exception.Message, "AuroraPad", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteCrash(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { }
    }
}
