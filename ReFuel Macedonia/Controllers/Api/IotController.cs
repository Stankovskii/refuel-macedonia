using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;

namespace ReFuel_Macedonia.Controllers.Api;

[ApiController]
[Route("api/iot")]
[Produces("application/json")]
public class IotController : ControllerBase
{
    public const string ApiKeyHeader = "X-ReFuel-Key";

    private readonly IFleetService _fleet;
    private readonly ApplicationDbContext _db;
    private readonly ReFuelOptions _options;

    public IotController(IFleetService fleet, ApplicationDbContext db, IOptions<ReFuelOptions> options)
    {
        _fleet = fleet;
        _db = db;
        _options = options.Value;
    }

    private bool IsTrustedCaller()
    {
        var configured = _options.IotApiKey;

        if (!string.IsNullOrWhiteSpace(configured) &&
            Request.Headers.TryGetValue(ApiKeyHeader, out var provided) &&
            string.Equals(provided.ToString(), configured, StringComparison.Ordinal))
            return true;

        return User.Identity?.IsAuthenticated == true &&
               (User.IsInRole(DbSeeder.RoleAdmin) || User.IsInRole(DbSeeder.RoleDriver));
    }

    [HttpPost("tankers/{id:int}/telemetry")]
    public async Task<IActionResult> PostTelemetry(int id, [FromBody] TelemetryInput input)
    {
        if (!IsTrustedCaller()) return Unauthorized(new { error = "device-key-required", header = ApiKeyHeader });
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var telemetry = await _fleet.RecordTelemetryAsync(id, input);
        if (telemetry is null) return NotFound(new { error = "tanker-not-found", tankerId = id });

        return Accepted(new
        {
            accepted = true,
            tankerId = id,
            telemetryId = telemetry.Id,
            recordedAtUtc = telemetry.RecordedAtUtc,
            source = telemetry.Source.ToString()
        });
    }

    [HttpGet("tankers")]
    public async Task<IActionResult> Tankers()
    {
        if (!IsTrustedCaller()) return Unauthorized(new { error = "device-key-required", header = ApiKeyHeader });

        var tankers = await _fleet.GetTankersAsync();

        return Ok(tankers.Select(t => new
        {
            id = t.Tanker.Id,
            code = t.Tanker.Code,
            driver = t.Tanker.DriverName,
            plate = t.Tanker.PlateNumber,
            zone = t.ZoneName,
            latitude = t.Tanker.CurrentLatitude,
            longitude = t.Tanker.CurrentLongitude,
            speedKph = t.Tanker.SpeedKph,
            pumpActive = t.Tanker.PumpActive,
            inMaintenance = t.Tanker.IsInMaintenance,
            odometerKm = t.Tanker.OdometerKm,
            lastTelemetryUtc = t.Tanker.LastTelemetryUtc,
            loadedLitres = t.Tanker.LoadedLitres,
            capacityLitres = t.Tanker.CompartmentCapacityLitres,
            compartments = t.Compartments.Select(c => new
            {
                number = c.Number,
                fuel = c.FuelType?.Code,
                capacityLitres = c.CapacityLitres,
                currentLitres = c.CurrentLitres
            })
        }));
    }

    [HttpGet("tankers/{id:int}/telemetry")]
    public async Task<IActionResult> Telemetry(int id, int take = 60)
    {
        if (!IsTrustedCaller()) return Unauthorized(new { error = "device-key-required", header = ApiKeyHeader });

        if (!await _db.DeliveryVehicles.AnyAsync(t => t.Id == id))
            return NotFound(new { error = "tanker-not-found", tankerId = id });

        var rows = await _fleet.GetTelemetryAsync(id, take);

        return Ok(rows.Select(r => new
        {
            recordedAtUtc = r.RecordedAtUtc,
            latitude = r.Latitude,
            longitude = r.Longitude,
            speedKph = r.SpeedKph,
            headingDegrees = r.HeadingDegrees,
            litresOnBoard = r.LitresOnBoard,
            odometerKm = r.OdometerKm,
            pumpActive = r.PumpActive,
            engineOn = r.EngineOn,
            source = r.Source.ToString()
        }));
    }
}
