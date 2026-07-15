using System.Globalization;
using System.Windows.Data;

namespace RevitAPP.Views;

/// <summary>
///     Converter cho RadioButton bind enum: IsChecked = (value == parameter).
///     ConverterParameter là tên hằng enum (string).
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter != null
            ? Enum.Parse(targetType, parameter.ToString()!)
            : System.Windows.Data.Binding.DoNothing;
}
