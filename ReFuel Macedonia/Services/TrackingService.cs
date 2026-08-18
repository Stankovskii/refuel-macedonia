using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services;

public interface ITrackingService
{
    Task<TrackingVm?> GetTrackingAsync(int orderId, string? userId, bool isAdmin = false);
    Task<DeliveryVehicle?> AssignDriverAsync(Order order);
}

public class TrackingService : ITrackingService
{
    private readonly ApplicationDbContext _db;

    public TrackingService(ApplicationDbContext db) => _db = db;

    public async Task<DeliveryVehicle?> AssignDriverAsync(Order order)
    {
        var fleet = await _db.DeliveryVehicles.Where(v => v.IsAvailable).ToListAsync();
        if (fleet.Count == 0) return null;

        var candidates = fleet
            .Where(v => v.CapacityLitres >= order.Litres)
            .ToList();

        if (candidates.Count == 0) candidates = fleet;

        var best = candidates
            .OrderBy(v => GeoUtils.RoadDistanceKm(v.BaseLatitude, v.BaseLongitude, order.DeliveryLatitude, order.DeliveryLongitude))
            .ThenByDescending(v => v.DriverRating)
            .First();

        order.DeliveryVehicleId = best.Id;
        return best;
    }

    public async Task<TrackingVm?> GetTrackingAsync(int orderId, string? userId, bool isAdmin = false)
    {
        var order = await _db.Orders
            .Include(o => o.FuelType)
            .Include(o => o.Station).ThenInclude(s => s!.Brand)
            .Include(o => o.DeliveryVehicle)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null) return null;
        if (!isAdmin && order.UserId != userId) return null;

        var driver = order.DeliveryVehicle;

        var originLat = order.Station?.Latitude ?? driver?.BaseLatitude ?? GeoUtils.CountryCenterLat;
        var originLon = order.Station?.Longitude ?? driver?.BaseLongitude ?? GeoUtils.CountryCenterLon;

        var progress = ComputeProgress(order);

        var currentLat = originLat + (order.DeliveryLatitude - originLat) * progress;
        var currentLon = originLon + (order.DeliveryLongitude - originLon) * progress;

        var remainingKm = GeoUtils.RoadDistanceKm(currentLat, currentLon, order.DeliveryLatitude, order.DeliveryLongitude);
        var remainingMinutes = order.Status switch
        {
            OrderStatus.Delivered => 0,
            OrderStatus.Cancelled => 0,
            OrderStatus.Arrived => 2,
            _ => Math.Max(1, Math.Round(GeoUtils.TravelMinutes(remainingKm) + StatusOverhead(order.Status), 0))
        };

        return new TrackingVm
        {
            Order = order,
            Driver = driver,
            OriginLat = originLat,
            OriginLng = originLon,
            CurrentLat = Math.Round(currentLat, 6),
            CurrentLng = Math.Round(currentLon, 6),
            DestinationLat = order.DeliveryLatitude,
            DestinationLng = order.DeliveryLongitude,
            ProgressPercent = (int)Math.Round(progress * 100),
            RemainingKm = Math.Round(remainingKm, 1),
            RemainingMinutes = remainingMinutes,
            EtaUtc = order.Status == OrderStatus.Delivered
                ? order.DeliveredAtUtc
                : DateTime.UtcNow.AddMinutes(remainingMinutes),
            Steps = BuildSteps(order)
        };
    }

    private static double StatusOverhead(OrderStatus status) => status switch
    {
        OrderStatus.Pending => 25,
        OrderStatus.Confirmed => 18,
        OrderStatus.Assigned => 10,
        _ => 0
    };

    private static double ComputeProgress(Order order)
    {
        if (order.Status == OrderStatus.Delivered) return 1.0;
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Failed) return 0;
        if (order.Status == OrderStatus.Arrived) return 0.97;

        if (order.Status != OrderStatus.OnTheWay)
        {
            return order.Status switch
            {
                OrderStatus.Pending => 0.02,
                OrderStatus.Confirmed => 0.06,
                _ => 0.12
            };
        }

        var dispatched = order.DispatchedAtUtc ?? order.CreatedAtUtc;
        var eta = order.EtaUtc ?? dispatched.AddMinutes(45);
        var total = (eta - dispatched).TotalMinutes;
        if (total <= 0) return 0.5;

        var elapsed = (DateTime.UtcNow - dispatched).TotalMinutes;
        return Math.Clamp(0.15 + elapsed / total * 0.8, 0.15, 0.95);
    }

    private static List<TrackingStepVm> BuildSteps(Order order)
    {
        var flow = new[]
        {
            OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Assigned,
            OrderStatus.OnTheWay, OrderStatus.Arrived, OrderStatus.Delivered
        };

        var history = order.StatusHistory.OrderBy(h => h.CreatedAtUtc).ToList();
        var currentIndex = Array.IndexOf(flow, order.Status);
        if (order.Status == OrderStatus.Cancelled) currentIndex = -1;

        var steps = new List<TrackingStepVm>();

        for (var i = 0; i < flow.Length; i++)
        {
            var entry = history.LastOrDefault(h => h.Status == flow[i]);
            steps.Add(new TrackingStepVm
            {
                Status = flow[i],
                Done = currentIndex >= i,
                Active = currentIndex == i,
                AtUtc = entry?.CreatedAtUtc
            });
        }

        return steps;
    }
}
