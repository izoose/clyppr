using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Clipper.App;

public partial class ClipViewerWindow : Window
{
    private static readonly string PlayGlyph = ((char)0xE768).ToString();
    private static readonly string PauseGlyph = ((char)0xE769).ToString();
    private static readonly string MuteGlyph = ((char)0xE74F).ToString();
    private static readonly string VolumeGlyph = ((char)0xE767).ToString();
    private static readonly string MaxGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    private readonly ClipViewerViewModel _vm;
    private readonly DispatcherTimer _timer;
    private bool _timerUpdate;
    private bool _playing;
    private bool _muted = true;

    public ClipViewerWindow(ClipViewerViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += OnTick;

        Loaded += (_, _) => { LoadClip(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); try { Player.Stop(); Player.Close(); } catch { } _vm.Main.RefreshCards(); };
        StateChanged += (_, _) => MaxBtn.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaxGlyph;
        KeyDown += OnKeyDown;

        Player.MediaOpened += OnMediaOpened;
        Player.MediaEnded += (_, _) => { Player.Pause(); _playing = false; PlayBtn.Content = PlayGlyph; };

        _vm.SourceChanged += LoadClip;
        _vm.RequestClose += () => { try { Close(); } catch { } };
    }

    private void LoadClip()
    {
        if (string.IsNullOrEmpty(_vm.FilePath)) return;
        Player.Source = new Uri(_vm.FilePath);
        Player.Volume = _muted ? 0 : 1.0;
        Player.Position = TimeSpan.Zero;
        Player.Play();
        if (!_playing) Player.Pause();
    }

    private void OnMediaOpened(object? sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan) PosSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!Player.NaturalDuration.HasTimeSpan) return;
        _timerUpdate = true;
        PosSlider.Value = Player.Position.TotalSeconds;
        _timerUpdate = false;
        TimeText.Text = $"{Fmt(Player.Position.TotalSeconds)} / {Fmt(PosSlider.Maximum)}";
    }

    private void PosSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_timerUpdate) return;
        Player.Position = TimeSpan.FromSeconds(e.NewValue);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _playing = !_playing;
        if (_playing) Player.Play(); else Player.Pause();
        PlayBtn.Content = _playing ? PauseGlyph : PlayGlyph;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        Player.Volume = _muted ? 0 : 1.0;
        MuteBtn.Content = _muted ? MuteGlyph : VolumeGlyph;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left) _vm.PrevCommand.Execute(null);
        else if (e.Key == Key.Right) _vm.NextCommand.Execute(null);
        else if (e.Key == Key.Space) PlayPause_Click(this, e);
        else if (e.Key == Key.Escape) Close();
    }

    private void Albums_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AlbumsBtn };
        foreach (var a in _vm.Main.Albums)
        {
            var album = a;
            var mi = new MenuItem { Header = album.Name, IsChecked = _vm.Current?.AlbumId == album.Id };
            mi.Click += (_, _) => _vm.SetAlbum(album);
            menu.Items.Add(mi);
        }
        if (_vm.Main.Albums.Count > 0) menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = "Remove from albums" };
        remove.Click += (_, _) => _vm.SetAlbum(null);
        menu.Items.Add(remove);
        var newAlbum = new MenuItem { Header = "New album…" };
        newAlbum.Click += (_, _) => { _vm.Main.NewAlbumCommand.Execute(null); };
        menu.Items.Add(newAlbum);
        menu.IsOpen = true;
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Fmt(double s) => TimeSpan.FromSeconds(s).ToString(@"m\:ss");
}
