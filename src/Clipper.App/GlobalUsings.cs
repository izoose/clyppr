// WinForms is enabled only for the tray icon (NotifyIcon). Pin the names that collide with
// WinForms to their WPF/Win32 versions so the rest of the app stays WPF by default.
global using Application = System.Windows.Application;
global using Clipboard = System.Windows.Clipboard;
global using MessageBox = System.Windows.MessageBox;
global using Button = System.Windows.Controls.Button;
global using UserControl = System.Windows.Controls.UserControl;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Point = System.Windows.Point;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using DataObject = System.Windows.DataObject;
global using DataFormats = System.Windows.DataFormats;
global using DragDrop = System.Windows.DragDrop;
global using DragDropEffects = System.Windows.DragDropEffects;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
