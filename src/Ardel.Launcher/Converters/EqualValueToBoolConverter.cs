using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Ardel.Launcher.Converters;

public sealed class EqualValueToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is string paramStr)
            return string.Equals(value?.ToString(), paramStr, StringComparison.OrdinalIgnoreCase);

        if (value is int intVal && TryParseParam(parameter, out int paramVal))
            return intVal == paramVal;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Only push when becoming checked; unchecked radios must not overwrite the value.
        if (value is not bool isChecked || !isChecked)
            return DependencyProperty.UnsetValue;

        if (parameter is string paramStr)
            return paramStr;

        if (TryParseParam(parameter, out int paramVal))
            return paramVal;
        return DependencyProperty.UnsetValue;
    }

    private static bool TryParseParam(object? parameter, out int value)
    {
        switch (parameter)
        {
            case int i:
                value = i;
                return true;
            case string s when int.TryParse(s, out value):
                return true;
            case null:
                value = 0;
                return false;
            default:
                return int.TryParse(parameter.ToString(), out value);
        }
    }
}
