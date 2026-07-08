using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Clipper.App;

public partial class EditorWindow : Window
{
    private static readonly string PlayGlyph = ((char)0xE768).ToString();
    private static readonly string PauseGlyph = ((char)0xE769).ToString();
    private static readonly string MuteGlyph = ((char)0xE74F).ToString();
    private static readonly string VolumeGlyph = ((char)0xE767).ToString();
    private static readonly string MaxGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    private enum Drag { None, In, Out, Scrub }

    private readonly EditorViewModel _vm;
    private bool _playing;
    private bool _muted;                 // preview audio starts ON so volume changes are audible
    private double _duration;
    private Drag _drag = Drag.None;

    private PreviewMixer? _mixer;
    private bool _mixerFailed;   // true if per-track extraction failed → use the file's own audio

    private double _zoom = 1.0;
    private bool _panning;
    private Point _panStart;

    public EditorWindow(EditorViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        Loaded += OnLoaded;
        Closed += OnClosed;
        StateChanged += (_, _) => MaxBtn.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaxGlyph;
        Player.MediaOpened += OnMediaOpened;
        Player.MediaEnded += (_, _) => StopPlayback();
        CompositionTarget.Rendering += OnRendering;
    }

    // ---- window ----
    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Player.Source = new Uri(_vm.FilePath);
        Player.Volume = 0;              // video is muted; PreviewMixer owns all audible sound
        Player.Play();
        Player.Pause();

        MuteBtn.Content = VolumeGlyph;

        // Build the real-time per-track mixer, then apply the current slider values.
        _mixer = await PreviewMixer.CreateAsync(_vm.FilePath, _vm.Tracks.Count);
        foreach (var t in _vm.Tracks)
        {
            ApplyTrack(t);
            t.PropertyChanged += Track_PropertyChanged;
        }

        if (_mixer is null)
        {
            // Couldn't split the tracks (e.g. an already-flattened clip) — play the file's own audio
            // so preview is never silent. Per-track volume just won't apply for this clip.
            _mixerFailed = true;
            Player.Volume = _muted ? 0 : 1.0;
        }
        else if (_playing && !_muted)
        {
            _mixer.Seek(Player.Position);
            _mixer.Play();
        }
    }

    private void Track_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TrackRowViewModel t && (e.PropertyName is nameof(TrackRowViewModel.Volume) or nameof(TrackRowViewModel.Keep)))
            ApplyTrack(t);
    }

    private void ApplyTrack(TrackRowViewModel t) => _mixer?.SetVolume(t.Index, t.Keep ? t.Volume : 0);

    private void OnMediaOpened(object? sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            _duration = Player.NaturalDuration.TimeSpan.TotalSeconds;
            _vm.SetDuration(_duration);
        }
    }

    // ---- smooth playhead (frame-synced instead of a coarse timer) ----
    private void OnRendering(object? sender, EventArgs e)
    {
        if (_duration <= 0) return;
        double pos = Player.Position.TotalSeconds;
        TimeText.Text = $"{Fmt(pos)} / {Fmt(_duration)}";
        LayoutTimeline(pos);
    }

    private void LayoutTimeline(double pos)
    {
        double w = Timeline.ActualWidth;
        if (w <= 0 || _duration <= 0) return;

        double inX = _vm.InSeconds / _duration * w;
        double outX = _vm.OutSeconds / _duration * w;
        double posX = Math.Clamp(pos / _duration * w, 0, w);

        TrackBg.Width = w;
        Canvas.SetLeft(SelRegion, inX);
        SelRegion.Width = Math.Max(0, outX - inX);

        double playEnd = Math.Clamp(posX, inX, outX);
        Canvas.SetLeft(PlayedRegion, inX);
        PlayedRegion.Width = Math.Max(0, playEnd - inX);

        Canvas.SetLeft(InHandle, inX - InHandle.Width / 2);
        Canvas.SetLeft(OutHandle, outX - OutHandle.Width / 2);
        Canvas.SetLeft(Playhead, posX - Playhead.Width / 2);
    }

    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutTimeline(Player.Position.TotalSeconds);

    // ---- transport ----
    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _playing = !_playing;
        if (_playing)
        {
            _mixer?.Seek(Player.Position);
            Player.Play();
            if (!_muted) _mixer?.Play();
        }
        else
        {
            Player.Pause();
            _mixer?.Pause();
        }
        UpdatePlayIcon();
    }

    private void StopPlayback()
    {
        Player.Pause();
        _mixer?.Pause();
        _playing = false;
        UpdatePlayIcon();
    }

    private void UpdatePlayIcon() => PlayBtn.Content = _playing ? PauseGlyph : PlayGlyph;

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        MuteBtn.Content = _muted ? MuteGlyph : VolumeGlyph;
        if (_mixerFailed) Player.Volume = _muted ? 0 : 1.0;
        _mixer?.SetMasterVolume(_muted ? 0f : 1f);
        if (!_muted && _playing) _mixer?.Play();
    }

    // ---- timeline drag: trim handles + scrub ----
    private const double GrabPx = 11;

    private void Timeline_Down(object sender, MouseButtonEventArgs e)
    {
        if (_duration <= 0) return;
        double w = Timeline.ActualWidth;
        double x = e.GetPosition(Timeline).X;
        double inX = _vm.InSeconds / _duration * w;
        double outX = _vm.OutSeconds / _duration * w;

        if (Math.Abs(x - inX) <= GrabPx) _drag = Drag.In;
        else if (Math.Abs(x - outX) <= GrabPx) _drag = Drag.Out;
        else { _drag = Drag.Scrub; SeekToX(x); }

        Timeline.CaptureMouse();
        e.Handled = true;
    }

    private void Timeline_Move(object sender, MouseEventArgs e)
    {
        if (_drag == Drag.None || _duration <= 0) return;
        double w = Timeline.ActualWidth;
        double time = Math.Clamp(e.GetPosition(Timeline).X / w, 0, 1) * _duration;

        switch (_drag)
        {
            case Drag.In:
                _vm.SetIn(time);
                SeekPreview(_vm.InSeconds);
                break;
            case Drag.Out:
                _vm.SetOut(time);
                SeekPreview(_vm.OutSeconds);
                break;
            case Drag.Scrub:
                SeekToX(e.GetPosition(Timeline).X);
                break;
        }
    }

    private void Timeline_Up(object sender, MouseButtonEventArgs e)
    {
        _drag = Drag.None;
        Timeline.ReleaseMouseCapture();
    }

    private void SeekToX(double x)
    {
        double w = Timeline.ActualWidth;
        SeekPreview(Math.Clamp(x / w, 0, 1) * _duration);
    }

    private void SeekPreview(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _duration));
        Player.Position = t;
        _mixer?.Seek(t);
    }

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
        CompositionTarget.Rendering -= OnRendering;
        foreach (var t in _vm.Tracks) t.PropertyChanged -= Track_PropertyChanged;
        try { Player.Stop(); Player.Close(); } catch { }
        _mixer?.Dispose();
    }

    private static string Fmt(double s) => TimeSpan.FromSeconds(Math.Max(0, s)).ToString(@"m\:ss");
}
