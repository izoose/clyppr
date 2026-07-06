using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Clipper.App;

/// <summary>System-tray icon so Clipper keeps buffering in the background like Medal.</summary>
public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly App _app;
    private readonly ToolStripMenuItem _bufferItem;

    public TrayManager(App app)
    {
        _app = app;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Clipper", null, (_, _) => _app.ShowMainWindow());
        menu.Items.Add("Save clip now", null, (_, _) => _app.Recording.SaveClip());
        _bufferItem = new ToolStripMenuItem("Buffer", null, (_, _) => ToggleBuffer());
        menu.Items.Add(_bufferItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => _app.QuitFromTray());
        menu.Opening += (_, _) => _bufferItem.Text = _app.Recording.BufferRunning ? "Buffer: On" : "Buffer: Off";

        _icon = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Clipper",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _app.ShowMainWindow();
    }

    private void ToggleBuffer()
    {
        if (_app.Recording.BufferRunning) _app.Recording.StopBuffer();
        else _app.Recording.StartBuffer();
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(3000);
    }

    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(0xF0, 0xC2, 0x4B));
            g.FillPolygon(brush, new[] { new Point(16, 2), new Point(30, 16), new Point(16, 30), new Point(2, 16) });
        }
        IntPtr h = bmp.GetHicon();
        return (Icon)Icon.FromHandle(h).Clone();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
