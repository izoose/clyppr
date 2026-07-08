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

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Clip clip } fe && DataContext is MainViewModel vm)
            BuildClipMenu(clip, vm, fe).IsOpen = true;
    }

    private void Card_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Clip clip } fe && DataContext is MainViewModel vm)
        {
            BuildClipMenu(clip, vm, fe).IsOpen = true;
            e.Handled = true;
        }
    }

    // A rich Medal-style right-click menu, shared by the card and the ⋯ button.
    private static ContextMenu BuildClipMenu(Clip clip, MainViewModel vm, UIElement target)
    {
        vm.ContextClip = clip;
        var menu = new ContextMenu { PlacementTarget = target };

        AddItem(menu, "Copy Link", () => vm.ShareCommand.Execute(clip));
        AddItem(menu, "Open in Editor", () => vm.EditCommand.Execute(clip));
        AddItem(menu, "Play", () => vm.PlayCommand.Execute(clip));
        menu.Items.Add(new Separator());

        var fav = new MenuItem { Header = clip.IsFavorite ? "Unfavorite" : "Favorite", IsChecked = clip.IsFavorite };
        fav.Click += (_, _) => vm.ToggleFavoriteCommand.Execute(clip);
        menu.Items.Add(fav);

        var addTo = new MenuItem { Header = "Albums" };
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
        if (clip.AlbumId is not null)
        {
            var rem = new MenuItem { Header = "Remove from album" };
            rem.Click += (_, _) => vm.RemoveFromAlbumCommand.Execute(null);
            addTo.Items.Add(rem);
        }
        menu.Items.Add(addTo);

        AddItem(menu, "Hashtags…", () => vm.EditTagsCommand.Execute(clip));
        menu.Items.Add(new Separator());

        AddItem(menu, "Open file location", () => vm.OpenFolderCommand.Execute(clip));
        AddItem(menu, "Copy file path", () => vm.CopyPathCommand.Execute(clip));
        menu.Items.Add(new Separator());
        AddItem(menu, "Delete", () => vm.DeleteCommand.Execute(clip));
        return menu;
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

    // Clicking a clip's thumbnail opens the viewer — or toggles its tick in multi-select mode.
    private void Thumb_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Clip clip } || DataContext is not MainViewModel vm) return;
        if (vm.SelectionMode)
        {
            clip.IsSelected = !clip.IsSelected;
            vm.RefreshSelectionCount();
        }
        else vm.OpenViewerCommand.Execute(clip);
    }

    // "Add to album" in the multi-select action bar → pick an album.
    private void SelectionAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || DataContext is not MainViewModel vm) return;
        var menu = new ContextMenu { PlacementTarget = btn };
        foreach (var album in vm.Albums)
        {
            var a = album;
            var mi = new MenuItem { Header = a.Name };
            mi.Click += (_, _) => vm.AddSelectedToAlbumCommand.Execute(a);
            menu.Items.Add(mi);
        }
        if (vm.Albums.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No albums yet", IsEnabled = false });
        menu.IsOpen = true;
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
