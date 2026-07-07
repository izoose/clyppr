using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Clipper.App;

public partial class MainWindow : Window
{
    private static readonly string MaxGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => MaxBtn.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaxGlyph;
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing hides to tray so the replay buffer keeps running; real exit is via the tray "Quit".
        if (!App.Current.IsQuitting)
        {
            e.Cancel = true;
            App.Current.HideToTray();
        }
        base.OnClosing(e);
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } btn)
        {
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }
    }
}
