using System;
using System.Globalization;
using System.Windows.Data;

namespace TotalUpdater.Next.UI
{
    public sealed class FilterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return String.Equals(value as string, parameter as string, StringComparison.Ordinal); }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return value is bool && (bool)value ? parameter as string : Binding.DoNothing; }
    }
}
