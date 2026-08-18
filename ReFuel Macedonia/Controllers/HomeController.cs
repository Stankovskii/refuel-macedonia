using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;
using ReFuel_Macedonia.Services.Ai;
using ReFuel_Macedonia.Services.Ingestion;

namespace ReFuel_Macedonia.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPriceService _prices;
    private readonly IPromotionService _promotions;
    private readonly IRecommendationService _recommendations;
    private readonly ITrackingService _tracking;
    private readonly IWeatherService _weather;
    private readonly IRoadConditionService _roads;
    private readonly UserManager<ApplicationUser> _users;

    public HomeController(
        ApplicationDbContext db,
        IPriceService prices,
        IPromotionService promotions,
        IRecommendationService recommendations,
        ITrackingService tracking,
        IWeatherService weather,
        IRoadConditionService roads,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _prices = prices;
        _promotions = promotions;
        _recommendations = recommendations;
        _tracking = tracking;
        _weather = weather;
        _roads = roads;
        _users = users;
    }

    public async Task<IActionResult> Index(double? lat, double? lng)
    {
        var userId = _users.GetUserId(User);

        var board = await _prices.GetBoardAsync();
        var mainFuels = board.Where(b => b.Code is "EUROSUPER_95" or "EUROSUPER_98" or "EURODIESEL").ToList();

        var series = new List<PriceSeriesVm>();
        foreach (var fuel in mainFuels)
            series.Add(await _prices.GetSeriesAsync(fuel.FuelTypeId, 12));

        var mainFuelId = mainFuels.FirstOrDefault()?.FuelTypeId ?? board.FirstOrDefault()?.FuelTypeId ?? 0;

        var deliverable = await _prices.GetFuelTypesAsync(onlyDeliverable: true);
        var drivers = await _db.DeliveryVehicles.Where(v => v.IsAvailable)
                                                .OrderByDescending(v => v.DriverRating)
                                                .ToListAsync();

        var deliveredOrders = await _db.Orders
            .Where(o => o.Status == OrderStatus.Delivered)
            .Select(o => new { o.Litres, o.CreatedAtUtc, o.DeliveredAtUtc })
            .ToListAsync();

        var quickFuelId = deliverable.FirstOrDefault(f => f.Id == mainFuelId)?.Id
                          ?? deliverable.FirstOrDefault()?.Id
                          ?? 0;

        var unitPrice = quickFuelId > 0 ? await _prices.GetCurrentNationalPriceAsync(quickFuelId) ?? 0 : 0;

        var vm = new HomeVm
        {
            Board = board,
            Series = series,
            Cheapest = mainFuelId > 0
                ? await _prices.GetCheapestStationsAsync(mainFuelId, 6)
                : new List<StationListItemVm>(),
            Promotions = await _promotions.GetLivePromotionsAsync(4),
            StationCount = await _db.Stations.CountAsync(s => s.IsActive),
            VerifiedStationCount = await _db.Stations.CountAsync(s => s.IsVerified),
            CityCount = await _db.Stations.Select(s => s.City).Distinct().CountAsync(),
            BrandCount = await _db.Brands.CountAsync(),
            LastPriceUpdateUtc = await _prices.GetLastUpdateAsync() ?? DateTime.UtcNow,
            Drivers = drivers,
            DeliveriesCompleted = deliveredOrders.Count,
            LitresDelivered = Math.Round(deliveredOrders.Sum(o => o.Litres), 0),
            AverageDeliveryMinutes = deliveredOrders.Count == 0
                ? 45
                : Math.Round(deliveredOrders
                    .Where(o => o.DeliveredAtUtc.HasValue)
                    .Select(o => (o.DeliveredAtUtc!.Value - o.CreatedAtUtc).TotalMinutes)
                    .DefaultIfEmpty(45)
                    .Average(), 0),
            BusinessContracts = await _db.SupplyContracts.CountAsync(c => c.Status == ContractStatus.Active),
            RoadAlerts = await _roads.GetAlertsAsync(),
            QuickOrder = new QuickOrderVm
            {
                Fuels = deliverable,
                FuelTypeId = quickFuelId,
                Litres = 40,
                EstimatedUnitPrice = unitPrice,
                EstimatedTotal = Math.Round(unitPrice * 40m + 200m, 0),
                EtaMinutes = 45,
                AvailableDrivers = drivers.Count
            }
        };

        vm.Weather = lat.HasValue && lng.HasValue
            ? await _weather.GetNearestAsync(lat.Value, lng.Value)
            : await _weather.GetForCityAsync("Скопје");

        if (!string.IsNullOrEmpty(userId))
        {
            var user = await _users.GetUserAsync(User);
            vm.UserName = user?.FullName;
            vm.QuickOrder.City = user?.City ?? "Скопје";
            vm.QuickOrder.Address = user?.DefaultAddress;
            vm.Recommendation = await _recommendations.RecommendAsync(userId, lat, lng, null, take: 4);

            if (user?.City is not null)
            {
                var localWeather = await _weather.GetForCityAsync(user.City);
                if (localWeather is not null) vm.Weather = localWeather;
            }

            var active = await _db.Orders
                .Include(o => o.FuelType)
                .Include(o => o.DeliveryVehicle)
                .Where(o => o.UserId == userId &&
                    (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed ||
                     o.Status == OrderStatus.Assigned || o.Status == OrderStatus.OnTheWay ||
                     o.Status == OrderStatus.Arrived))
                .OrderByDescending(o => o.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (active is not null)
            {
                vm.ActiveOrder = active;
                vm.ActiveTracking = await _tracking.GetTrackingAsync(active.Id, userId);
            }
        }
        else if (lat.HasValue && lng.HasValue)
        {
            vm.Recommendation = await _recommendations.RecommendAsync(null, lat, lng, mainFuelId, take: 4);
        }

        return View(vm);
    }

    public IActionResult About() => View();

    public IActionResult Business() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string? returnUrl)
    {
        culture = SupportedCultures.Normalize(culture);

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                Path = "/"
            });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewBag.RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }
}
