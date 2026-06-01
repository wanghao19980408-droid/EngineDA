using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace EngineDA.Converts
{
    public class UnitToCategoryConverter : IValueConverter
    {
        private readonly Dictionary<string, string> _unitMap;

        public UnitToCategoryConverter(Dictionary<string, string> unitMap)
        {
            _unitMap = unitMap;
        }

        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string unit && _unitMap.TryGetValue(unit, out var category))
                return category;
            return "其他";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
