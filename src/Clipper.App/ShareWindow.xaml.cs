using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Clipper.Core;

namespace Clipper.App;

public partial class ShareWindow : Window
{
    private readonly Clip _clip;
    private Point _dragStart;
    private bool _maybeDrag;

    public ShareWindow(Clip clip)
    {
        InitializeComponent();
        _clip = clip;

        TitleText.Text = clip.Title;
        DurationText.Text = clip.DurationText;
        try
        {
            if (!string.IsNullOrEmpty(clip.ThumbnailPath) && File.Exists(clip.ThumbnailPath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(clip.ThumbnailPath);
                bmp.EndInit();
                Thumb.Source = bmp;
            }
        }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- drag the clip file straight into Discord / desktop ----
    private void Drag_Down(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _maybeDrag = true;
    }

    private void Drag_Move(object sender, MouseEventArgs e)
    {
        if (!_maybeDrag || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < 6 && Math.Abs(p.Y - _dragStart.Y) < 6) return;
        _maybeDrag = false;
        if (!File.Exists(_clip.FilePath)) return;
        var data = new DataObject(DataFormats.FileDrop, new[] { _clip.FilePath });
        try { DragDrop.DoDragDrop(DragCard, data, DragDropEffects.Copy); } catch { }
    }

    // ---- primary actions ----
    private void CopyFile_Click(object sender, RoutedEventArgs e)
    {
        if (CopyFileToClipboard()) Status("Clip copied — paste it into Discord with Ctrl+V");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_clip.FilePath)) { Status("File not found"); return; }
        try { Process.Start("explorer.exe", $"/select,\"{_clip.FilePath}\""); } catch { }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_clip.FilePath)) { Status("File not found"); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Path.GetFileName(_clip.FilePath),
            Filter = "Video (*" + Path.GetExtension(_clip.FilePath) + ")|*" + Path.GetExtension(_clip.FilePath),
        };
        if (dlg.ShowDialog(this) == true)
        {
            try { File.Copy(_clip.FilePath, dlg.FileName, overwrite: true); Status("Saved a copy"); }
            catch (Exception ex) { Status("Save failed: " + ex.Message); }
        }
    }

    // ---- social: copy the clip, then open the platform so the user attaches it ----
    private void X_Click(object sender, RoutedEventArgs e)
    {
        CopyFileToClipboard();
        OpenUrl("https://twitter.com/intent/tweet?text=" + Uri.EscapeDataString(_clip.Title));
        Status("Clip copied — attach it in the X composer");
    }

    private void Reddit_Click(object sender, RoutedEventArgs e)
    {
        CopyFileToClipboard();
        OpenUrl("https://www.reddit.com/submit?title=" + Uri.EscapeDataString(_clip.Title));
        Status("Clip copied — attach it to your Reddit post");
    }

    private void YouTube_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.youtube.com/upload");
        if (File.Exists(_clip.FilePath))
        {
            try { Process.Start("explorer.exe", $"/select,\"{_clip.FilePath}\""); } catch { }
        }
        Status("Opened YouTube — drag the highlighted file into the uploader");
    }

    private void Discord_Click(object sender, RoutedEventArgs e)
    {
        CopyFileToClipboard();
        try { Process.Start(new ProcessStartInfo("discord://") { UseShellExecute = true }); } catch { }
        Status("Clip copied — paste into any Discord chat with Ctrl+V (or drag the preview in)");
    }

    // ---- helpers ----
    private bool CopyFileToClipboard()
    {
        if (!File.Exists(_clip.FilePath)) { Status("File not found"); return false; }
        try
        {
            var col = new StringCollection { _clip.FilePath };
            Clipboard.SetFileDropList(col);
            return true;
        }
        catch { return false; }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void Status(string msg) => StatusText.Text = msg;
}
