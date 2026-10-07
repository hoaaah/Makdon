using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MdViewer;

/// <summary>Label mode tampilan untuk status bar.</summary>
public sealed class ViewModeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ViewMode.Edit => "Editor",
        ViewMode.Split => "Terpisah",
        ViewMode.Preview => "Pratinjau",
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible bila ada tab dan mode bukan Pratinjau (toolbar format hanya relevan saat editor tampak).</summary>
public sealed class EditorVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is ViewMode mode && mode != ViewMode.Preview ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Memecah path menjadi bagian untuk daftar berkas terakhir. Parameter: Name, MenuName (garis bawah digandakan), Directory.</summary>
public sealed class PathPartConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return "";
        return (parameter as string) switch
        {
            "Directory" => Path.GetDirectoryName(path) ?? "",
            // Di header menu, "_" adalah penanda access key; gandakan agar tampil apa adanya.
            "MenuName" => Path.GetFileName(path).Replace("_", "__"),
            _ => Path.GetFileName(path),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
