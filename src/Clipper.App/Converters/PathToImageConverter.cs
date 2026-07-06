using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Clipper.App;

/// <summary>Loads a thumbnail file into a decoded, non-locking BitmapImage. Null → no image.</summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // decode now, don't lock the file
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
