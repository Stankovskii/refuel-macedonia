using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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

[Authorize(Roles = DbSeeder.RoleAdmin)]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPriceService _prices;
    private readonly IOrderService _orders;
    private readonly IOsmImportService _osm;
    private readonly IErcPriceScraper _scraper;
    private readonly IPricePredictionService _predictions;
    private readonly IRouteOptimizationService _routes;
    private readonly IWeatherService _weather;
    private readonly IRoadConditionService _roadConditions;
    private readonly IBusinessService _business;
    private readonly IPricingService _pricing;
    private readonly IProfitabilityService _profitability;
    private readonly UserManager<ApplicationUser> _users;

    public AdminController(
        ApplicationDbContext db,
        IPriceService prices,
        IOrderService orders,
        IOsmImportService osm,
        IErcPriceScraper scraper,
        IPricePredictionService predictions,
        IRouteOptimizationService routes,
        IWeatherService weather,
        IRoadConditionService roadConditions,
        IBusinessService business,
        IPricingService pricing,
        IProfitabilityService profitability,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _prices = prices;
        _orders = orders;
        _osm = osm;
        _scraper = scraper;
        _predictions = predictions;
        _routes = routes;
        _weather = weather;
        _roadConditions = roadConditions;
        _business = business;
        _pricing = pricing;
        _profitability = profitability;
        _users = users;
    }

    public async Task<IActionResult> Index()
    {
        var delivered = await _db.Orders
            .Where(o => o.Status == OrderStatus.Delivered)
            .Select(o => new { o.Total, o.Litres })
            .ToListAsync();

        var byStatus = await _db.Orders
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var byBrand = await _db.Stations
            .Where(s => s.BrandId != null)
            .GroupBy(s => s.Brand!.Name)
            .Select(g => new { Brand = g.Key, Count = g.Count() })
            .ToListAsync();

        var vm = new AdminDashboardVm
        {
            Stations = await _db.Stations.CountAsync(),
            OsmStations = await _db.Stations.CountAsync(s => s.Source == StationSource.OpenStreetMap),
            VerifiedStations = await _db.Stations.CountAsync(s => s.IsVerified),
            Users = await _db.Users.CountAsync(),
            Orders = await _db.Orders.CountAsync(),
            OpenOrders = await _db.Orders.CountAsync(o =>
                o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed ||
                o.Status == OrderStatus.Assigned || o.Status == OrderStatus.OnTheWay),
            PendingReports = await _db.PriceReports.CountAsync(r => r.Status == ReportStatus.Pending),
            Revenue = delivered.Count == 0 ? 0 : delivered.Sum(o => o.Total),
            LitresDelivered = delivered.Count == 0 ? 0 : Math.Round(delivered.Sum(o => o.Litres), 1),
            LastOsmImportUtc = IngestionState.LastOsmImportUtc,
            LastPriceUpdateUtc = IngestionState.LastScrapeUtc ?? await _prices.GetLastUpdateAsync(),
            Board = await _prices.GetBoardAsync(),
            OrdersByStatus = byStatus.ToDictionary(x => x.Status.ToString(), x => x.Count),
            StationsByBrand = byBrand.OrderByDescending(x => x.Count).ToDictionary(x => x.Brand, x => x.Count),
            RecentOrders = await _db.Orders
                .Include(o => o.User)
                .Include(o => o.FuelType)
                .OrderByDescending(o => o.CreatedAtUtc)
                .Take(12)
                .ToListAsync(),
            RecentReports = await _db.PriceReports
                .Include(r => r.Station)
                .Include(r => r.FuelType)
                .Include(r => r.User)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(12)
                .ToListAsync()
        };

        ViewBag.OsmResult = IngestionState.LastOsmResult;
        ViewBag.ScrapeResult = IngestionState.LastScrapeResult;

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportOsm(bool replaceSeed = true)
    {
        var result = await _osm.ImportAsync(replaceSeed);
        IngestionState.LastOsmResult = result;
        IngestionState.LastOsmImportUtc = DateTime.UtcNow;

        TempData["Message"] = result.Message;
        TempData["Detail"] = $"{result.Created} + / {result.Updated} ~ / {result.Fetched} total";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScrapePrices()
    {
        var result = await _scraper.RefreshAsync();
        IngestionState.LastScrapeResult = result;
        IngestionState.LastScrapeUtc = DateTime.UtcNow;

        TempData["Message"] = result.Message;
        TempData["Detail"] = result.Prices is null ? null : string.Join(", ", result.Prices.Select(p => $"{p.Key}={p.Value}"));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshWeather()
    {
        var result = await _weather.RefreshAsync();
        IngestionState.LastWeatherResult = result;
        IngestionState.LastWeatherUtc = DateTime.UtcNow;
        TempData["Message"] = result.Message;
        TempData["Detail"] = result.Updated.ToString();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshRoads()
    {
        var result = await _roadConditions.RefreshAsync();
        IngestionState.LastRoadResult = result;
        IngestionState.LastRoadUtc = DateTime.UtcNow;
        TempData["Message"] = result.Message;
        TempData["Detail"] = $"{result.Roads} / {result.Borders}";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Contracts()
    {
        var accounts = await _business.GetAccountsAsync();
        ViewBag.Deliveries = await _db.ContractDeliveries
            .Include(d => d.SupplyContract).ThenInclude(c => c!.BusinessAccount)
            .Include(d => d.ContractAsset)
            .Include(d => d.DeliveryVehicle)
            .Include(d => d.Proof)
            .OrderByDescending(d => d.ScheduledForUtc)
            .Take(40)
            .ToListAsync();

        ViewBag.Invoices = await _db.Invoices
            .Include(i => i.BusinessAccount)
            .OrderByDescending(i => i.IssuedOn)
            .Take(20)
            .ToListAsync();

        return View(accounts);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateDeliveries()
    {
        var created = await _business.GenerateUpcomingDeliveriesAsync(14);
        TempData["Message"] = "admin.contracts.generated";
        TempData["Detail"] = created.ToString();
        return RedirectToAction(nameof(Contracts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateForecasts()
    {
        var count = await _predictions.RegenerateAsync(6);
        TempData["Message"] = "admin.forecasts.done";
        TempData["Detail"] = count.ToString();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Prices()
    {
        ViewBag.Fuels = await _db.FuelTypes.OrderBy(f => f.SortOrder).ToListAsync();

        var recent = await _db.NationalPrices
            .Include(p => p.FuelType)
            .OrderByDescending(p => p.EffectiveFrom)
            .Take(60)
            .ToListAsync();

        return View(recent);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrices(DateTime effectiveFrom, string[] codes, decimal[] values)
    {
        var map = new Dictionary<string, decimal>();
        for (var i = 0; i < codes.Length && i < values.Length; i++)
        {
            if (values[i] > 0) map[codes[i]] = values[i];
        }

        if (map.Count > 0)
        {
            var result = await _scraper.ApplyManualAsync(map, effectiveFrom);
            TempData["Message"] = result.Message;
        }

        return RedirectToAction(nameof(Prices));
    }

    public async Task<IActionResult> Reports(ReportStatus status = ReportStatus.Pending)
    {
        var reports = await _db.PriceReports
            .Include(r => r.Station)
            .Include(r => r.FuelType)
            .Include(r => r.User)
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(100)
            .ToListAsync();

        ViewBag.Status = status;
        return View(reports);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Moderate(int id, bool approve)
    {
        var moderatorId = _users.GetUserId(User) ?? string.Empty;
        await _prices.ModerateReportAsync(id, approve, moderatorId);
        return RedirectToAction(nameof(Reports));
    }

    public async Task<IActionResult> Orders(OrderStatus? status, int page = 1)
    {
        var query = _db.Orders
            .Include(o => o.User)
            .Include(o => o.FuelType)
            .Include(o => o.Station)
            .AsQueryable();

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);

        var total = await query.CountAsync();
        const int pageSize = 25;

        var orders = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        ViewBag.Status = status;

        return View(orders);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, OrderStatus status)
    {
        await _orders.AdvanceStatusAsync(id, status, "admin.status.changed");
        return RedirectToAction(nameof(Orders));
    }

    public async Task<IActionResult> Stations(string? q, int page = 1)
    {
        var query = _db.Stations.Include(s => s.Brand).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(s => s.Name.Contains(q) || s.City.Contains(q));

        var total = await query.CountAsync();
        const int pageSize = 30;

        var stations = await query
            .OrderBy(s => s.City).ThenBy(s => s.Name)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        ViewBag.Query = q;

        return View(stations);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyStation(int id)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == id);
        if (station is not null)
        {
            station.IsVerified = !station.IsVerified;
            if (station.IsVerified) station.Source = StationSource.CompanyVerified;
            station.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Stations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStation(int id)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == id);
        if (station is not null)
        {
            station.IsActive = !station.IsActive;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Stations));
    }

    public async Task<IActionResult> Promotions()
    {
        var promotions = await _db.Promotions
            .Include(p => p.FuelType)
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.EndsAtUtc)
            .ToListAsync();

        ViewBag.Fuels = await _db.FuelTypes.OrderBy(f => f.SortOrder).ToListAsync();
        return View(promotions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePromotion(int id)
    {
        var promo = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == id);
        if (promo is not null)
        {
            promo.IsActive = !promo.IsActive;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Promotions));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePromotion(Promotion model)
    {
        if (string.IsNullOrWhiteSpace(model.Code))
        {
            TempData["Message"] = "admin.promo.error";
            return RedirectToAction(nameof(Promotions));
        }

        model.Code = model.Code.Trim().ToUpperInvariant();
        if (await _db.Promotions.AnyAsync(p => p.Code == model.Code))
        {
            TempData["Message"] = "admin.promo.duplicate";
            return RedirectToAction(nameof(Promotions));
        }

        if (string.IsNullOrWhiteSpace(model.TitleEn)) model.TitleEn = model.TitleMk;
        if (string.IsNullOrWhiteSpace(model.TitleSq)) model.TitleSq = model.TitleMk;
        if (model.DayOfWeekMask <= 0) model.DayOfWeekMask = 127;
        if (model.FuelTypeId is 0) model.FuelTypeId = null;

        model.CreatedAtUtc = DateTime.UtcNow;
        _db.Promotions.Add(model);
        await _db.SaveChangesAsync();

        TempData["Message"] = "admin.promo.created";
        return RedirectToAction(nameof(Promotions));
    }

    public async Task<IActionResult> Routes()
    {
        var plans = await _routes.PlanAsync();
        ViewBag.Fleet = await _db.DeliveryVehicles.ToListAsync();
        return View(plans);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRoutes()
    {
        var plans = await _routes.PlanAsync(DateTime.UtcNow, persist: true);
        TempData["Message"] = "admin.routes.saved";
        TempData["Detail"] = plans.Count.ToString();
        return RedirectToAction(nameof(Routes));
    }

    public async Task<IActionResult> Pricing(int? fuelTypeId, double litres = 45, double distanceKm = 12,
        double monthlyLitres = 0, CustomerType customerType = CustomerType.Individual, bool emergency = false,
        OrderType orderType = OrderType.Standard)
    {
        var vm = await BuildPricingVmAsync(fuelTypeId, litres, distanceKm, monthlyLitres, customerType,
            emergency ? OrderType.Emergency : orderType);
        return View(vm);
    }

    private async Task<PricingAdminVm> BuildPricingVmAsync(int? fuelTypeId, double litres, double distanceKm,
        double monthlyLitres, CustomerType customerType, OrderType orderType)
    {
        var fuels = await _db.FuelTypes.Where(f => f.IsDeliverable).OrderBy(f => f.SortOrder).ToListAsync();
        var fuel = fuels.FirstOrDefault(f => f.Id == fuelTypeId) ?? fuels.FirstOrDefault();

        // The preview is anchored on the regulator's pump price, exactly like a real order.
        var pumpPrice = fuel is null ? 0m : await _prices.GetCurrentNationalPriceAsync(fuel.Id) ?? 0m;

        var request = new PricingRequest
        {
            CustomerType = customerType,
            PumpPricePerLitre = pumpPrice,
            BaseCostPerLitre = pumpPrice,
            Litres = Math.Max(1, litres),
            DistanceKm = Math.Max(0, distanceKm),
            MonthlyLitres = Math.Max(0, monthlyLitres),
            OrderType = orderType,
            IsEmergency = orderType == OrderType.Emergency
        };

        return new PricingAdminVm
        {
            Settings = await _pricing.GetSettingsAsync(),
            Tiers = await _pricing.GetTiersAsync(onlyActive: false),
            Bands = await _pricing.GetBandsAsync(),
            Zones = await _pricing.GetZonesAsync(onlyActive: false),
            Fuels = fuels,
            SelectedFuelTypeId = fuel?.Id ?? 0,
            SampleBaseCost = pumpPrice,
            PreviewRequest = request,
            Preview = pumpPrice <= 0 ? null : await _pricing.QuoteAsync(request)
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePricingSettings(PricingSettings model)
    {
        await _pricing.SaveSettingsAsync(model, User.Identity?.Name ?? "admin");
        TempData["Message"] = "admin.pricing.saved";
        return RedirectToAction(nameof(Pricing));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTier(PricingTier model)
    {
        await _pricing.SaveTierAsync(model);
        TempData["Message"] = "admin.pricing.tier.saved";
        return RedirectToAction(nameof(Pricing));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBand(VolumeDiscountBand model)
    {
        await _pricing.SaveBandAsync(model);
        TempData["Message"] = "admin.pricing.band.saved";
        return RedirectToAction(nameof(Pricing));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBand(int id)
    {
        await _pricing.DeleteBandAsync(id);
        TempData["Message"] = "admin.pricing.band.deleted";
        return RedirectToAction(nameof(Pricing));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveZone(DeliveryZone model)
    {
        await _pricing.SaveZoneAsync(model);
        TempData["Message"] = "admin.pricing.zone.saved";
        return RedirectToAction(nameof(Pricing));
    }

    public async Task<IActionResult> Economics(EconomicsInputVm input)
    {
        var settings = await _pricing.GetSettingsAsync();

        if (input.MonthlyFixedCosts <= 0) input.MonthlyFixedCosts = settings.MonthlyFixedCosts;

        if (input.AverageMarginPerLitre <= 0)
        {
            var tier = await _pricing.ResolveTierAsync(CustomerType.Individual);
            input.AverageMarginPerLitre = tier.MarginPerLitre;
        }

        if (input.Tankers <= 0) input.Tankers = Math.Max(1, await _db.DeliveryVehicles.CountAsync());

        ViewBag.Settings = settings;

        return View(_pricing.CalculateEconomics(input, settings));
    }

    public async Task<IActionResult> Profitability(int days = 90)
    {
        return View(await _profitability.GetAsync(days));
    }

    public async Task<IActionResult> Anomalies(AnomalyStatus? status)
    {
        var query = _db.FuelAnomalies
            .Include(a => a.DeliveryVehicle)
            .Include(a => a.BusinessAccount)
            .Include(a => a.ContractDelivery)
            .Include(a => a.Order)
            .AsQueryable();

        if (status.HasValue) query = query.Where(a => a.Status == status.Value);

        ViewBag.Status = status;

        return View(await query
            .OrderByDescending(a => a.DetectedAtUtc)
            .Take(80)
            .ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewAnomaly(int id, AnomalyStatus status, string? explanation)
    {
        var anomaly = await _db.FuelAnomalies.FirstOrDefaultAsync(a => a.Id == id);
        if (anomaly is not null)
        {
            anomaly.Status = status;
            anomaly.Explanation = explanation;
            anomaly.ReviewedByUserId = _users.GetUserId(User);
            anomaly.ReviewedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Message"] = "admin.anomaly.reviewed";
        }

        return RedirectToAction(nameof(Anomalies));
    }

    public async Task<IActionResult> Offers()
    {
        ViewBag.Fuels = await _db.FuelTypes.Where(f => f.IsDeliverable).OrderBy(f => f.SortOrder).ToListAsync();
        ViewBag.Tiers = await _pricing.GetTiersAsync();
        ViewBag.Settings = await _pricing.GetSettingsAsync();

        return View(await _db.BusinessOffers
            .Include(o => o.FuelType)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(60)
            .ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOffer(BusinessOffer model)
    {
        if (string.IsNullOrWhiteSpace(model.CompanyName) || model.EstimatedLitresPerMonth <= 0)
        {
            TempData["Message"] = "admin.offer.error";
            return RedirectToAction(nameof(Offers));
        }

        var settings = await _pricing.GetSettingsAsync();
        var tier = await _pricing.ResolveTierAsync(model.CustomerType);

        if (model.ProposedMarginPerLitre <= 0) model.ProposedMarginPerLitre = tier.MarginPerLitre;

        // The offer's cost basis is what ReFuel pays: the pump price minus the procurement spread.
        if (model.BaseCostPerLitre <= 0)
        {
            var pump = await _prices.GetCurrentNationalPriceAsync(model.FuelTypeId) ?? 0m;
            model.BaseCostPerLitre = Math.Round(Math.Max(0, pump - settings.ProcurementSpreadPerLitre), 2);
        }

        if (model.VolumeDiscountPerLitre <= 0)
        {
            var bands = await _pricing.GetBandsAsync(tier.Id);
            model.VolumeDiscountPerLitre = bands.FirstOrDefault(b => b.Covers(model.EstimatedLitresPerMonth))?.DiscountPerLitre ?? 0m;
        }

        model.Recalculate(settings);

        model.Id = 0;
        model.Status = OfferStatus.Draft;
        model.BusinessAccountId = null;
        model.OfferNumber = await NextOfferNumberAsync();
        model.CreatedByUserId = _users.GetUserId(User);
        model.CreatedAtUtc = DateTime.UtcNow;
        if (model.ValidUntil < DateTime.UtcNow.Date) model.ValidUntil = DateTime.UtcNow.Date.AddDays(30);

        _db.BusinessOffers.Add(model);
        await _db.SaveChangesAsync();

        TempData["Message"] = "admin.offer.created";
        TempData["Detail"] = model.OfferNumber;
        return RedirectToAction(nameof(Offers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetOfferStatus(int id, OfferStatus status)
    {
        var offer = await _db.BusinessOffers.FirstOrDefaultAsync(o => o.Id == id);
        if (offer is not null)
        {
            offer.Status = status;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Offers));
    }

    private async Task<string> NextOfferNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var count = await _db.BusinessOffers.CountAsync();

        string number;
        var attempt = 0;

        do
        {
            number = $"RF-OFF-{year}-{count + 1 + attempt:D4}";
            attempt++;
        }
        while (await _db.BusinessOffers.AnyAsync(o => o.OfferNumber == number) && attempt < 60);

        return number;
    }
}
