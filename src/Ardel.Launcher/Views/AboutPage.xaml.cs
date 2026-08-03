using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText = FormatVersion();
    }

    public string VersionText { get; }

    private static string FormatVersion()
    {
        var asm = typeof(AboutPage).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString()
                   ?? "?";
        // Strip Source Link / commit suffix if present.
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            info = info[..plus];
        return Loc.Format(LocKeys.About_Version, info);
    }
}
