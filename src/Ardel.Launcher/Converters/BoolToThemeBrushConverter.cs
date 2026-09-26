using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Ardel.Launcher.Converters;

/// <summary>Picks a theme brush resource key based on a bool (e.g. language card selected).</summary>
public sealed class BoolToThemeBrushConverter : IValueConverter
{
    public string FalseResourceKey { get; set; } = string.Empty;

    public string TrueResourceKey { get; set; } = string.Empty;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var selected = value is true;
        var key = selected ? TrueResourceKey : FalseResourceKey;
        if (!string.IsNullOrEmpty(key) &&
            Application.Current.Resources.TryGetValue(key, out var resource) &&
            resource is Brush brush)
            return brush;

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
