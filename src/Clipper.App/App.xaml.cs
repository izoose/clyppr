using System.IO;
using System.Windows;
using Clipper.Core;

namespace Clipper.App;

public partial class App : Application
{
    public AppSettings Settings { get; private set; } = null!;
    public ClipLibrary Library { get; private set; } = null!;
    public RecordingService Recording { get; private set; } = null!;

    private TrayManager? _tray;
    private MainWindow? _mainWindow;
    private bool _balloonShown;

    public bool IsQuitting { get; private set; }

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;   // tray keeps the app alive when the window closes

        Settings = AppSettings.Load();
        Directory.CreateDirectory(Settings.ClipsDirectory);
        Library = new ClipLibrary();
        Recording = new RecordingService(Settings, Library);

        var vm = new MainViewModel(Settings, Library, Recording);
        _mainWindow = new MainWindow { DataContext = vm };
        MainWindow = _mainWindow;

        _tray = new TrayManager(this);

        bool startHidden = e.Args.Contains("--tray");
        if (!startHidden) _mainWindow.Show();

        Recording.RegisterHotkey();
        if (Settings.BufferEnabledOnStart) Recording.StartBuffer();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>Called by MainWindow when the user "closes" it — we hide to tray instead of exiting.</summary>
    public void HideToTray()
    {
        _mainWindow?.Hide();
        if (!_balloonShown)
        {
            _balloonShown = true;
            _tray?.ShowBalloon("Clipper is still running", "Buffering in the background. Press your clip hotkey any time.");
        }
    }

    public void QuitFromTray()
    {
        IsQuitting = true;
        try { Recording?.Dispose(); } catch { }
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Recording?.Dispose(); } catch { }
        _tray?.Dispose();
        base.OnExit(e);
    }
}
