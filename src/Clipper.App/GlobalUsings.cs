// WinForms is enabled only for the tray icon (NotifyIcon). Pin the names that collide with
// WinForms to their WPF/Win32 versions so the rest of the app stays WPF by default.
global using Application = System.Windows.Application;
global using Clipboard = System.Windows.Clipboard;
global using MessageBox = System.Windows.MessageBox;
global using Button = System.Windows.Controls.Button;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
