using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ardel.Launcher.Helpers;

public static class PersonalizationTemplateHelper
{
    public static void WireThemeSelection(ItemsRepeater repeater, Action<string> onSelect)
    {
        repeater.ElementPrepared += (_, e) =>
        {
            if (e.Element is not Button button)
                return;

            button.Click += (_, _) =>
            {
                if (button.Tag is string code)
                    onSelect(code);
            };
        };
    }
}
