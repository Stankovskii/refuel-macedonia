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
    private readonly IPricingService _pricing;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        ApplicationDbContext db,
        IPriceService prices,
        IPromotionService promotions,
        IRecommendationService recommendations,
        ITrackingService tracking,
        IPricingService pricing,
        UserManager<ApplicationUser> users,
        ILogger<HomeController> logger)
    {
        _db = db;
        _prices = prices;
        _promotions = promotions;
        _recommendations = recommendations;
        _tracking = tracking;
        _pricing = pricing;
        _users = users;
        _logger = logger;
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

        // Pricing model data for the landing page: tiers, bands, zones and a
        // real quote for the quick-order widget (pump price + minimum delivery).
        var settings = await _pricing.GetSettingsAsync();
        var tiers = await _pricing.GetTiersAsync();
        var bands = await _pricing.GetBandsAsync();
        var zones = await _pricing.GetZonesAsync();

        const double quickLitres = 40;
        var quickTotal = Math.Round(unitPrice * (decimal)quickLitres, 0);
        if (unitPrice > 0)
        {
            try
            {
                var quote = await _pricing.QuoteAsync(new PricingRequest
                {
                    CustomerType = CustomerType.Individual,
                    PumpPricePerLitre = unitPrice,
                    BaseCostPerLitre = unitPrice,
                    Litres = quickLitres,
                    DistanceKm = 0,
                    OrderType = OrderType.Standard
                });
                quickTotal = Math.Round(quote.Total, 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Quick-order estimate failed; falling back to fuel-only total.");
            }
        }

        var contractDrops = await _db.ContractDeliveries
            .Where(d => d.Status == OrderStatus.Delivered)
            .Select(d => d.DeliveredLitres)
            .ToListAsync();

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
            DeliveriesCompleted = deliveredOrders.Count + contractDrops.Count,
            LitresDelivered = Math.Round(deliveredOrders.Sum(o => o.Litres) + contractDrops.Sum(), 0),
            AverageDeliveryMinutes = deliveredOrders.Count == 0
                ? 45
                : Math.Round(deliveredOrders
                    .Where(o => o.DeliveredAtUtc.HasValue)
                    .Select(o => (o.DeliveredAtUtc!.Value - o.CreatedAtUtc).TotalMinutes)
                    .DefaultIfEmpty(45)
                    .Average(), 0),
            BusinessContracts = await _db.SupplyContracts.CountAsync(c => c.Status == ContractStatus.Active),
            QuickOrder = new QuickOrderVm
            {
                Fuels = deliverable,
                FuelTypeId = quickFuelId,
                Litres = quickLitres,
                EstimatedUnitPrice = unitPrice,
                EstimatedTotal = quickTotal,
                EtaMinutes = 45,
                AvailableDrivers = drivers.Count
            },
            Zones = zones,
            Segments = BuildSegments(tiers, bands, settings.ProcurementSpreadPerLitre),
            TankerCount = await _db.DeliveryVehicles.CountAsync(),
            DeliveryCityCount = zones.SelectMany(z => z.CityList).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            MinDeliveryFee = zones.Count > 0 ? zones.Min(z => z.BaseDeliveryFee)
                           : tiers.Count > 0 ? tiers.Min(t => t.MinDeliveryFee) : 0m,
            ScheduledDiscountPercent = tiers.Count > 0 ? tiers.Max(t => t.ScheduledDeliveryDiscountPercent) : 0,
            PriceDecisionReference = board.FirstOrDefault(b => b.IsRegulated && !string.IsNullOrWhiteSpace(b.DecisionReference))?.DecisionReference
                                     ?? board.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.DecisionReference))?.DecisionReference,
            PriceEffectiveFrom = board.Count > 0 ? board.Max(b => b.EffectiveFrom) : null
        };

        if (!string.IsNullOrEmpty(userId))
        {
            var user = await _users.GetUserAsync(User);
            vm.UserName = user?.FullName;
            vm.QuickOrder.City = user?.City ?? "Скопје";
            vm.QuickOrder.Address = user?.DefaultAddress;
            vm.Recommendation = await _recommendations.RecommendAsync(userId, lat, lng, null, take: 4);

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

    public IActionResult Terms() => View();

    public IActionResult Privacy() => View();

    /// <summary>
    /// Groups the active pricing tiers into the three landing-page segments and
    /// derives the customer-facing "below pump price" range of each group from
    /// the procurement spread, the tier margins (with their floors) and the best
    /// volume band — the same arithmetic the pricing service applies to a quote.
    /// </summary>
    private static List<HomeSegmentVm> BuildSegments(List<PricingTier> tiers, List<VolumeDiscountBand> bands, decimal spread)
    {
        var result = new List<HomeSegmentVm>();

        void Add(string key, bool featured, params CustomerType[] types)
        {
            var group = tiers.Where(t => t.IsActive && types.Contains(t.CustomerType)).ToList();
            if (group.Count == 0) return;

            decimal MinDiscount(PricingTier t) => Math.Max(0m, spread - t.MarginPerLitre);
            decimal MaxDiscount(PricingTier t)
            {
                var bestBand = bands.Where(b => b.IsActive && (b.PricingTierId == null || b.PricingTierId == t.Id))
                                    .Select(b => b.DiscountPerLitre)
                                    .DefaultIfEmpty(0m).Max();
                var floor = Math.Max(t.MinMarginPerLitre, t.MarginPerLitre - bestBand);
                return Math.Max(MinDiscount(t), spread - floor);
            }

            var from = group.Min(MinDiscount);
            var to = group.Max(MaxDiscount);

            result.Add(new HomeSegmentVm
            {
                Key = key,
                Featured = featured,
                DiscountFrom = Math.Round(from, 2),
                DiscountTo = Math.Round(Math.Max(from, to), 2),
                MinDeliveryFee = group.Min(t => t.MinDeliveryFee),
                ScheduledDiscountPercent = group.Max(t => t.ScheduledDeliveryDiscountPercent),
                EmergencySurcharge = group.Min(t => t.EmergencySurcharge),
                MinLitres = group.Min(t => t.MinOrderLitres),
                MaxLitres = group.Max(t => t.MaxOrderLitres),
                HasVolumeBands = bands.Any(b => b.IsActive && b.DiscountPerLitre > 0 &&
                                                (b.PricingTierId == null || group.Any(t => t.Id == b.PricingTierId)))
            });
        }

        Add("individual", false, CustomerType.Individual);
        Add("business", true, CustomerType.SmallBusiness, CustomerType.Construction);
        Add("fleet", false, CustomerType.Fleet, CustomerType.Enterprise);
        return result;
    }

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
