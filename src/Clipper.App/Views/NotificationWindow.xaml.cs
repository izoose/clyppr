using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Clipper.App;

public partial class NotificationWindow : Window
{
    public NotificationWindow(string message)
    {
        InitializeComponent();
        Message.Text = message;
        Opacity = 0;

        Loaded += (_, _) =>
        {
            // Top-left of the work area (like Medal's in-game toast).
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + 8;
            Top = wa.Top + 8;

            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

            var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            life.Tick += (_, _) =>
            {
                life.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280));
                fade.Completed += (_, _) => { try { Close(); } catch { } };
                BeginAnimation(OpacityProperty, fade);
            };
            life.Start();
        };
    }
}

/// <summary>Shows a single Medal-style on-screen toast (replacing any current one).</summary>
public static class Notifier
{
    private static NotificationWindow? _current;

    public static void Show(string message)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            try { _current?.Close(); } catch { }
            _current = new NotificationWindow(message);
            _current.Show();
        });
    }
}
