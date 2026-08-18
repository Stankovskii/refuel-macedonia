namespace ReFuel_Macedonia.Infrastructure;

public static class GeoUtils
{
    private const double EarthRadiusKm = 6371.0088;

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    public static double RoadDistanceKm(double lat1, double lon1, double lat2, double lon2, double detourFactor = 1.30)
        => HaversineKm(lat1, lon1, lat2, lon2) * detourFactor;

    public static double AverageSpeedKmh(double distanceKm) => distanceKm switch
    {
        < 5 => 25,    // dense urban
        < 20 => 40,   // urban + suburban
        < 60 => 65,   // regional roads
        _ => 85       // motorway
    };

    public static double TravelMinutes(double distanceKm)
        => distanceKm <= 0 ? 0 : distanceKm / AverageSpeedKmh(distanceKm) * 60.0;

    public static (double MinLat, double MinLon, double MaxLat, double MaxLon) BoundingBox(double lat, double lon, double radiusKm)
    {
        var dLat = radiusKm / 111.32;
        var dLon = radiusKm / (111.32 * Math.Cos(ToRadians(lat)));
        return (lat - dLat, lon - dLon, lat + dLat, lon + dLon);
    }

    private static double ToRadians(double deg) => deg * Math.PI / 180.0;

    public const double CountryCenterLat = 41.6086;
    public const double CountryCenterLon = 21.7453;

    public const double CountryMinLat = 40.83;
    public const double CountryMinLon = 20.44;
    public const double CountryMaxLat = 42.38;
    public const double CountryMaxLon = 23.05;

    public static bool IsInsideCountry(double lat, double lon)
        => lat >= CountryMinLat && lat <= CountryMaxLat && lon >= CountryMinLon && lon <= CountryMaxLon;
}
