using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Infrastructure;

/// <summary>
/// What one delivery costs ReFuel to perform, split into the three things the business actually
/// pays for: the driver's time, the tanker's kilometres and the fixed cost of dispatching a job.
/// </summary>
public readonly record struct DeliveryCost(decimal Driver, decimal Tanker, decimal Dispatch, double KmDriven, double Hours, bool Grouped)
{
    public decimal Total => Driver + Tanker + Dispatch;

    public static DeliveryCost Zero => new(0, 0, 0, 0, 0, false);
}

/// <summary>
/// The single place where a delivery cost is estimated. Every screen — the quote, the profitability
/// report, the economics calculator, sales offers — uses this, so they can never disagree.
///
/// Two shapes:
///  • Dedicated trip — base → customer → base for one drop. Contract drops (a full compartment for
///    one depot) and emergency orders (a tanker leaves for that customer alone).
///  • Grouped drop — the order's share of a multi-stop round. Standard and scheduled orders, which
///    the dispatcher batches into routes. The out-and-back distance is shared by every stop on the
///    loop; the hop between stops and the time spent filling are charged in full.
/// </summary>
public static class DeliveryCosting
{
    public static DeliveryCost DedicatedTrip(PricingSettings settings, double distanceKm)
        => Compute(settings, distanceKm, stops: 1);

    public static DeliveryCost GroupedDrop(PricingSettings settings, double distanceKm)
        => Compute(settings, distanceKm, stops: Math.Max(1, settings.AverageStopsPerRoute));

    public static DeliveryCost For(PricingSettings settings, double distanceKm, bool grouped)
        => grouped ? GroupedDrop(settings, distanceKm) : DedicatedTrip(settings, distanceKm);

    /// <summary>Standard and scheduled orders ride on a shared route; emergencies and contract drops get their own tanker.</summary>
    public static bool IsGrouped(OrderType type) => type is OrderType.Standard or OrderType.Scheduled;

    private static DeliveryCost Compute(PricingSettings settings, double distanceKm, int stops)
    {
        var legs = settings.ChargeReturnLeg ? 2d : 1d;
        var speed = settings.AverageSpeedKph <= 0 ? 40 : settings.AverageSpeedKph;
        var distance = Math.Max(0, distanceKm);

        var km = distance * legs / stops + (stops > 1 ? Math.Max(0, settings.InterStopKm) : 0);
        var hours = km / speed + Math.Max(0, settings.ServiceMinutesPerStop) / 60d;

        var driver = settings.DriverCostPerHour * (decimal)hours;
        var tanker = settings.TankerCostPerKm * (decimal)km;
        var dispatch = settings.FixedDispatchCost / stops;

        return new DeliveryCost(
            Math.Round(driver, 2),
            Math.Round(tanker, 2),
            Math.Round(dispatch, 2),
            Math.Round(km, 1),
            Math.Round(hours, 2),
            stops > 1);
    }
}
