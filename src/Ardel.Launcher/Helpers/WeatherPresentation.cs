using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Helpers;

/// <summary>WMO weather interpretation codes → localized short labels + Segoe Fluent glyph.</summary>
internal static class WeatherPresentation
{
    public static string FormatTemperature(double celsiusOrFahrenheit, bool useFahrenheit) =>
        $"{Math.Round(celsiusOrFahrenheit):0}°{(useFahrenheit ? "F" : "C")}";

    public static string FormatDailyRange(double? high, double? low, bool useFahrenheit)
    {
        if (high is not { } hi || low is not { } lo)
            return string.Empty;

        return Loc.Format(
            LocKeys.Home_WeatherTodayRange,
            FormatTemperature(lo, useFahrenheit),
            FormatTemperature(hi, useFahrenheit));
    }

    public static string FormatConditionLine(
        string condition,
        double? high,
        double? low,
        bool useFahrenheit)
    {
        var range = FormatDailyRange(high, low, useFahrenheit);
        return string.IsNullOrEmpty(range)
            ? condition
            : Loc.Format(LocKeys.Home_WeatherConditionTodayRange, condition, range);
    }

    public static string ConditionLabel(int code, bool isDay) => code switch
    {
        0 => Loc.Get(isDay ? LocKeys.Weather_Clear : LocKeys.Weather_ClearNight),
        1 => Loc.Get(LocKeys.Weather_MainlyClear),
        2 => Loc.Get(LocKeys.Weather_PartlyCloudy),
        3 => Loc.Get(LocKeys.Weather_Overcast),
        45 or 48 => Loc.Get(LocKeys.Weather_Fog),
        51 or 53 or 55 => Loc.Get(LocKeys.Weather_Drizzle),
        56 or 57 => Loc.Get(LocKeys.Weather_FreezingDrizzle),
        61 or 63 or 65 => Loc.Get(LocKeys.Weather_Rain),
        66 or 67 => Loc.Get(LocKeys.Weather_FreezingRain),
        71 or 73 or 75 or 77 => Loc.Get(LocKeys.Weather_Snow),
        80 or 81 or 82 => Loc.Get(LocKeys.Weather_RainShowers),
        85 or 86 => Loc.Get(LocKeys.Weather_SnowShowers),
        95 => Loc.Get(LocKeys.Weather_Thunderstorm),
        96 or 99 => Loc.Get(LocKeys.Weather_ThunderstormHail),
        _ => Loc.Get(LocKeys.Weather_Unknown)
    };

    /// <summary>Segoe Fluent Icons glyph for the condition.</summary>
    public static string ConditionGlyph(int code, bool isDay) => code switch
    {
        0 => isDay ? "\uE706" : "\uE708", // Sunny / ClearNight
        1 or 2 => isDay ? "\uE708" : "\uE708",
        3 => "\uE753", // Cloud
        45 or 48 => "\uE753",
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "\uE759", // Rain
        71 or 73 or 75 or 77 or 85 or 86 => "\uE9C2", // Snow
        95 or 96 or 99 => "\uE9CA", // Thunderstorm-ish
        _ => "\uE9CE"
    };
}
