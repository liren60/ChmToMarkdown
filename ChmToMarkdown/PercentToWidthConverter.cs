using System;
using System.Globalization;
using System.Windows.Data;

namespace ChmToMarkdown
{
    public class PercentToWidthConverter : IValueConverter
    {
        public static readonly PercentToWidthConverter Instance = new();
        private const double TotalWidth = 120.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int pct)
                return Math.Max(0, TotalWidth * pct / 100.0);
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
