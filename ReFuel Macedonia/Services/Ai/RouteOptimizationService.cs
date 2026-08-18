using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services.Ai;

public interface IRouteOptimizationService
{
    Task<List<RoutePlanVm>> PlanAsync(DateTime? forDate = null, bool persist = false);
    Task<RoutePlanVm?> PlanForVehicleAsync(int deliveryVehicleId, List<int> orderIds, bool persist = false);
}

public class RouteOptimizationService : IRouteOptimizationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RouteOptimizationService> _logger;

    public RouteOptimizationService(ApplicationDbContext db, ILogger<RouteOptimizationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private const int ServiceMinutesPerStop = 12;

    public async Task<List<RoutePlanVm>> PlanAsync(DateTime? forDate = null, bool persist = false)
    {
        var trucks = await _db.DeliveryVehicles.Where(v => v.IsAvailable).ToListAsync();
        if (trucks.Count == 0) return new List<RoutePlanVm>();

        var openStatuses = new[] { OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Assigned, OrderStatus.OnTheWay };

        var orders = await _db.Orders
            .Include(o => o.FuelType)
            .Where(o => openStatuses.Contains(o.Status))
            .OrderBy(o => o.CreatedAtUtc)
            .Take(200)
            .ToListAsync();

        if (orders.Count == 0) return new List<RoutePlanVm>();

        var buckets = trucks.ToDictionary(t => t.Id, _ => new List<Order>());
        var load = trucks.ToDictionary(t => t.Id, _ => 0.0);

        foreach (var order in orders.OrderByDescending(o => o.Type == OrderType.Emergency))
        {
            var truck = trucks
                .Where(t => load[t.Id] + order.Litres <= t.CapacityLitres)
                .OrderBy(t => GeoUtils.RoadDistanceKm(t.BaseLatitude, t.BaseLongitude, order.DeliveryLatitude, order.DeliveryLongitude))
                .FirstOrDefault();

            if (truck is null) continue;   // fleet is full for today

            buckets[truck.Id].Add(order);
            load[truck.Id] += order.Litres;
        }

        var plans = new List<RoutePlanVm>();

        foreach (var truck in trucks)
        {
            var assigned = buckets[truck.Id];
            if (assigned.Count == 0) continue;

            var plan = BuildPlan(truck, assigned);
            plans.Add(plan);

            if (persist) await PersistAsync(truck, plan, forDate ?? DateTime.UtcNow);
        }

        _logger.LogInformation("Planned {Plans} routes for {Orders} open orders.", plans.Count, orders.Count);
        return plans.OrderByDescending(p => p.Stops.Count).ToList();
    }

    public async Task<RoutePlanVm?> PlanForVehicleAsync(int deliveryVehicleId, List<int> orderIds, bool persist = false)
    {
        var truck = await _db.DeliveryVehicles.FirstOrDefaultAsync(v => v.Id == deliveryVehicleId);
        if (truck is null) return null;

        var orders = await _db.Orders.Where(o => orderIds.Contains(o.Id)).ToListAsync();
        if (orders.Count == 0) return null;

        var plan = BuildPlan(truck, orders);
        if (persist) await PersistAsync(truck, plan, DateTime.UtcNow);
        return plan;
    }

    private RoutePlanVm BuildPlan(DeliveryVehicle truck, List<Order> orders)
    {
        var points = orders.Select(o => (o.Id, Lat: o.DeliveryLatitude, Lng: o.DeliveryLongitude)).ToList();

        var naive = TourLength(truck, orders.Select(o => o.Id).ToList(), points);

        var tour = NearestNeighbour(truck, points);
        tour = TwoOpt(truck, tour, points);
        tour = PrioritiseEmergencies(tour, orders);

        var optimised = TourLength(truck, tour, points);

        var plan = new RoutePlanVm
        {
            VehicleId = truck.Id,
            VehicleCode = truck.Code,
            Driver = truck.DriverName,
            Algorithm = "Nearest neighbour + 2-opt",
            TotalDistanceKm = Math.Round(optimised, 2),
            NaiveDistanceKm = Math.Round(naive, 2),
            ImprovementPercent = naive <= 0 ? 0 : Math.Round((naive - optimised) / naive * 100, 1),
            TotalLitres = Math.Round(orders.Sum(o => o.Litres), 1)
        };

        var cursorLat = truck.BaseLatitude;
        var cursorLon = truck.BaseLongitude;
        var cumulative = 0.0;
        var minutes = 0.0;
        var seq = 1;

        foreach (var orderId in tour)
        {
            var order = orders.First(o => o.Id == orderId);
            var leg = GeoUtils.RoadDistanceKm(cursorLat, cursorLon, order.DeliveryLatitude, order.DeliveryLongitude);
            cumulative += leg;
            minutes += GeoUtils.TravelMinutes(leg) + ServiceMinutesPerStop;

            plan.Stops.Add(new RouteStopVm
            {
                Sequence = seq++,
                OrderId = order.Id,
                Reference = order.Reference,
                Label = $"{order.DeliveryAddress}, {order.DeliveryCity}",
                Lat = order.DeliveryLatitude,
                Lng = order.DeliveryLongitude,
                LegDistanceKm = Math.Round(leg, 2),
                CumulativeKm = Math.Round(cumulative, 2),
                Eta = DateTime.UtcNow.AddMinutes(minutes),
                Litres = order.Litres
            });

            cursorLat = order.DeliveryLatitude;
            cursorLon = order.DeliveryLongitude;
        }

        plan.DurationMinutes = Math.Round(minutes + GeoUtils.TravelMinutes(
            GeoUtils.RoadDistanceKm(cursorLat, cursorLon, truck.BaseLatitude, truck.BaseLongitude)), 0);

        return plan;
    }

    private static double Distance(double lat1, double lon1, double lat2, double lon2)
        => GeoUtils.RoadDistanceKm(lat1, lon1, lat2, lon2);

    private static double TourLength(DeliveryVehicle truck, List<int> tour, List<(int Id, double Lat, double Lng)> points)
    {
        if (tour.Count == 0) return 0;

        var total = 0.0;
        var lat = truck.BaseLatitude;
        var lon = truck.BaseLongitude;

        foreach (var id in tour)
        {
            var p = points.First(x => x.Id == id);
            total += Distance(lat, lon, p.Lat, p.Lng);
            lat = p.Lat;
            lon = p.Lng;
        }

        total += Distance(lat, lon, truck.BaseLatitude, truck.BaseLongitude);
        return total;
    }

    private static List<int> NearestNeighbour(DeliveryVehicle truck, List<(int Id, double Lat, double Lng)> points)
    {
        var remaining = points.ToList();
        var tour = new List<int>();
        var lat = truck.BaseLatitude;
        var lon = truck.BaseLongitude;

        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestDistance = double.MaxValue;

            for (var i = 0; i < remaining.Count; i++)
            {
                var d = Distance(lat, lon, remaining[i].Lat, remaining[i].Lng);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestIndex = i;
                }
            }

            var next = remaining[bestIndex];
            tour.Add(next.Id);
            lat = next.Lat;
            lon = next.Lng;
            remaining.RemoveAt(bestIndex);
        }

        return tour;
    }

    private static List<int> TwoOpt(DeliveryVehicle truck, List<int> tour, List<(int Id, double Lat, double Lng)> points)
    {
        if (tour.Count < 4 || tour.Count > 60) return tour;

        var best = tour.ToList();
        var bestLength = TourLength(truck, best, points);
        var improved = true;
        var guard = 0;

        while (improved && guard++ < 50)
        {
            improved = false;

            for (var i = 0; i < best.Count - 1; i++)
            {
                for (var j = i + 1; j < best.Count; j++)
                {
                    var candidate = best.ToList();
                    candidate.Reverse(i, j - i + 1);

                    var length = TourLength(truck, candidate, points);
                    if (length < bestLength - 1e-9)
                    {
                        best = candidate;
                        bestLength = length;
                        improved = true;
                    }
                }
            }
        }

        return best;
    }

    private static List<int> PrioritiseEmergencies(List<int> tour, List<Order> orders)
    {
        var emergency = orders.Where(o => o.Type == OrderType.Emergency).Select(o => o.Id).ToHashSet();
        if (emergency.Count == 0) return tour;

        return tour.Where(id => emergency.Contains(id))
                   .Concat(tour.Where(id => !emergency.Contains(id)))
                   .ToList();
    }

    private async Task PersistAsync(DeliveryVehicle truck, RoutePlanVm plan, DateTime forDate)
    {
        var entity = new RoutePlan
        {
            DeliveryVehicleId = truck.Id,
            PlannedForUtc = forDate,
            TotalDistanceKm = plan.TotalDistanceKm,
            EstimatedDurationMinutes = plan.DurationMinutes,
            TotalLitres = plan.TotalLitres,
            ImprovementPercent = plan.ImprovementPercent,
            Algorithm = plan.Algorithm
        };

        foreach (var s in plan.Stops)
        {
            entity.Stops.Add(new RouteStop
            {
                OrderId = s.OrderId,
                Sequence = s.Sequence,
                Latitude = s.Lat,
                Longitude = s.Lng,
                LegDistanceKm = s.LegDistanceKm,
                EtaUtc = s.Eta,
                Label = s.Label
            });
        }

        _db.RoutePlans.Add(entity);
        await _db.SaveChangesAsync();
    }
}
