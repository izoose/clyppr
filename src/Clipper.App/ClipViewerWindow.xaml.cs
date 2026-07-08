using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Clipper.App;

public partial class ClipViewerWindow : Window
{
    private static readonly string PlayGlyph = ((char)0xE768).ToString();
    private static readonly string PauseGlyph = ((char)0xE769).ToString();
    private static readonly string MuteGlyph = ((char)0xE74F).ToString();
    private static readonly string VolumeGlyph = ((char)0xE767).ToString();
    private static readonly string MaxGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    // Recorded game audio comes in quiet; lift the whole mix so clips are clearly audible.
    private const float PreviewBoost = 1.7f;

    private readonly ClipViewerViewModel _vm;
    private bool _timerUpdate;
    private bool _playing = true;   // autoplay when a clip opens (like Medal)
    private bool _muted;   // clips play with sound by default (like Medal)
    private bool _scrubbing;      // user is dragging the scrub thumb
    private double? _seekTarget;  // where we just seeked to; hold the slider here until it catches up
    private int _seekFrames;

    private PreviewMixer? _mixer;
    private bool _mixerFailed;
    private int _loadGen;

    public ClipViewerWindow(ClipViewerViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        // Seek only when the drag finishes (rapid Position sets mid-drag get dropped by MediaElement).
        PosSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _scrubbing = true));
        PosSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { _scrubbing = false; SeekTo(PosSlider.Value); }));

        Loaded += (_, _) => { MuteBtn.Content = VolumeGlyph; LoadClip(); CompositionTarget.Rendering += OnRender; _vm.ScanPlayers(); };
        Closed += (_, _) =>
        {
            try { CompositionTarget.Rendering -= OnRender; } catch { }
            try { Player.Stop(); Player.Close(); } catch { }
            try { _mixer?.Dispose(); } catch { }
            try { _vm.Main.RefreshCards(); } catch { }
        };
        StateChanged += (_, _) => MaxBtn.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaxGlyph;
        PreviewKeyDown += OnKeyDown;   // Preview so play/arrow keys aren't swallowed by focused buttons

        Player.MediaOpened += OnMediaOpened;
        Player.MediaEnded += (_, _) => { Player.Pause(); _mixer?.Pause(); _playing = false; PlayBtn.Content = PlayGlyph; };

        _vm.SourceChanged += LoadClip;
        _vm.RequestClose += () => { try { Close(); } catch { } };
        _vm.SeekRequested += t => { SeekTo(t); if (!_playing) PlayPause_Click(this, new RoutedEventArgs()); };
    }

    private async void LoadClip()
    {
        if (string.IsNullOrEmpty(_vm.FilePath)) return;
        SetZoom(1.0);
        int gen = ++_loadGen;

        // tear down the previous clip's audio
        var old = _mixer; _mixer = null; _mixerFailed = false;
        try { old?.Dispose(); } catch { }

        Player.Source = new Uri(_vm.FilePath);
        Player.Volume = 0;                 // video is muted; the mixer owns all audio (plays every track)
        Player.Position = TimeSpan.Zero;
        Player.Play();
        if (!_playing) Player.Pause();
        PlayBtn.Content = _playing ? PauseGlyph : PlayGlyph;

        // Build a mix of ALL the clip's audio tracks (MediaElement would only play one, often a silent one).
        var mixer = await PreviewMixer.CreateAsync(_vm.FilePath, 8);
        if (gen != _loadGen) { try { mixer?.Dispose(); } catch { } return; }   // a newer clip started loading
        _mixer = mixer;

        if (_mixer is null)
        {
            _mixerFailed = true;
            Player.Volume = _muted ? 0 : 1.0;     // no separate tracks — fall back to the file's own audio
        }
        else
        {
            for (int i = 0; i < _mixer.TrackCount; i++) _mixer.SetVolume(i, PreviewBoost);
            _mixer.SetMasterVolume(_muted ? 0f : 1f);
            if (_playing) { _mixer.Seek(Player.Position); _mixer.Play(); }
        }
    }

    private void OnMediaOpened(object? sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan) PosSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
    }

    private void OnRender(object? sender, EventArgs e)
    {
        if (!Player.NaturalDuration.HasTimeSpan || _scrubbing) return;   // don't fight an active drag
        double vpos = Player.Position.TotalSeconds;

        if (_seekTarget is double target)
        {
            // Video is still seeking. Hold the UI at the target and keep audio paused until the
            // video actually arrives, then resume audio locked to the video's real position.
            if (Math.Abs(vpos - target) < 0.35 || ++_seekFrames > 120)
            {
                _seekTarget = null;
                _seekFrames = 0;
                if (_playing && !_muted && !_mixerFailed) { _mixer?.Seek(Player.Position); _mixer?.Play(); }
            }
            else vpos = target;
        }
        else if (_playing && !_muted && !_mixerFailed && _mixer is not null)
        {
            // Drift correction: keep the audio mixer glued to the video clock (fixes slow desync too).
            if (Math.Abs(_mixer.Position.TotalSeconds - vpos) > 0.2) _mixer.Seek(TimeSpan.FromSeconds(vpos));
        }

        _timerUpdate = true;
        PosSlider.Value = vpos;
        _timerUpdate = false;
        TimeText.Text = $"{Fmt(vpos)} / {Fmt(PosSlider.Maximum)}";
    }

    private void PosSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_timerUpdate) return;
        if (_scrubbing) { TimeText.Text = $"{Fmt(e.NewValue)} / {Fmt(PosSlider.Maximum)}"; return; }  // drag: show time, seek on release
        SeekTo(e.NewValue);   // click on the track (or keyboard) → seek now
    }

    private void SeekTo(double seconds)
    {
        if (!Player.NaturalDuration.HasTimeSpan) return;
        double max = Player.NaturalDuration.TimeSpan.TotalSeconds;
        double t = Math.Clamp(seconds, 0, max);
        _seekTarget = t;
        _seekFrames = 0;
        var ts = TimeSpan.FromSeconds(t);
        _mixer?.Pause();          // silence audio while the video seeks; resumes in sync when it lands
        Player.Position = ts;
        _mixer?.Seek(ts);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _playing = !_playing;
        if (_playing)
        {
            Player.Play();
            if (!_mixerFailed && !_muted) { _mixer?.Seek(Player.Position); _mixer?.Play(); }
        }
        else
        {
            Player.Pause();
            _mixer?.Pause();
        }
        PlayBtn.Content = _playing ? PauseGlyph : PlayGlyph;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        MuteBtn.Content = _muted ? MuteGlyph : VolumeGlyph;
        if (_mixerFailed) Player.Volume = _muted ? 0 : 1.0;
        else _mixer?.SetMasterVolume(_muted ? 0f : 1f);
        if (!_muted && _playing) _mixer?.Play();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space: PlayPause_Click(this, e); e.Handled = true; break;
            case Key.Left: SeekBy(-5); e.Handled = true; break;      // rewind 5s
            case Key.Right: SeekBy(5); e.Handled = true; break;      // forward 5s
            case Key.Up: SetZoom(_zoom * 1.15); e.Handled = true; break;
            case Key.Down: SetZoom(_zoom / 1.15); e.Handled = true; break;
            case Key.OemComma: _vm.PrevCommand.Execute(null); e.Handled = true; break;   // , previous clip
            case Key.OemPeriod: _vm.NextCommand.Execute(null); e.Handled = true; break;  // . next clip
            case Key.D0: case Key.NumPad0: SetZoom(1.0); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    private void SeekBy(double seconds)
    {
        double from = _seekTarget ?? Player.Position.TotalSeconds;   // chain rapid presses off the last target
        SeekTo(from + seconds);
    }

    // ---- zoom & pan (like the editor) ----
    private double _zoom = 1.0;
    private bool _panning;
    private Point _panStart;

    private void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 1.0, 6.0);
        Zoom.ScaleX = Zoom.ScaleY = _zoom;
        ZoomText.Text = $"{_zoom * 100:0}%";
        ZoomBadge.Visibility = _zoom > 1.001 ? Visibility.Visible : Visibility.Collapsed;
        if (_zoom <= 1.0) { Pan.X = 0; Pan.Y = 0; }
    }

    private void Video_Wheel(object sender, MouseWheelEventArgs e) => SetZoom(e.Delta > 0 ? _zoom * 1.15 : _zoom / 1.15);

    private void Video_Down(object sender, MouseButtonEventArgs e)
    {
        if (_zoom <= 1.0) return;
        _panning = true;
        _panStart = e.GetPosition(VideoHost);
        VideoHost.CaptureMouse();
    }

    private void Video_Move(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(VideoHost);
        Pan.X += p.X - _panStart.X;
        Pan.Y += p.Y - _panStart.Y;
        _panStart = p;
    }

    private void Video_Up(object sender, MouseButtonEventArgs e) { _panning = false; VideoHost.ReleaseMouseCapture(); }
    private void Video_ResetZoom(object sender, MouseButtonEventArgs e) => SetZoom(1.0);

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

    private void PlayerChip_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PlayerHit hit } fe) return;
        var menu = new ContextMenu { PlacementTarget = fe };

        var jump = new MenuItem { Header = $"Jump to @{hit.Name}" };
        jump.Click += (_, _) => _vm.SeekToPlayerCommand.Execute(hit);
        menu.Items.Add(jump);

        var all = new MenuItem { Header = $"See all my clips with @{hit.Name}" };
        all.Click += (_, _) => { _vm.Main.FilterByPlayer(hit.Name); Close(); };
        menu.Items.Add(all);

        var profile = new MenuItem { Header = "Open Roblox profile" };
        profile.Click += (_, _) => _ = Clipper.Core.RobloxApi.OpenProfileAsync(hit.Name);
        menu.Items.Add(profile);

        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Fmt(double s) => TimeSpan.FromSeconds(s).ToString(@"m\:ss");
}
