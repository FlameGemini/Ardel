using Windows.Devices.Geolocation;

namespace Ardel.Launcher.Helpers;

internal readonly record struct GeoCoordinates(double Latitude, double Longitude);

internal static class WeatherGeolocation
{
    public static async Task<GeoCoordinates?> TryGetCurrentPositionAsync(
        CancellationToken cancellationToken = default)
    {
        var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (access != GeolocationAccessStatus.Allowed)
            return null;

        var geolocator = new Geolocator
        {
            DesiredAccuracy = PositionAccuracy.Default,
            DesiredAccuracyInMeters = 5000
        };

        var position = await geolocator
            .GetGeopositionAsync(maximumAge: TimeSpan.FromMinutes(30), timeout: TimeSpan.FromSeconds(12))
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var coordinate = position?.Coordinate;
        if (coordinate is null)
            return null;

        return new GeoCoordinates(coordinate.Latitude, coordinate.Longitude);
    }
}
