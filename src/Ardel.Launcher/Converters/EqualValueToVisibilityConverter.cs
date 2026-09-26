using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Ardel.Launcher.Converters;

public sealed class EqualValueToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (!TryGetInt(value, out var intVal) || !TryGetInt(parameter, out var paramVal))
            return Visibility.Collapsed;

        return intVal == paramVal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();

    private static bool TryGetInt(object? value, out int result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case long l:
                result = (int)l;
                return true;
            case string s when int.TryParse(s, out result):
                return true;
            case null:
                result = 0;
                return false;
            default:
                return int.TryParse(value.ToString(), out result);
        }
    }
}
