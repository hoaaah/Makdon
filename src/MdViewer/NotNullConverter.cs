using System.Globalization;
using System.Windows.Data;

namespace MdViewer;

/// <summary>true bila nilai tidak null (dipakai untuk mengaktifkan kontrol saat ada tab).</summary>
public sealed class NotNullConverter : IValueConverter
{
    public static readonly NotNullConverter Instance = new();

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
