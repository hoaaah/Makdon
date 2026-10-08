using System.Globalization;
using System.Windows.Data;

namespace Makdon;

/// <summary>Menghubungkan RadioButton ke <see cref="ViewMode"/>; ConverterParameter = nama mode.</summary>
public sealed class ViewModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ViewMode mode && parameter is string name && Enum.TryParse<ViewMode>(name, out var wanted) && mode == wanted;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string name && Enum.TryParse<ViewMode>(name, out var wanted)
            ? wanted
            : Binding.DoNothing;
}
