using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services;

public interface IFleetService
{
    Task<TankerTelemetry?> RecordTelemetryAsync(int tankerId, TelemetryInput input);
    Task<List<TankerVm>> GetTankersAsync();
    Task<TankerVm?> GetTankerAsync(int id);
    Task<List<TankerTelemetry>> GetTelemetryAsync(int tankerId, int take = 60);
    Task<int> SimulateTickAsync();
    Task<int> DetectAnomaliesAsync();
    Task<bool> SetCompartmentAsync(int compartmentId, double litres, int? fuelTypeId);
    Task<int> RefillTankerAsync(int tankerId);
    Task<bool> ToggleMaintenanceAsync(int tankerId);
}

public class FleetService : IFleetService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<FleetService> _logger;

    private const double VarianceThreshold = 0.08;
    private const double MeterToleranceLitres = 2.5;
    private const double InventoryToleranceLitres = 60;

    public FleetService(ApplicationDbContext db, ILogger<FleetService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TankerTelemetry?> RecordTelemetryAsync(int tankerId, TelemetryInput input)
    {
        var tanker = await _db.DeliveryVehicles
            .Include(t => t.Compartments)
            .FirstOrDefaultAsync(t => t.Id == tankerId);

        if (tanker is null) return null;

        var recordedAt = input.RecordedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        if (recordedAt > DateTime.UtcNow.AddMinutes(5)) recordedAt = DateTime.UtcNow;

        var telemetry = new TankerTelemetry
        {
            DeliveryVehicleId = tanker.Id,
            RecordedAtUtc = recordedAt,
            Latitude = Math.Round(input.Latitude, 6),
            Longitude = Math.Round(input.Longitude, 6),
            SpeedKph = Math.Clamp(input.SpeedKph, 0, 200),
            HeadingDegrees = Math.Clamp(input.HeadingDegrees, 0, 360),
            LitresOnBoard = Math.Max(0, input.LitresOnBoard),
            OdometerKm = Math.Max(0, input.OdometerKm),
            PumpActive = input.PumpActive,
            EngineOn = input.EngineOn,
            Source = input.Source,
            Note = input.Note
        };

        _db.TankerTelemetries.Add(telemetry);

        if (GeoUtils.IsInsideCountry(telemetry.Latitude, telemetry.Longitude))
        {
            tanker.CurrentLatitude = telemetry.Latitude;
            tanker.CurrentLongitude = telemetry.Longitude;
        }

        tanker.SpeedKph = telemetry.SpeedKph;
        tanker.PumpActive = telemetry.PumpActive;
        tanker.LastTelemetryUtc = telemetry.RecordedAtUtc;

        if (telemetry.OdometerKm > tanker.OdometerKm) tanker.OdometerKm = telemetry.OdometerKm;

        await _db.SaveChangesAsync();

        if (telemetry.PumpActive && telemetry.SpeedKph > 12)
            await RaiseAnomalyAsync(AnomalyKind.DispensedOffSite, AnomalySeverity.Attention, tanker.Id, null, null,
                telemetry.LitresOnBoard, telemetry.LitresOnBoard,
                "Пумпата е активна додека возилото се движи",
                "Телеметријата пријави активна пумпа при брзина над 12 km/h. Ова е известување за проверка на сензорот или на постапката на терен — не е обвинување.");

        var loaded = tanker.Compartments.Sum(c => c.CurrentLitres);
        if (tanker.Compartments.Count > 0 && Math.Abs(loaded - telemetry.LitresOnBoard) > InventoryToleranceLitres)
            await RaiseAnomalyAsync(AnomalyKind.InventoryDrift, AnomalySeverity.Information, tanker.Id, null, null,
                loaded, telemetry.LitresOnBoard,
                "Разлика помеѓу евиденцијата на комори и сензорот",
                "Збирот на коморите се разликува од количината пријавена од сензорот. Најчесто станува збор за неажурирана евиденција или температурна корекција.");

        return telemetry;
    }

    public async Task<List<TankerVm>> GetTankersAsync()
    {
        var tankers = await _db.DeliveryVehicles
            .Include(t => t.Compartments).ThenInclude(c => c.FuelType)
            .Include(t => t.DeliveryZone)
            .OrderBy(t => t.Code)
            .ToListAsync();

        var ids = tankers.Select(t => t.Id).ToList();

        var latestStamps = await _db.TankerTelemetries
            .Where(t => ids.Contains(t.DeliveryVehicleId))
            .GroupBy(t => t.DeliveryVehicleId)
            .Select(g => new { Id = g.Key, Recorded = g.Max(x => x.RecordedAtUtc) })
            .ToListAsync();

        var stamps = latestStamps.Select(s => s.Recorded).Distinct().ToList();

        var latest = stamps.Count == 0
            ? new List<TankerTelemetry>()
            : await _db.TankerTelemetries
                .Where(t => ids.Contains(t.DeliveryVehicleId) && stamps.Contains(t.RecordedAtUtc))
                .ToListAsync();

        var open = await _db.Orders
            .Where(o => o.DeliveryVehicleId != null &&
                        (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Assigned ||
                         o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Arrived))
            .GroupBy(o => o.DeliveryVehicleId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count(), Litres = g.Sum(x => x.Litres) })
            .ToListAsync();

        return tankers.Select(t =>
        {
            var assigned = open.FirstOrDefault(o => o.Id == t.Id);

            return new TankerVm
            {
                Tanker = t,
                Compartments = t.Compartments.OrderBy(c => c.Number).ToList(),
                Latest = latest
                    .Where(l => l.DeliveryVehicleId == t.Id)
                    .OrderByDescending(l => l.RecordedAtUtc)
                    .FirstOrDefault(),
                OpenOrders = assigned?.Count ?? 0,
                AssignedLitres = Math.Round(assigned?.Litres ?? 0, 1),
                ZoneName = t.DeliveryZone?.Name,
                ZoneColor = t.DeliveryZone?.ColorHex
            };
        }).ToList();
    }

    public async Task<TankerVm?> GetTankerAsync(int id)
    {
        var all = await GetTankersAsync();
        return all.FirstOrDefault(t => t.Tanker.Id == id);
    }

    public Task<List<TankerTelemetry>> GetTelemetryAsync(int tankerId, int take = 60) =>
        _db.TankerTelemetries
            .Where(t => t.DeliveryVehicleId == tankerId)
            .OrderByDescending(t => t.RecordedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

    public async Task<int> SimulateTickAsync()
    {
        var tankers = await _db.DeliveryVehicles
            .Include(t => t.Compartments)
            .Where(t => !t.IsInMaintenance)
            .ToListAsync();

        if (tankers.Count == 0) return 0;

        var openOrders = await _db.Orders
            .Where(o => o.DeliveryVehicleId != null &&
                        (o.Status == OrderStatus.Assigned || o.Status == OrderStatus.OnTheWay))
            .Select(o => new { o.DeliveryVehicleId, o.DeliveryLatitude, o.DeliveryLongitude })
            .ToListAsync();

        var seed = (int)(DateTime.UtcNow.Ticks % int.MaxValue);
        var random = new Random(seed);
        var recorded = 0;

        foreach (var tanker in tankers)
        {
            var target = openOrders.FirstOrDefault(o => o.DeliveryVehicleId == tanker.Id);

            var fromLat = tanker.CurrentLatitude == 0 ? tanker.BaseLatitude : tanker.CurrentLatitude;
            var fromLng = tanker.CurrentLongitude == 0 ? tanker.BaseLongitude : tanker.CurrentLongitude;

            double toLat, toLng;
            bool moving;

            if (target is not null)
            {
                toLat = target.DeliveryLatitude;
                toLng = target.DeliveryLongitude;
                moving = true;
            }
            else
            {
                toLat = tanker.BaseLatitude;
                toLng = tanker.BaseLongitude;
                moving = GeoUtils.HaversineKm(fromLat, fromLng, toLat, toLng) > 0.4;
            }

            var step = moving ? 0.22 : 0.0;

            var lat = fromLat + (toLat - fromLat) * step + (random.NextDouble() - 0.5) * 0.002;
            var lng = fromLng + (toLng - fromLng) * step + (random.NextDouble() - 0.5) * 0.002;

            var distance = GeoUtils.HaversineKm(fromLat, fromLng, lat, lng);

            var input = new TelemetryInput
            {
                Latitude = lat,
                Longitude = lng,
                SpeedKph = moving ? Math.Round(28 + random.NextDouble() * 46, 1) : 0,
                HeadingDegrees = Math.Round(random.NextDouble() * 360, 0),
                LitresOnBoard = Math.Round(tanker.Compartments.Sum(c => c.CurrentLitres), 1),
                OdometerKm = Math.Round(tanker.OdometerKm + distance, 1),
                PumpActive = !moving && target is not null,
                EngineOn = true,
                Source = TelemetrySource.Simulator,
                Note = "simulated"
            };

            var telemetry = await RecordTelemetryAsync(tanker.Id, input);
            if (telemetry is not null) recorded++;
        }

        await TrimTelemetryAsync();

        return recorded;
    }

    private async Task TrimTelemetryAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-3);

        var stale = await _db.TankerTelemetries
            .Where(t => t.RecordedAtUtc < cutoff)
            .Take(500)
            .ToListAsync();

        if (stale.Count == 0) return;

        _db.TankerTelemetries.RemoveRange(stale);
        await _db.SaveChangesAsync();
    }

    public async Task<int> DetectAnomaliesAsync()
    {
        var created = 0;
        var since = DateTime.UtcNow.AddDays(-14);

        var deliveries = await _db.ContractDeliveries
            .Include(d => d.SupplyContract)
            .Include(d => d.Proof)
            .Where(d => d.Status == OrderStatus.Delivered && d.ScheduledForUtc >= since && d.PlannedLitres > 0)
            .OrderByDescending(d => d.ScheduledForUtc)
            .Take(200)
            .ToListAsync();

        foreach (var delivery in deliveries)
        {
            var variance = Math.Abs(delivery.DeliveredLitres - delivery.PlannedLitres) / Math.Max(1, delivery.PlannedLitres);

            if (variance > VarianceThreshold)
            {
                var raised = await RaiseAnomalyAsync(AnomalyKind.VolumeVariance,
                    variance > 0.15 ? AnomalySeverity.Attention : AnomalySeverity.Information,
                    delivery.DeliveryVehicleId, null, delivery.Id,
                    delivery.PlannedLitres, delivery.DeliveredLitres,
                    "Отстапување помеѓу планирана и испорачана количина",
                    "Испораката отстапува од планираната количина за повеќе од 8 %. Ова е известување за проверка, не обвинување — " +
                    "разликата обично произлегува од температурна корекција, делумно полн резервоар или ракување на терен.",
                    delivery.SupplyContract?.BusinessAccountId);

                if (raised) created++;
            }

            if (delivery.Proof is not null)
            {
                var meterDifference = Math.Abs(delivery.Proof.MeterDifference - delivery.Proof.LitresDelivered);

                if (meterDifference > MeterToleranceLitres)
                {
                    var raised = await RaiseAnomalyAsync(AnomalyKind.MeterMismatch, AnomalySeverity.Attention,
                        delivery.DeliveryVehicleId, null, delivery.Id,
                        delivery.Proof.MeterDifference, delivery.Proof.LitresDelivered,
                        "Отчитувањето на мерачот не се совпаѓа со потврдата",
                        "Разликата на мерачот и потпишаната количина на доставницата не се совпаѓаат. Потребна е проверка на записот.",
                        delivery.SupplyContract?.BusinessAccountId);

                    if (raised) created++;
                }
            }
        }

        return created;
    }

    private async Task<bool> RaiseAnomalyAsync(
        AnomalyKind kind, AnomalySeverity severity,
        int? tankerId, int? orderId, int? contractDeliveryId,
        double expected, double observed,
        string subject, string detail,
        int? businessAccountId = null)
    {
        var since = DateTime.UtcNow.AddHours(-12);

        var exists = await _db.FuelAnomalies.AnyAsync(a =>
            a.Kind == kind &&
            a.DeliveryVehicleId == tankerId &&
            a.OrderId == orderId &&
            a.ContractDeliveryId == contractDeliveryId &&
            (contractDeliveryId != null || a.DetectedAtUtc >= since));

        if (exists) return false;

        _db.FuelAnomalies.Add(new FuelAnomaly
        {
            Kind = kind,
            Severity = severity,
            Status = AnomalyStatus.Open,
            DeliveryVehicleId = tankerId,
            OrderId = orderId,
            ContractDeliveryId = contractDeliveryId,
            BusinessAccountId = businessAccountId,
            ExpectedLitres = Math.Round(expected, 1),
            ObservedLitres = Math.Round(observed, 1),
            Subject = subject,
            Detail = detail
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Fuel alert raised: {Kind} for tanker {Tanker}.", kind, tankerId);
        return true;
    }

    public async Task<bool> SetCompartmentAsync(int compartmentId, double litres, int? fuelTypeId)
    {
        var compartment = await _db.TankerCompartments.FirstOrDefaultAsync(c => c.Id == compartmentId);
        if (compartment is null) return false;

        compartment.CurrentLitres = Math.Clamp(litres, 0, compartment.CapacityLitres);
        if (fuelTypeId is > 0) compartment.FuelTypeId = fuelTypeId;
        compartment.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> RefillTankerAsync(int tankerId)
    {
        var compartments = await _db.TankerCompartments
            .Where(c => c.DeliveryVehicleId == tankerId)
            .ToListAsync();

        foreach (var compartment in compartments)
        {
            compartment.CurrentLitres = compartment.CapacityLitres;
            compartment.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return compartments.Count;
    }

    public async Task<bool> ToggleMaintenanceAsync(int tankerId)
    {
        var tanker = await _db.DeliveryVehicles.FirstOrDefaultAsync(t => t.Id == tankerId);
        if (tanker is null) return false;

        tanker.IsInMaintenance = !tanker.IsInMaintenance;
        tanker.IsAvailable = !tanker.IsInMaintenance;

        await _db.SaveChangesAsync();
        return true;
    }
}
