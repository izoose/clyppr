using System.Windows;
using System.Windows.Controls;

namespace Clipper.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
