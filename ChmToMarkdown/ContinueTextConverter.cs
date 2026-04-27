using System;
using System.Globalization;
using System.Windows.Data;

namespace ChmToMarkdown
{
    public class ContinueTextConverter : IValueConverter
    {
        public static readonly ContinueTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "继续转换" : "开始转换";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
