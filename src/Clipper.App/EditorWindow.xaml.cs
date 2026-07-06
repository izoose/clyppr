using System.Windows;
using System.Windows.Threading;

namespace Clipper.App;

public partial class EditorWindow : Window
{
    // Segoe MDL2 Assets glyphs via code point (avoids literal PUA chars in source).
    private static readonly string PlayGlyph = ((char)0xE768).ToString();
    private static readonly string PauseGlyph = ((char)0xE769).ToString();
    private static readonly string MuteGlyph = ((char)0xE74F).ToString();
    private static readonly string VolumeGlyph = ((char)0xE767).ToString();

    private readonly EditorViewModel _vm;
    private readonly DispatcherTimer _timer;
    private bool _timerUpdate;
    private bool _playing;
    private bool _muted = true;

    public EditorWindow(EditorViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += OnTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
        Player.MediaOpened += OnMediaOpened;
        Player.MediaEnded += (_, _) => { Player.Pause(); _playing = false; UpdatePlayIcon(); };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Player.Source = new Uri(_vm.FilePath);
        Player.Volume = 0;                 // start muted — never surprise the user with audio
        Player.Play();                     // Play then Pause forces the first frame to render
        Player.Pause();
        _timer.Start();
    }

    private void OnMediaOpened(object? sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            double d = Player.NaturalDuration.TimeSpan.TotalSeconds;
            PosSlider.Maximum = d;
            _vm.SetDuration(d);
        }
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
        if (_timerUpdate) return;                       // change came from the tick, not the user
        Player.Position = TimeSpan.FromSeconds(e.NewValue);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _playing = !_playing;
        if (_playing) Player.Play(); else Player.Pause();
        UpdatePlayIcon();
    }

    private void UpdatePlayIcon() => PlayBtn.Content = _playing ? PauseGlyph : PlayGlyph;

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        Player.Volume = _muted ? 0 : 0.8;
        MuteBtn.Content = _muted ? MuteGlyph : VolumeGlyph;
    }

    private void SetIn_Click(object sender, RoutedEventArgs e) => _vm.SetIn(Player.Position.TotalSeconds);
    private void SetOut_Click(object sender, RoutedEventArgs e) => _vm.SetOut(Player.Position.TotalSeconds);

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        try { Player.Stop(); Player.Close(); } catch { }
    }

    private static string Fmt(double s) => TimeSpan.FromSeconds(s).ToString(@"m\:ss");
}
