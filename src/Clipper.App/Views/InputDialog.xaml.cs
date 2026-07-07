using System.Windows;
using System.Windows.Input;

namespace Clipper.App;

public partial class InputDialog : Window
{
    public string Value => Input.Text.Trim();

    public InputDialog(string prompt, string initial = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; Close(); }
        else if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    /// <summary>Shows the dialog; returns the trimmed text or null if cancelled/empty.</summary>
    public static string? Ask(Window? owner, string prompt, string initial = "")
    {
        var dlg = new InputDialog(prompt, initial) { Owner = owner };
        if (dlg.ShowDialog() == true && dlg.Value.Length > 0) return dlg.Value;
        return null;
    }
}
