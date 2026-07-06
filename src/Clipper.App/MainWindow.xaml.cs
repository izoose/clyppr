using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Clipper.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window hides to tray so the replay buffer keeps running; real exit is via the tray "Quit".
        if (!App.Current.IsQuitting)
        {
            e.Cancel = true;
            App.Current.HideToTray();
        }
        base.OnClosing(e);
    }

    // Open the card's context menu on a normal left-click of the "⋯" button.
    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } btn)
        {
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }
    }
}
