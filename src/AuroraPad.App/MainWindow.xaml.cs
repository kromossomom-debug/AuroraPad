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

        bool minimized = _vm.StartMinimized || Environment.GetCommandLineArgs().Contains("--minimized");
        if (minimized)
        {
            WindowState = WindowState.Minimized;
            if (_vm.MinimizeToTray) Loaded += (_, _) => Hide();
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _vm.MinimizeToTray) Hide();
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
        _tray.Visible = false;
        _tray.Dispose();
        _vm.Dispose();
        base.OnClosing(e);
    }
}
