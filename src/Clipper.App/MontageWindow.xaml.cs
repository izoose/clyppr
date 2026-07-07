using System.Windows;

namespace Clipper.App;

public partial class MontageWindow : Window
{
    public MontageWindow(MontageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
