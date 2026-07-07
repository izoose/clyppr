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
            Icon = LoadIcon(),
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

    private static Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/clipper.ico"));
            if (info?.Stream is { } s) return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
