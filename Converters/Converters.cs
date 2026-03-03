using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ChatMapperApp.Models;

namespace ChatMapperApp.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return b ? Visibility.Visible : Visibility.Collapsed;
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return b ? Visibility.Collapsed : Visibility.Visible;
        if (value is int i) return i > 0 ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is Visibility.Collapsed;
}

public class MappedIndicatorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.FromRgb(34, 139, 34))   // ForestGreen
            : new SolidColorBrush(Color.FromRgb(180, 180, 180)); // LightGray
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class NodeTypeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NodeType nt)
        {
            return nt switch
            {
                NodeType.Root => "📋",
                NodeType.Object => "{ }",
                NodeType.Array => "[ ]",
                NodeType.Value => "=",
                NodeType.Attribute => "@",
                NodeType.Element => "< >",
                NodeType.CsvColumn => "▪",
                NodeType.Text => "—",
                _ => "·"
            };
        }
        return "·";
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class NodeTypeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NodeType nt)
        {
            return nt switch
            {
                NodeType.Array => new SolidColorBrush(Color.FromRgb(180, 120, 0)),   // DarkGoldenrod
                NodeType.Object or NodeType.Element => new SolidColorBrush(Color.FromRgb(90, 60, 160)),  // Purple
                NodeType.Value => new SolidColorBrush(Color.FromRgb(0, 100, 0)),      // DarkGreen
                NodeType.Attribute => new SolidColorBrush(Color.FromRgb(0, 80, 160)),  // Steel Blue
                NodeType.CsvColumn => new SolidColorBrush(Color.FromRgb(0, 80, 160)),
                _ => new SolidColorBrush(Colors.Black),
            };
        }
        return new SolidColorBrush(Colors.Black);
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
