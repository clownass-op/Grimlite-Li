using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Grimoire.Tools.Wiki
{
    public class DepthToIndentConverter : IValueConverter
    {
        private const double IndentPerLevel = 20;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int depth = value is int i ? i : 0;
            return new Thickness(4 + (depth * IndentPerLevel), 2, 0, 2);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
