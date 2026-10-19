using System.ComponentModel;
using System.Windows;
using AuroraPad.App.ViewModels;
using Forms = System.Windows.Forms;

namespace AuroraPad.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Forms.NotifyIcon _tray;
    private bool _exiting;
    private WindowState _restoreState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(App.Store, App.Settings);
        DataContext = _vm;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Localization.L.T("tray.open"), null, (_, _) => ShowFromTray());
        menu.Items.Add(Localization.L.T("tray.exit"), null, (_, _) => { _exiting = true; Close(); });
        _tray = new Forms.NotifyIcon
        {
            Text = "AuroraPad",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _vm.Notify += (title, text) => _tray.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Warning);

        FitToScreen();

        // --background (used by "start with Windows" and the installer) starts straight in the tray.
        bool background = Environment.GetCommandLineArgs().Contains("--background");
        bool minimized = background || _vm.StartMinimized || Environment.GetCommandLineArgs().Contains("--minimized");
        if (minimized)
        {
            _restoreState = WindowState;
            WindowState = WindowState.Minimized;
            if (background || _vm.MinimizeToTray) Loaded += (_, _) => { Hide(); _vm.SetBackground(true); };
            else _vm.SetBackground(true);
        }
        IsVisibleChanged += (_, _) => _vm.SetBackground(!IsVisible || WindowState == WindowState.Minimized);
    }

    /// <summary>
    /// Never open bigger than the screen (with 150% scaling a 1080p laptop has only ~1280x672 units),
    /// otherwise the title bar ends up off-screen. Restores the last size/position when it still fits,
    /// and opens maximized on small screens or when it was closed maximized.
    /// </summary>
    private void FitToScreen()
    {
        var work = SystemParameters.WorkArea;
        var s = App.Settings;
        MinWidth = Math.Min(MinWidth, work.Width);
        MinHeight = Math.Min(MinHeight, work.Height);

        if (s.WindowWidth is double w && w > 0 && s.WindowHeight is double h && h > 0)
        {
            Width = Math.Min(w, work.Width);
            Height = Math.Min(h, work.Height);
            if (s.WindowLeft is { } left && s.WindowTop is { } top)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = Math.Clamp(left, work.Left, work.Right - Width);
                Top = Math.Clamp(top, work.Top, work.Bottom - Height);
            }
        }
        else
        {
            Width = Math.Min(Width, work.Width - 40);
            Height = Math.Min(Height, work.Height - 40);
        }

        bool smallScreen = work.Width < 1300 || work.Height < 800;
        if (s.WindowMaximized ?? smallScreen) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var s = App.Settings;
        var state = WindowState == WindowState.Minimized ? _restoreState : WindowState;
        s.WindowMaximized = state == WindowState.Maximized;
        var bounds = RestoreBounds.IsEmpty ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            s.WindowLeft = bounds.Left; s.WindowTop = bounds.Top;
            s.WindowWidth = bounds.Width; s.WindowHeight = bounds.Height;
        }
        App.Store.SaveSettings(s);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = _restoreState;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState != WindowState.Minimized) _restoreState = WindowState;
        if (WindowState == WindowState.Minimized && _vm.MinimizeToTray) Hide();
        _vm.SetBackground(!IsVisible || WindowState == WindowState.Minimized);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // With "minimize to tray" on, the close button just hides the window; "Sair" in the tray menu quits.
        if (_vm.MinimizeToTray && !_exiting)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(2000, "AuroraPad", Localization.L.T("tray.stillRunning"), Forms.ToolTipIcon.Info);
            return;
        }
        SavePlacement();
        _tray.Visible = false;
        _tray.Dispose();
        _vm.Dispose();
        base.OnClosing(e);
    }
}
