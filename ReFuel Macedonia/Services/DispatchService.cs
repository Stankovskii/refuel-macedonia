using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services.Ai;

namespace ReFuel_Macedonia.Services;

public interface IDispatchService
{
    Task<DispatchVm> GetBoardAsync(bool buildRoutes = true);
    Task<List<ZoneRouteVm>> BuildZoneRoutesAsync(bool persist = false);
    Task<bool> AssignAsync(int orderId, int tankerId);
}

public class DispatchService : IDispatchService
{
    private readonly ApplicationDbContext _db;
    private readonly IPricingService _pricing;
    private readonly IFleetService _fleet;
    private readonly IRouteOptimizationService _routes;
    private readonly ILogger<DispatchService> _logger;

    private static readonly OrderStatus[] OpenStatuses =
    {
        OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Assigned, OrderStatus.OnTheWay, OrderStatus.Arrived
    };

    public DispatchService(
        ApplicationDbContext db,
        IPricingService pricing,
        IFleetService fleet,
        IRouteOptimizationService routes,
        ILogger<DispatchService> logger)
    {
        _db = db;
        _pricing = pricing;
        _fleet = fleet;
        _routes = routes;
        _logger = logger;
    }

    public async Task<DispatchVm> GetBoardAsync(bool buildRoutes = true)
    {
        var queue = await BuildQueueAsync();

        var vm = new DispatchVm
        {
            Queue = queue,
            Tankers = await _fleet.GetTankersAsync(),
            Zones = await _pricing.GetZonesAsync(),
            EmergencyCount = queue.Count(q => q.Priority == DeliveryPriority.Emergency),
            ScheduledCount = queue.Count(q => q.Priority == DeliveryPriority.Scheduled),
            ContractCount = queue.Count(q => q.Priority == DeliveryPriority.ContractFleet),
            StandardCount = queue.Count(q => q.Priority == DeliveryPriority.Standard),
            TotalLitres = Math.Round(queue.Sum(q => q.Order.Litres), 1),
            OpenAnomalies = await _db.FuelAnomalies.CountAsync(a => a.Status == AnomalyStatus.Open)
        };

        if (buildRoutes) vm.Routes = await BuildRoutesAsync(queue, vm.Tankers, vm.Zones, persist: false);

        return vm;
    }

    public async Task<List<ZoneRouteVm>> BuildZoneRoutesAsync(bool persist = false)
    {
        var queue = await BuildQueueAsync();
        var tankers = await _fleet.GetTankersAsync();
        var zones = await _pricing.GetZonesAsync();

        return await BuildRoutesAsync(queue, tankers, zones, persist);
    }

    private async Task<List<DispatchItemVm>> BuildQueueAsync()
    {
        var orders = await _db.Orders
            .Include(o => o.FuelType)
            .Include(o => o.User)
            .Include(o => o.BusinessAccount)
            .Include(o => o.DeliveryVehicle)
            .Where(o => OpenStatuses.Contains(o.Status))
            .OrderBy(o => o.CreatedAtUtc)
            .Take(250)
            .ToListAsync();

        if (orders.Count == 0) return new List<DispatchItemVm>();

        var zones = await _pricing.GetZonesAsync();
        var items = new List<DispatchItemVm>();

        foreach (var order in orders)
        {
            var zone = zones.FirstOrDefault(z => z.Id == order.DeliveryZoneId)
                       ?? MatchZone(zones, order.DeliveryLatitude, order.DeliveryLongitude, order.DeliveryCity);

            items.Add(new DispatchItemVm
            {
                Order = order,
                Priority = order.Priority,
                ZoneId = zone?.Id,
                ZoneName = zone?.Name,
                ZoneColor = zone?.ColorHex ?? "#8A93A3",
                WaitingMinutes = (int)Math.Max(0, (DateTime.UtcNow - order.CreatedAtUtc).TotalMinutes)
            });
        }

        return items
            .OrderBy(i => (int)i.Priority)
            .ThenBy(i => i.Order.ScheduledForUtc ?? i.Order.CreatedAtUtc)
            .ToList();
    }

    private static DeliveryZone? MatchZone(List<DeliveryZone> zones, double latitude, double longitude, string? city)
    {
        if (zones.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(city))
        {
            var byCity = zones.FirstOrDefault(z =>
                z.CityList.Any(c => string.Equals(c, city, StringComparison.OrdinalIgnoreCase)));

            if (byCity is not null) return byCity;
        }

        DeliveryZone? best = null;
        var bestDistance = double.MaxValue;

        foreach (var zone in zones)
        {
            var distance = GeoUtils.HaversineKm(latitude, longitude, zone.CenterLatitude, zone.CenterLongitude);
            if (distance > zone.RadiusKm || distance >= bestDistance) continue;

            best = zone;
            bestDistance = distance;
        }

        return best;
    }

    private async Task<List<ZoneRouteVm>> BuildRoutesAsync(
        List<DispatchItemVm> queue, List<TankerVm> tankers, List<DeliveryZone> zones, bool persist)
    {
        var routes = new List<ZoneRouteVm>();
        if (queue.Count == 0 || tankers.Count == 0) return routes;

        var usedTankers = new HashSet<int>();

        var groups = queue
            .GroupBy(q => q.ZoneId)
            .OrderBy(g => g.Min(i => (int)i.Priority))
            .ToList();

        foreach (var group in groups)
        {
            var zone = zones.FirstOrDefault(z => z.Id == group.Key);

            var route = new ZoneRouteVm
            {
                ZoneId = group.Key,
                ZoneName = zone?.Name ?? group.First().ZoneName ?? "—",
                ZoneColor = zone?.ColorHex ?? "#8A93A3"
            };

            var candidates = tankers
                .Where(t => !t.Tanker.IsInMaintenance && t.Tanker.IsAvailable && !usedTankers.Contains(t.Tanker.Id))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = tankers
                    .Where(t => !t.Tanker.IsInMaintenance && !usedTankers.Contains(t.Tanker.Id))
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                route.Warnings.Add("dispatch.warn.notanker");
                routes.Add(route);
                continue;
            }

            var centreLat = zone?.CenterLatitude ?? group.Average(i => i.Order.DeliveryLatitude);
            var centreLng = zone?.CenterLongitude ?? group.Average(i => i.Order.DeliveryLongitude);

            var tanker = candidates
                .OrderByDescending(t => t.Tanker.DeliveryZoneId == group.Key)
                .ThenBy(t => GeoUtils.RoadDistanceKm(
                    t.Tanker.BaseLatitude, t.Tanker.BaseLongitude, centreLat, centreLng))
                .First();

            usedTankers.Add(tanker.Tanker.Id);
            route.Tanker = tanker.Tanker;

            var available = tanker.Compartments
                .Where(c => c.FuelTypeId.HasValue)
                .GroupBy(c => c.FuelTypeId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.CurrentLitres));

            var noCompartments = tanker.Compartments.Count == 0;
            var loadLimit = noCompartments ? tanker.Tanker.CapacityLitres : double.MaxValue;
            var loaded = 0d;

            foreach (var item in group.OrderBy(i => (int)i.Priority).ThenBy(i => i.Order.CreatedAtUtc))
            {
                var fuelId = item.Order.FuelTypeId;
                var litres = item.Order.Litres;

                if (noCompartments)
                {
                    if (loaded + litres > loadLimit)
                    {
                        item.StockAvailable = false;
                        continue;
                    }

                    loaded += litres;
                }
                else
                {
                    var stock = available.TryGetValue(fuelId, out var value) ? value : 0;

                    if (stock < litres)
                    {
                        item.StockAvailable = false;
                        continue;
                    }

                    available[fuelId] = stock - litres;
                }

                route.Stops.Add(item);
            }

            var skipped = group.Count(i => !i.StockAvailable);
            if (skipped > 0) route.Warnings.Add("dispatch.warn.stock");

            if (route.Stops.Count == 0)
            {
                route.ExceedsCapacity = true;
                routes.Add(route);
                continue;
            }

            var plan = await _routes.PlanForVehicleAsync(
                tanker.Tanker.Id, route.Stops.Select(s => s.Order.Id).ToList(), persist);

            if (plan is not null)
            {
                route.TotalDistanceKm = plan.TotalDistanceKm;
                route.NaiveDistanceKm = plan.NaiveDistanceKm;
                route.ImprovementPercent = plan.ImprovementPercent;
                route.EstimatedMinutes = plan.DurationMinutes;
                route.TotalLitres = plan.TotalLitres;

                var order = plan.Stops.ToDictionary(s => s.OrderId, s => s.Sequence);

                route.Stops = route.Stops
                    .OrderBy(s => order.TryGetValue(s.Order.Id, out var sequence) ? sequence : int.MaxValue)
                    .ToList();
            }
            else
            {
                route.TotalLitres = Math.Round(route.Stops.Sum(s => s.Order.Litres), 1);
            }

            routes.Add(route);
        }

        if (persist)
            _logger.LogInformation("Persisted {Count} zone route(s).", routes.Count(r => r.Stops.Count > 0));

        return routes;
    }

    public async Task<bool> AssignAsync(int orderId, int tankerId)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return false;

        var tanker = await _db.DeliveryVehicles
            .Include(t => t.Compartments)
            .FirstOrDefaultAsync(t => t.Id == tankerId);

        if (tanker is null) return false;

        order.DeliveryVehicleId = tanker.Id;

        if (order.Status is OrderStatus.Pending or OrderStatus.Confirmed)
            order.Status = OrderStatus.Assigned;

        order.DispatchedAtUtc ??= DateTime.UtcNow;

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            Note = "order.history.assigned"
        });

        await _db.SaveChangesAsync();
        return true;
    }
}
