using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RevitAPP.Chat.Models;
using Color = System.Windows.Media.Color;
using Visibility = System.Windows.Visibility;

namespace RevitAPP.Chat.Views;

/// <summary>Căn phải cho tin nhắn user, trái cho assistant/tool.</summary>
public sealed class RoleAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value as string == ChatBubble.User ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Đỏ khi IsError, ngược lại màu chữ hệ thống.</summary>
public sealed class ErrorBrushConverter : IValueConverter
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? ErrorBrush : SystemColors.ControlTextBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → Visibility (true=Visible, false=Collapsed).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Đảo bool (dùng để disable input khi busy).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}
