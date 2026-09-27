using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// English UI keeps App.xaml Consolas defaults. Non-English applies a
/// proportional stack to the shell only.
/// Never assign FontFamily into Application.Resources at runtime — that
/// stow-crashes unpackaged WinUI (0xC000027B). Static FontFamily fields
/// are equally unsafe during type init.
/// Greeting uses a separate display face applied on the control itself.
/// </summary>
internal static class AppTypography
{
    /// <summary>Packaged JetBrains Mono SemiBold — Latin greeting display only.</summary>
    private const string GreetingLatinFont =
        "ms-appx:///Assets/Fonts/JetBrainsMono-SemiBold.ttf#JetBrains Mono";

    public static void Apply(string languageTag)
    {
        // DirectWrite and App.xaml modern font stacks provide seamless typography across all languages.
        // We do not overwrite NavView.FontFamily or ContentFrame.FontFamily, as doing so breaks FontIcon symbol glyph inheritance.
    }

    /// <summary>
    /// Home greeting display face. Always the Latin JetBrains Mono used for English
    /// “Good morning” — applied on the TextBlock (not Application.Resources).
    /// </summary>
    public static FontFamily CreateGreetingFont(string? languageTag = null) =>
        new(GreetingLatinFont);

    public static FontWeight GreetingFontWeight(string? languageTag = null) => FontWeights.Normal;

    /// <summary>1/1000 em. Slight tracking for Latin display.</summary>
    public static int GreetingCharacterSpacing(string? languageTag = null) => 40;

    private static bool IsEnglish(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ||
        tag.StartsWith("en", StringComparison.OrdinalIgnoreCase);
}
