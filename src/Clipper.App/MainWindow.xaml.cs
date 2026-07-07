using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clipper.Core;

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
        if (!App.Current.IsQuitting)
        {
            e.Cancel = true;
            App.Current.HideToTray();
        }
        base.OnClosing(e);
    }

    // Builds the clip "⋯" menu dynamically so the album list is current.
    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || DataContext is not MainViewModel vm || btn.DataContext is not Clip clip) return;
        vm.ContextClip = clip;

        var menu = new ContextMenu { PlacementTarget = btn };

        var addTo = new MenuItem { Header = "Add to album" };
        foreach (var album in vm.Albums)
        {
            var a = album;
            var mi = new MenuItem { Header = a.Name, IsChecked = clip.AlbumId == a.Id };
            mi.Click += (_, _) => vm.AddToAlbumCommand.Execute(a);
            addTo.Items.Add(mi);
        }
        if (vm.Albums.Count > 0) addTo.Items.Add(new Separator());
        var newAlbum = new MenuItem { Header = "New album…" };
        newAlbum.Click += (_, _) => vm.AddToNewAlbumCommand.Execute(null);
        addTo.Items.Add(newAlbum);
        menu.Items.Add(addTo);

        if (clip.AlbumId is not null)
        {
            var remove = new MenuItem { Header = "Remove from album" };
            remove.Click += (_, _) => vm.RemoveFromAlbumCommand.Execute(null);
            menu.Items.Add(remove);
        }
        menu.Items.Add(new Separator());

        AddItem(menu, "Play", () => vm.PlayCommand.Execute(clip));
        AddItem(menu, "Edit", () => vm.EditCommand.Execute(clip));
        AddItem(menu, "Open file location", () => vm.OpenFolderCommand.Execute(clip));
        AddItem(menu, "Copy file path", () => vm.CopyPathCommand.Execute(clip));
        menu.Items.Add(new Separator());
        AddItem(menu, "Delete", () => vm.DeleteCommand.Execute(clip));

        menu.IsOpen = true;
    }

    private void AlbumChip_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Button btn || DataContext is not MainViewModel vm || btn.DataContext is not AlbumViewModel album) return;
        var menu = new ContextMenu { PlacementTarget = btn };
        AddItem(menu, "Rename", () => vm.RenameAlbumCommand.Execute(album));
        AddItem(menu, "Delete album", () => vm.DeleteAlbumCommand.Execute(album));
        menu.IsOpen = true;
    }

    private static void AddItem(ContextMenu menu, string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        menu.Items.Add(mi);
    }

    // ---- hover-to-preview ----

    private static readonly string[] VideoExts = { ".mp4", ".mkv", ".mov", ".webm", ".avi" };

    private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Clip clip) return;
        if (!VideoExts.Contains(Path.GetExtension(clip.FilePath).ToLowerInvariant())) return;
        var preview = FindChild<MediaElement>(fe);
        if (preview is null || !File.Exists(clip.FilePath)) return;
        preview.Source = new Uri(clip.FilePath);
        preview.Visibility = Visibility.Visible;
        preview.Position = TimeSpan.Zero;
        preview.Play();
    }

    private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var preview = FindChild<MediaElement>(fe);
        if (preview is null) return;
        preview.Stop();
        preview.Visibility = Visibility.Collapsed;
        preview.Source = null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}
