using System.IO;
using System.Windows;
using Clipper.Core;

namespace Clipper.App;

public partial class App : Application
{
    public AppSettings Settings { get; private set; } = null!;
    public ClipLibrary Library { get; private set; } = null!;
    public RecordingService Recording { get; private set; } = null!;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = AppSettings.Load();
        Directory.CreateDirectory(Settings.ClipsDirectory);
        Library = new ClipLibrary();
        Recording = new RecordingService(Settings, Library);

        var vm = new MainViewModel(Settings, Library, Recording);
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Recording.RegisterHotkey();
        if (Settings.BufferEnabledOnStart) Recording.StartBuffer();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Recording?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
