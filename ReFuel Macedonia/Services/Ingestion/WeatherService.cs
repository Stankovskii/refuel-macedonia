using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Services.Ingestion;

public record WeatherResult(bool Success, int Updated, string Message);

public interface IWeatherService
{
    Task<WeatherResult> RefreshAsync(CancellationToken ct = default);
    Task<List<WeatherSnapshot>> GetLatestAsync(int take = 12);
    Task<WeatherSnapshot?> GetForCityAsync(string city);
    Task<WeatherSnapshot?> GetNearestAsync(double lat, double lon);
}

public class WeatherService : IWeatherService
{
    private static readonly (string City, double Lat, double Lon)[] TrackedCities =
    {
        ("Скопје", 41.9981, 21.4254),
        ("Битола", 41.0314, 21.3347),
        ("Куманово", 42.1322, 21.7144),
        ("Тетово", 42.0106, 20.9714),
        ("Прилеп", 41.3444, 21.5528),
        ("Охрид", 41.1231, 20.8016),
        ("Гостивар", 41.7964, 20.9083),
        ("Велес", 41.7156, 21.7756),
        ("Штип", 41.7458, 22.1958),
        ("Струмица", 41.4375, 22.6431),
        ("Кочани", 41.9167, 22.4128),
        ("Гевгелија", 41.1394, 22.5019),
        ("Крива Паланка", 42.2011, 22.3319),
        ("Дебар", 41.5247, 20.5272)
    };

    private readonly HttpClient _http;
    private readonly ApplicationDbContext _db;
    private readonly ReFuelOptions _options;
    private readonly ILogger<WeatherService> _logger;

    public WeatherService(HttpClient http, ApplicationDbContext db, IOptions<ReFuelOptions> options, ILogger<WeatherService> logger)
    {
        _http = http;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WeatherResult> RefreshAsync(CancellationToken ct = default)
    {
        if (!_options.EnableWeather)
            return new WeatherResult(false, 0, "weather.disabled");

        var latitudes = string.Join(',', TrackedCities.Select(c => c.Lat.ToString("F4", CultureInfo.InvariantCulture)));
        var longitudes = string.Join(',', TrackedCities.Select(c => c.Lon.ToString("F4", CultureInfo.InvariantCulture)));

        var url = $"{_options.WeatherApiUrl}?latitude={latitudes}&longitude={longitudes}" +
                  "&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day" +
                  "&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=Europe%2FSkopje";

        string payload;
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return new WeatherResult(false, 0, "weather.error.http");

            payload = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Weather provider unreachable.");
            return new WeatherResult(false, 0, "weather.error.network");
        }

        List<WeatherSnapshot> snapshots;
        try
        {
            snapshots = Parse(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Weather payload could not be parsed.");
            return new WeatherResult(false, 0, "weather.error.parse");
        }

        if (snapshots.Count == 0)
            return new WeatherResult(false, 0, "weather.error.empty");

        var existing = await _db.WeatherSnapshots.ToListAsync(ct);
        if (existing.Count > 0) _db.WeatherSnapshots.RemoveRange(existing);

        _db.WeatherSnapshots.AddRange(snapshots);
        await _db.SaveChangesAsync(ct);

        return new WeatherResult(true, snapshots.Count, "weather.ok");
    }

    private static List<WeatherSnapshot> Parse(string json)
    {
        var list = new List<WeatherSnapshot>();
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : new List<JsonElement> { root };

        for (var i = 0; i < items.Count && i < TrackedCities.Length; i++)
        {
            var element = items[i];
            if (!element.TryGetProperty("current", out var current)) continue;

            var city = TrackedCities[i];

            double GetDouble(JsonElement source, string name)
                => source.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

            double GetDaily(string name)
            {
                if (!element.TryGetProperty("daily", out var daily)) return 0;
                if (!daily.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return 0;
                foreach (var v in arr.EnumerateArray())
                    if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
                return 0;
            }

            list.Add(new WeatherSnapshot
            {
                City = city.City,
                Latitude = city.Lat,
                Longitude = city.Lon,
                TemperatureC = Math.Round(GetDouble(current, "temperature_2m"), 1),
                FeelsLikeC = Math.Round(GetDouble(current, "apparent_temperature"), 1),
                Humidity = (int)GetDouble(current, "relative_humidity_2m"),
                PrecipitationMm = Math.Round(GetDouble(current, "precipitation"), 1),
                WeatherCode = (int)GetDouble(current, "weather_code"),
                WindKmh = Math.Round(GetDouble(current, "wind_speed_10m"), 1),
                WindDirection = (int)GetDouble(current, "wind_direction_10m"),
                IsDay = GetDouble(current, "is_day") > 0.5,
                MaxTemperatureC = Math.Round(GetDaily("temperature_2m_max"), 1),
                MinTemperatureC = Math.Round(GetDaily("temperature_2m_min"), 1),
                ObservedAtUtc = DateTime.UtcNow
            });
        }

        return list;
    }

    public async Task<List<WeatherSnapshot>> GetLatestAsync(int take = 12)
        => await _db.WeatherSnapshots
                    .OrderByDescending(w => w.ObservedAtUtc)
                    .Take(Math.Max(1, take))
                    .ToListAsync();

    public async Task<WeatherSnapshot?> GetForCityAsync(string city)
        => await _db.WeatherSnapshots
                    .Where(w => w.City == city)
                    .OrderByDescending(w => w.ObservedAtUtc)
                    .FirstOrDefaultAsync();

    public async Task<WeatherSnapshot?> GetNearestAsync(double lat, double lon)
    {
        var all = await _db.WeatherSnapshots.ToListAsync();
        if (all.Count == 0) return null;

        return all.OrderBy(w => GeoUtils.HaversineKm(lat, lon, w.Latitude, w.Longitude)).First();
    }
}
