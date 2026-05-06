using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Player.App;

/// <summary>
/// 把布尔值转换为可见性，供“已播放”徽记这类轻量状态使用。
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is bool flag && flag;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility == Visibility.Visible;
    }
}
