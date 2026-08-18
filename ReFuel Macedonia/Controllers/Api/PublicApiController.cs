using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;
using ReFuel_Macedonia.Services.Ai;

namespace ReFuel_Macedonia.Controllers.Api;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class PublicApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IStationService _stations;
    private readonly IPriceService _prices;
    private readonly IPricePredictionService _predictions;
    private readonly IRecommendationService _recommendations;
    private readonly IRouteOptimizationService _routes;
    private readonly UserManager<ApplicationUser> _users;

    public PublicApiController(
        ApplicationDbContext db,
        IStationService stations,
        IPriceService prices,
        IPricePredictionService predictions,
        IRecommendationService recommendations,
        IRouteOptimizationService routes,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _stations = stations;
        _prices = prices;
        _predictions = predictions;
        _recommendations = recommendations;
        _routes = routes;
        _users = users;
    }

    [HttpGet("fuels")]
    public async Task<IActionResult> Fuels()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var fuels = await _prices.GetFuelTypesAsync();

        return Ok(fuels.Select(f => new
        {
            f.Id,
            f.Code,
            Name = f.LocalizedName(culture),
            f.Unit,
            f.ColorHex,
            f.IsRegulated,
            f.IsDeliverable
        }));
    }

    [HttpGet("prices")]
    public async Task<IActionResult> Prices() => Ok(await _prices.GetBoardAsync());

    [HttpGet("prices/{fuelTypeId:int}/history")]
    public async Task<IActionResult> History(int fuelTypeId, int months = 12)
        => Ok(await _prices.GetSeriesAsync(fuelTypeId, Math.Clamp(months, 1, 60)));

    [HttpGet("stations/map")]
    public async Task<IActionResult> MapData(int? fuelTypeId, string? city, int? brandId)
        => Ok(await _stations.GetMapDataAsync(fuelTypeId, city, brandId));

    [HttpGet("stations/near")]
    public async Task<IActionResult> Near(double lat, double lng, double radiusKm = 10, int? fuelTypeId = null, int take = 25)
        => Ok(await _stations.GetNearbyAsync(lat, lng, Math.Clamp(radiusKm, 0.5, 120), fuelTypeId, Math.Clamp(take, 1, 200)));

    [HttpGet("stations/cheapest")]
    public async Task<IActionResult> Cheapest(int fuelTypeId, int take = 10, string? city = null)
        => Ok(await _prices.GetCheapestStationsAsync(fuelTypeId, Math.Clamp(take, 1, 50), city));

    [HttpGet("ai/forecast/{fuelTypeId:int}")]
    public async Task<IActionResult> Forecast(int fuelTypeId, int weeks = 4)
        => Ok(await _predictions.ForecastAsync(fuelTypeId, Math.Clamp(weeks, 1, 12)));

    [HttpGet("ai/recommend")]
    public async Task<IActionResult> Recommend(double? lat, double? lng, int? fuelTypeId, double? tank, double? consumption, int take = 6)
    {
        var userId = _users.GetUserId(User);
        return Ok(await _recommendations.RecommendAsync(userId, lat, lng, fuelTypeId, tank, consumption, Math.Clamp(take, 1, 30)));
    }

    [HttpGet("ai/routes")]
    [Authorize(Roles = DbSeeder.RoleAdmin)]
    public async Task<IActionResult> Routes() => Ok(await _routes.PlanAsync());

    [HttpGet("compare/{fuelTypeId:int}")]
    public async Task<IActionResult> Compare(int fuelTypeId)
        => Ok(new
        {
            byBrand = await _prices.GetBrandComparisonAsync(fuelTypeId),
            byCity = await _prices.GetCityComparisonAsync(fuelTypeId, 30)
        });

    [HttpGet("stats")]
    public async Task<IActionResult> Stats() => Ok(new
    {
        stations = await _db.Stations.CountAsync(s => s.IsActive),
        verified = await _db.Stations.CountAsync(s => s.IsVerified),
        fromOsm = await _db.Stations.CountAsync(s => s.Source == StationSource.OpenStreetMap),
        cities = await _db.Stations.Select(s => s.City).Distinct().CountAsync(),
        brands = await _db.Brands.CountAsync(),
        prices = await _db.StationPrices.CountAsync(p => p.IsCurrent),
        lastUpdateUtc = await _prices.GetLastUpdateAsync()
    });
}
