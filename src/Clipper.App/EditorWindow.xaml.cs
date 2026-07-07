using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Clipper.App;

public partial class EditorWindow : Window
{
    private static readonly string PlayGlyph = ((char)0xE768).ToString();
    private static readonly string PauseGlyph = ((char)0xE769).ToString();
    private static readonly string MuteGlyph = ((char)0xE74F).ToString();
    private static readonly string VolumeGlyph = ((char)0xE767).ToString();
    private static readonly string MaxGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    private readonly EditorViewModel _vm;
    private readonly DispatcherTimer _timer;
    private bool _timerUpdate;
    private bool _playing;
    private bool _muted = true;

    private double _zoom = 1.0;
    private bool _panning;
    private Point _panStart;

    public EditorWindow(EditorViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += OnTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
        StateChanged += (_, _) => MaxBtn.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaxGlyph;
        Player.MediaOpened += OnMediaOpened;
        Player.MediaEnded += (_, _) => { Player.Pause(); _playing = false; UpdatePlayIcon(); };
    }

    // ---- window ----
    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Player.Source = new Uri(_vm.FilePath);
        Player.Volume = 0;
        Player.Play();
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
        if (_timerUpdate) return;
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
        Player.Volume = _muted ? 0 : 1.0;
        MuteBtn.Content = _muted ? MuteGlyph : VolumeGlyph;
    }

    private void SetIn_Click(object sender, RoutedEventArgs e) => _vm.SetIn(Player.Position.TotalSeconds);
    private void SetOut_Click(object sender, RoutedEventArgs e) => _vm.SetOut(Player.Position.TotalSeconds);

    // ---- zoom & pan ----
    private void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 1.0, 6.0);
        Zoom.ScaleX = Zoom.ScaleY = _zoom;
        ZoomText.Text = $"{_zoom * 100:0}%";
        if (_zoom <= 1.0) { Pan.X = 0; Pan.Y = 0; }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void Preview_Wheel(object sender, MouseWheelEventArgs e) => SetZoom(e.Delta > 0 ? _zoom * 1.15 : _zoom / 1.15);

    private void Preview_Down(object sender, MouseButtonEventArgs e)
    {
        if (_zoom <= 1.0) return;
        _panning = true;
        _panStart = e.GetPosition(PreviewHost);
        PreviewHost.CaptureMouse();
    }

    private void Preview_Move(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(PreviewHost);
        Pan.X += p.X - _panStart.X;
        Pan.Y += p.Y - _panStart.Y;
        _panStart = p;
    }

    private void Preview_Up(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        PreviewHost.ReleaseMouseCapture();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        try { Player.Stop(); Player.Close(); } catch { }
    }

    private static string Fmt(double s) => TimeSpan.FromSeconds(s).ToString(@"m\:ss");
}
