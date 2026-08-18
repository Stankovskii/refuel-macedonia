using System.Globalization;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Infrastructure;

public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Plain(int value, int decimals = 0) => value.ToString("F" + decimals, Inv);

    public static string Plain(double value, int decimals = 0) => value.ToString("F" + decimals, Inv);

    public static string Plain(decimal value, int decimals = 2) => value.ToString("F" + decimals, Inv);

    public static string N(int value, int decimals = 0) => value.ToString("N" + decimals, Inv);

    public static string N(decimal value, int decimals = 2) => value.ToString("N" + decimals, Inv);

    public static string N(double value, int decimals = 2) => value.ToString("N" + decimals, Inv);

    public static string Money(decimal value, int decimals = 2)
        => value.ToString("N" + decimals, Inv) + " ден";

    public static string PerUnit(decimal value, string unit)
        => value.ToString("N2", Inv) + " ден/" + (unit == "kg" ? "кг" : "л");

    public static string Change(decimal value)
        => (value > 0 ? "+" : "") + value.ToString("N2", Inv);

    public static string Litres(double value) => value.ToString("N1", Inv) + " л";

    public static string Km(double value) => value < 10
        ? value.ToString("N1", Inv) + " km"
        : value.ToString("N0", Inv) + " km";

    public static string Percent(double value, int decimals = 1)
        => value.ToString("N" + decimals, Inv) + " %";

    public static string Minutes(double value)
    {
        if (value < 60) return Math.Round(value) + " min";
        var h = (int)(value / 60);
        var m = (int)Math.Round(value - h * 60);
        return m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    public static string TrendArrow(string trend) => trend switch
    {
        "up" => "▲",
        "down" => "▼",
        _ => "▬"
    };

    public static string TrendClass(string trend) => trend switch
    {
        "up" => "trend-up",
        "down" => "trend-down",
        _ => "trend-flat"
    };

    public static string StatusClass(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "badge-pending",
        OrderStatus.Confirmed => "badge-confirmed",
        OrderStatus.Assigned => "badge-assigned",
        OrderStatus.OnTheWay => "badge-onway",
        OrderStatus.Delivered => "badge-delivered",
        OrderStatus.Cancelled => "badge-cancelled",
        _ => "badge-failed"
    };

    public static string StatusKey(OrderStatus status) => "order.status." + status.ToString().ToLowerInvariant();

    public static string OrderTypeKey(OrderType type) => "order.type." + type.ToString().ToLowerInvariant();

    public static string TierKey(LoyaltyTier tier) => "tier." + tier.ToString().ToLowerInvariant();

    public static string SourceKey(PriceSource source) => "source." + source.ToString().ToLowerInvariant();

    public static string LocalTime(DateTime utc, string format = "dd.MM.yyyy HH:mm")
        => ToLocal(utc).ToString(format, Inv);

    /// <summary>The inverse of ToLocal: a wall-clock time typed by a customer in North Macedonia, as UTC.</summary>
    public static DateTime ToUtc(DateTime local)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Central European Standard Time" : "Europe/Skopje");
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.SpecifyKind(local.AddHours(-2), DateTimeKind.Utc);
        }
        catch (InvalidTimeZoneException)
        {
            return DateTime.SpecifyKind(local.AddHours(-2), DateTimeKind.Utc);
        }
    }

    public static DateTime ToLocal(DateTime utc)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Central European Standard Time" : "Europe/Skopje");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        }
        catch (TimeZoneNotFoundException)
        {
            return utc.AddHours(2);
        }
        catch (InvalidTimeZoneException)
        {
            return utc.AddHours(2);
        }
    }
}
