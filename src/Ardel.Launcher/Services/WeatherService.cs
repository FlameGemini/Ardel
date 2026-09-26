using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Ardel.Launcher.Services;

/// <summary>Open-Meteo geocoding + current weather (no API key).</summary>
public sealed class WeatherService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(4),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.All
        };

        var http = new HttpClient(handler)
        {
            Timeout = RequestTimeout,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ArdelLauncher/1.0");
        return http;
    }

    public async Task<IReadOnlyList<WeatherPlace>> SearchPlacesAsync(
        string query,
        string languageTag,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var lang = MapLanguage(languageTag);
        var url =
            "https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={Uri.EscapeDataString(query.Trim())}" +
            $"&count=8&language={lang}&format=json";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);
        using var response = await Http.GetAsync(url, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content
            .ReadFromJsonAsync<GeoSearchResponse>(cancellationToken: cts.Token)
            .ConfigureAwait(false);

        if (payload?.Results is not { Count: > 0 })
            return [];

        return payload.Results
            .Where(r => r is not null)
            .Select(r => new WeatherPlace(
                r!.Name ?? query.Trim(),
                r.Admin1,
                r.Country,
                r.CountryCode,
                r.Latitude,
                r.Longitude,
                r.Timezone,
                FormatDisplay(r)))
            .ToList();
    }

    public async Task<WeatherSnapshot?> GetCurrentAsync(
        double latitude,
        double longitude,
        bool useFahrenheit,
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        var unit = useFahrenheit ? "fahrenheit" : "celsius";
        var tz = string.IsNullOrWhiteSpace(timezone) ? "auto" : timezone.Trim();
        var url =
            "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&longitude={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,weather_code,is_day" +
            "&daily=temperature_2m_max,temperature_2m_min" +
            $"&temperature_unit={unit}" +
            $"&timezone={Uri.EscapeDataString(tz)}";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);
            using var response = await Http.GetAsync(url, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content
                .ReadFromJsonAsync<ForecastResponse>(cancellationToken: cts.Token)
                .ConfigureAwait(false);

            if (payload?.Current is null)
                return null;

            double? dailyHigh = null;
            double? dailyLow = null;
            if (payload.Daily?.Temperature2mMax is { Count: > 0 } highs &&
                payload.Daily.Temperature2mMin is { Count: > 0 } lows)
            {
                dailyHigh = highs[0];
                dailyLow = lows[0];
            }

            return new WeatherSnapshot(
                payload.Current.Temperature2m,
                (int)Math.Round(payload.Current.WeatherCode),
                payload.Current.IsDay == 1,
                useFahrenheit,
                payload.Timezone,
                dailyHigh,
                dailyLow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out — treat as soft failure for the widget.
            return null;
        }
    }

    public async Task<WeatherPlace?> ReverseGeocodeAsync(
        double latitude,
        double longitude,
        string languageTag,
        CancellationToken cancellationToken = default)
    {
        var lang = MapLanguage(languageTag);
        var url =
            "https://nominatim.openstreetmap.org/reverse" +
            $"?lat={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&lon={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&format=json&accept-language={Uri.EscapeDataString(lang)}&zoom=10";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "ArdelLauncher/1.0 (weather widget)");

        using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content
            .ReadFromJsonAsync<NominatimReverseResponse>(cancellationToken: cts.Token)
            .ConfigureAwait(false);

        if (payload is null)
            return null;

        var name = payload.Address?.City
                   ?? payload.Address?.Town
                   ?? payload.Address?.County
                   ?? payload.Address?.State
                   ?? payload.Name
                   ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new WeatherPlace(
            name.Trim(),
            payload.Address?.State,
            payload.Address?.Country,
            payload.Address?.CountryCode?.ToUpperInvariant(),
            latitude,
            longitude,
            null,
            string.IsNullOrWhiteSpace(payload.DisplayName) ? name.Trim() : payload.DisplayName.Trim());
    }

    private static string MapLanguage(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return "en";

        // Open-Meteo geocoding only accepts two-letter ISO codes.
        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh";
        if (tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return "ja";
        if (tag.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
            return "fr";
        if (tag.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            return "es";
        if (tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return "ko";
        if (tag.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            return "de";
        if (tag.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            return "pt";
        if (tag.StartsWith("it", StringComparison.OrdinalIgnoreCase))
            return "it";
        if (tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            return "ru";
        return "en";
    }

    private static string FormatDisplay(GeoResult r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Name))
            parts.Add(r.Name!);
        if (!string.IsNullOrWhiteSpace(r.Admin1) &&
            !string.Equals(r.Admin1, r.Name, StringComparison.OrdinalIgnoreCase))
            parts.Add(r.Admin1!);
        if (!string.IsNullOrWhiteSpace(r.Country))
        {
            var country = r.Country!;
            if (!string.IsNullOrWhiteSpace(r.CountryCode))
                country += $" ({r.CountryCode})";
            parts.Add(country);
        }

        return string.Join(", ", parts);
    }

    private sealed class GeoSearchResponse
    {
        [JsonPropertyName("results")]
        public List<GeoResult>? Results { get; set; }
    }

    private sealed class GeoResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("admin1")]
        public string? Admin1 { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }

    private sealed class ForecastResponse
    {
        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }

        [JsonPropertyName("current")]
        public CurrentWeather? Current { get; set; }

        [JsonPropertyName("daily")]
        public DailyWeather? Daily { get; set; }
    }

    private sealed class DailyWeather
    {
        [JsonPropertyName("temperature_2m_max")]
        public List<double>? Temperature2mMax { get; set; }

        [JsonPropertyName("temperature_2m_min")]
        public List<double>? Temperature2mMin { get; set; }
    }

    private sealed class CurrentWeather
    {
        [JsonPropertyName("temperature_2m")]
        public double Temperature2m { get; set; }

        [JsonPropertyName("weather_code")]
        public double WeatherCode { get; set; }

        [JsonPropertyName("is_day")]
        public int IsDay { get; set; }
    }

    private sealed class NominatimReverseResponse
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("address")]
        public NominatimAddress? Address { get; set; }
    }

    private sealed class NominatimAddress
    {
        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("town")]
        public string? Town { get; set; }

        [JsonPropertyName("county")]
        public string? County { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }
    }
}

public sealed record WeatherPlace(
    string Name,
    string? Admin1,
    string? Country,
    string? CountryCode,
    double Latitude,
    double Longitude,
    string? Timezone,
    string DisplayName);

public sealed record WeatherSnapshot(
    double Temperature,
    int WeatherCode,
    bool IsDay,
    bool UseFahrenheit,
    string? Timezone,
    double? DailyHigh = null,
    double? DailyLow = null);
