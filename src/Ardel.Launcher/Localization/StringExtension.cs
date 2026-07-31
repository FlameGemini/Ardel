using Microsoft.UI.Xaml.Markup;

namespace Ardel.Launcher.Localization;

/// <summary>
/// XAML: <c>Text="{loc:String Key=Home_Tagline}"</c>
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class StringExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Loc.Get(Key);
}
