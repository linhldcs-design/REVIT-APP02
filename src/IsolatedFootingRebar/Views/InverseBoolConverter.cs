using System.Globalization;
using System.Windows.Data;

namespace IsolatedFootingRebar.Views;

/// <summary>Đảo giá trị bool cho binding (vd radio "Segmented" = !Closed). Dùng qua x:Static Instance.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
