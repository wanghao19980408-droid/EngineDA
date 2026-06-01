using System;
using System.Globalization;
using System.Windows.Data;

namespace EngineDA.Converts
{
    /// <summary>
    /// 布尔值转字符串转换器
    /// 参数格式: "TrueValue|FalseValue"
    /// </summary>
    public class BoolToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not bool boolValue || parameter is not string param)
                return string.Empty;

            var parts = param.Split('|');
            if (parts.Length != 2)
                return string.Empty;

            return boolValue ? parts[0] : parts[1];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}