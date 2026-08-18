using System.ComponentModel.DataAnnotations;

namespace ReFuel_Macedonia.Models;

public class WeatherSnapshot
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string City { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public double TemperatureC { get; set; }
    public double FeelsLikeC { get; set; }
    public double WindKmh { get; set; }
    public int WindDirection { get; set; }
    public double PrecipitationMm { get; set; }
    public int Humidity { get; set; }
    public int WeatherCode { get; set; }
    public bool IsDay { get; set; }

    public double MinTemperatureC { get; set; }
    public double MaxTemperatureC { get; set; }

    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;

    public string Icon => WeatherCode switch
    {
        0 => IsDay ? "sun" : "moon",
        1 or 2 => "cloud-sun",
        3 => "cloud",
        45 or 48 => "fog",
        51 or 53 or 55 or 56 or 57 => "drizzle",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "rain",
        71 or 73 or 75 or 77 or 85 or 86 => "snow",
        95 or 96 or 99 => "storm",
        _ => "cloud"
    };

    public string DescriptionKey => WeatherCode switch
    {
        0 => "weather.clear",
        1 => "weather.mostlyclear",
        2 => "weather.partlycloudy",
        3 => "weather.overcast",
        45 or 48 => "weather.fog",
        51 or 53 or 55 or 56 or 57 => "weather.drizzle",
        61 or 63 or 80 or 81 => "weather.rain",
        65 or 82 => "weather.heavyrain",
        66 or 67 => "weather.freezingrain",
        71 or 73 or 85 => "weather.snow",
        75 or 86 => "weather.heavysnow",
        77 => "weather.snowgrains",
        95 => "weather.thunderstorm",
        96 or 99 => "weather.hailstorm",
        _ => "weather.unknown"
    };

    public string DrivingAdviceKey => WeatherCode switch
    {
        >= 95 => "weather.advice.storm",
        >= 71 and <= 86 => "weather.advice.snow",
        >= 61 and <= 67 => "weather.advice.rain",
        45 or 48 => "weather.advice.fog",
        _ => TemperatureC <= 2 ? "weather.advice.ice" : "weather.advice.ok"
    };

    public bool IsHazardous => WeatherCode >= 61 || TemperatureC <= 1 || WindKmh >= 55;
}

public class RoadCondition
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Road { get; set; } = string.Empty;

    [MaxLength(600)]
    public string Description { get; set; } = string.Empty;

    public RoadStatus Status { get; set; } = RoadStatus.Normal;

    [MaxLength(120)]
    public string? Region { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [MaxLength(200)]
    public string Source { get; set; } = "AMSM";

    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;

    public string StatusClass => Status switch
    {
        RoadStatus.Normal => "badge-ok",
        RoadStatus.Caution => "badge-warn",
        RoadStatus.Restricted => "badge-info",
        _ => "badge-err"
    };

    public string StatusKey => "road.status." + Status.ToString().ToLowerInvariant();
}

public class BorderCrossing
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Country { get; set; } = string.Empty;

    public int EntryWaitMinutes { get; set; }
    public int ExitWaitMinutes { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;

    public string WaitClass => Math.Max(EntryWaitMinutes, ExitWaitMinutes) switch
    {
        <= 15 => "badge-ok",
        <= 45 => "badge-warn",
        _ => "badge-err"
    };
}
