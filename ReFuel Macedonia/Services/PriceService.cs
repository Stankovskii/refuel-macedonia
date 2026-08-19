using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services;

public interface IPriceService
{
    Task<List<FuelType>> GetFuelTypesAsync(bool onlyDeliverable = false);
    Task<List<NationalPriceVm>> GetBoardAsync(bool withMarketStats = true);
    Task<PriceSeriesVm> GetSeriesAsync(int fuelTypeId, int months = 12);
    Task<List<BrandPriceVm>> GetBrandComparisonAsync(int fuelTypeId);
    Task<List<CityPriceVm>> GetCityComparisonAsync(int fuelTypeId, int take = 15);
    Task<decimal?> GetCurrentNationalPriceAsync(int fuelTypeId);
    Task<decimal?> GetStationPriceAsync(int stationId, int fuelTypeId);
    Task<List<StationListItemVm>> GetCheapestStationsAsync(int fuelTypeId, int take = 10, string? city = null);
    Task<DateTime?> GetLastUpdateAsync();

    Task<(bool Ok, string MessageKey, double Plausibility)> SubmitReportAsync(PriceReportVm model, string userId);
    Task<bool> ModerateReportAsync(int reportId, bool approve, string moderatorId);
    Task ApplyPriceAsync(int stationId, int fuelTypeId, decimal price, PriceSource source, double confidence, string? userId = null);
    Task<int> ApplyNationalPricesToStationsAsync();
}

public class PriceService : IPriceService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PriceService> _logger;

    public PriceService(ApplicationDbContext db, ILogger<PriceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static string Culture => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "sq" => "sq",
        "en" => "en",
        _ => "mk"
    };

    private record CurrentPriceRow(int StationId, string StationName, string City, int? BrandId, string? BrandName,
                                   string BrandColor, double Lat, double Lon, decimal Price);

    private Task<List<CurrentPriceRow>> LoadCurrentAsync(int fuelTypeId)
        => _db.StationPrices
              .Where(p => p.FuelTypeId == fuelTypeId && p.IsCurrent && p.Station!.IsActive)
              .Select(p => new CurrentPriceRow(
                    p.StationId,
                    p.Station!.Name,
                    p.Station.City,
                    p.Station.BrandId,
                    p.Station.Brand != null ? p.Station.Brand.Name : null,
                    p.Station.Brand != null ? p.Station.Brand.ColorHex : "#64748B",
                    p.Station.Latitude,
                    p.Station.Longitude,
                    p.Price))
              .ToListAsync();

    public Task<List<FuelType>> GetFuelTypesAsync(bool onlyDeliverable = false)
        => _db.FuelTypes
              .Where(f => !onlyDeliverable || f.IsDeliverable)
              .OrderBy(f => f.SortOrder)
              .ToListAsync();

    public async Task<List<NationalPriceVm>> GetBoardAsync(bool withMarketStats = true)
    {
        var fuels = await _db.FuelTypes.OrderBy(f => f.SortOrder).ToListAsync();
        var culture = Culture;
        var board = new List<NationalPriceVm>();

        foreach (var fuel in fuels)
        {
            var last2 = await _db.NationalPrices
                .Where(p => p.FuelTypeId == fuel.Id)
                .OrderByDescending(p => p.EffectiveFrom)
                .Take(2)
                .ToListAsync();

            if (last2.Count == 0) continue;

            var vm = new NationalPriceVm
            {
                FuelTypeId = fuel.Id,
                Code = fuel.Code,
                Name = fuel.LocalizedName(culture),
                Unit = fuel.Unit,
                Color = fuel.ColorHex,
                IsRegulated = fuel.IsRegulated,
                Price = last2[0].Price,
                PreviousPrice = last2.Count > 1 ? last2[1].Price : last2[0].Price,
                Change = last2.Count > 1 ? Math.Round(last2[0].Price - last2[1].Price, 3) : 0m,
                EffectiveFrom = last2[0].EffectiveFrom,
                DecisionReference = last2[0].DecisionReference
            };

            if (withMarketStats)
            {
                var rows = await LoadCurrentAsync(fuel.Id);
                if (rows.Count > 0)
                {
                    vm.MarketMin = rows.Min(r => r.Price);
                    vm.MarketMax = rows.Max(r => r.Price);
                    vm.MarketAvg = Math.Round(rows.Average(r => r.Price), 2);
                    vm.StationCount = rows.Count;

                    var cheapest = rows.OrderBy(r => r.Price).First();
                    vm.CheapestStationId = cheapest.StationId;
                    vm.CheapestStationName = cheapest.StationName;
                    vm.CheapestStationCity = cheapest.City;
                }
            }

            var prediction = await _db.PricePredictions
                .Where(p => p.FuelTypeId == fuel.Id && p.TargetDate > DateTime.UtcNow.Date)
                .OrderBy(p => p.TargetDate)
                .FirstOrDefaultAsync();

            if (prediction is not null)
            {
                vm.PredictedNext = prediction.PredictedPrice;
                vm.PredictedTrend = prediction.Trend;
                vm.PredictionConfidence = prediction.Confidence;
            }

            board.Add(vm);
        }

        return board;
    }

    public async Task<PriceSeriesVm> GetSeriesAsync(int fuelTypeId, int months = 12)
    {
        var fuel = await _db.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelTypeId);
        var from = DateTime.UtcNow.Date.AddMonths(-Math.Abs(months));

        var points = await _db.NationalPrices
            .Where(p => p.FuelTypeId == fuelTypeId && p.EffectiveFrom >= from)
            .OrderBy(p => p.EffectiveFrom)
            .Select(p => new PriceHistoryPointVm
            {
                Date = p.EffectiveFrom,
                Price = p.Price,
                Change = p.Change,
                IsOfficial = p.IsOfficial,
                Reference = p.DecisionReference
            })
            .ToListAsync();

        var forecast = await _db.PricePredictions
            .Where(p => p.FuelTypeId == fuelTypeId && p.TargetDate >= DateTime.UtcNow.Date)
            .OrderBy(p => p.TargetDate)
            .Select(p => new PriceHistoryPointVm { Date = p.TargetDate, Price = p.PredictedPrice })
            .ToListAsync();

        return new PriceSeriesVm
        {
            FuelTypeId = fuelTypeId,
            Name = fuel?.LocalizedName(Culture) ?? string.Empty,
            Color = fuel?.ColorHex ?? "#2563EB",
            Points = points,
            Forecast = forecast
        };
    }

    public async Task<List<BrandPriceVm>> GetBrandComparisonAsync(int fuelTypeId)
    {
        var rows = await LoadCurrentAsync(fuelTypeId);

        return rows.Where(r => r.BrandId.HasValue)
                   .GroupBy(r => new { r.BrandId, r.BrandName, r.BrandColor })
                   .Select(g => new BrandPriceVm
                   {
                       BrandId = g.Key.BrandId!.Value,
                       Brand = g.Key.BrandName ?? "-",
                       Color = g.Key.BrandColor,
                       AveragePrice = Math.Round(g.Average(x => x.Price), 2),
                       MinPrice = g.Min(x => x.Price),
                       StationCount = g.Count()
                   })
                   .OrderBy(x => x.AveragePrice)
                   .ToList();
    }

    public async Task<List<CityPriceVm>> GetCityComparisonAsync(int fuelTypeId, int take = 15)
    {
        var rows = await LoadCurrentAsync(fuelTypeId);

        return rows.GroupBy(r => r.City)
                   .Select(g => new CityPriceVm
                   {
                       City = g.Key,
                       AveragePrice = Math.Round(g.Average(x => x.Price), 2),
                       MinPrice = g.Min(x => x.Price),
                       StationCount = g.Count(),
                       Latitude = g.Average(x => x.Lat),
                       Longitude = g.Average(x => x.Lon)
                   })
                   .OrderByDescending(x => x.StationCount)
                   .Take(take)
                   .ToList();
    }

    public async Task<decimal?> GetCurrentNationalPriceAsync(int fuelTypeId)
        => await _db.NationalPrices
                    .Where(p => p.FuelTypeId == fuelTypeId)
                    .OrderByDescending(p => p.EffectiveFrom)
                    .Select(p => (decimal?)p.Price)
                    .FirstOrDefaultAsync();

    public async Task<decimal?> GetStationPriceAsync(int stationId, int fuelTypeId)
        => await _db.StationPrices
                    .Where(p => p.StationId == stationId && p.FuelTypeId == fuelTypeId && p.IsCurrent)
                    .Select(p => (decimal?)p.Price)
                    .FirstOrDefaultAsync();

    public async Task<List<StationListItemVm>> GetCheapestStationsAsync(int fuelTypeId, int take = 10, string? city = null)
    {
        var query = _db.StationPrices
            .Where(p => p.FuelTypeId == fuelTypeId && p.IsCurrent && p.Station!.IsActive);

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(p => p.Station!.City == city);

        return await query
            .OrderBy(p => p.Price)
            .Take(take)
            .Select(p => new StationListItemVm
            {
                Id = p.StationId,
                Name = p.Station!.Name,
                Brand = p.Station.Brand != null ? p.Station.Brand.Name : null,
                BrandColor = p.Station.Brand != null ? p.Station.Brand.ColorHex : "#64748B",
                City = p.Station.City,
                Address = p.Station.Address,
                Latitude = p.Station.Latitude,
                Longitude = p.Station.Longitude,
                IsOpen24h = p.Station.IsOpen24h,
                HasLpg = p.Station.HasLpg,
                HasShop = p.Station.HasShop,
                HasEvCharger = p.Station.HasEvCharger,
                IsVerified = p.Station.IsVerified,
                Price = p.Price,
                PriceObservedAtUtc = p.ObservedAtUtc
            })
            .ToListAsync();
    }

    public async Task<DateTime?> GetLastUpdateAsync()
        => await _db.StationPrices
                    .OrderByDescending(p => p.ObservedAtUtc)
                    .Select(p => (DateTime?)p.ObservedAtUtc)
                    .FirstOrDefaultAsync();

    public async Task<(bool Ok, string MessageKey, double Plausibility)> SubmitReportAsync(PriceReportVm model, string userId)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == model.StationId);
        if (station is null) return (false, "report.error.station", 0);

        var lastFromUser = await _db.PriceReports
            .Where(r => r.UserId == userId && r.StationId == model.StationId && r.FuelTypeId == model.FuelTypeId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => (DateTime?)r.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (lastFromUser.HasValue && lastFromUser.Value > DateTime.UtcNow.AddHours(-6))
            return (false, "report.error.toosoon", 0);

        var national = await GetCurrentNationalPriceAsync(model.FuelTypeId) ?? model.Price;

        var currentPrices = await _db.StationPrices
            .Where(p => p.FuelTypeId == model.FuelTypeId && p.IsCurrent)
            .Select(p => p.Price)
            .ToListAsync();

        var marketAvg = currentPrices.Count > 0 ? currentPrices.Average() : national;
        var plausibility = Plausibility(model.Price, national, marketAvg);

        var report = new PriceReport
        {
            StationId = model.StationId,
            FuelTypeId = model.FuelTypeId,
            UserId = userId,
            Price = model.Price,
            Note = model.Note,
            PlausibilityScore = plausibility,
            Status = plausibility >= 0.75 ? ReportStatus.Approved : ReportStatus.Pending,
            ReviewedAtUtc = plausibility >= 0.75 ? DateTime.UtcNow : null
        };

        _db.PriceReports.Add(report);
        await _db.SaveChangesAsync();

        if (report.Status == ReportStatus.Approved)
        {
            await ApplyPriceAsync(report.StationId, report.FuelTypeId, report.Price, PriceSource.UserReport, plausibility, userId);
            return (true, "report.ok.applied", plausibility);
        }

        return plausibility < 0.25
            ? (true, "report.ok.flagged", plausibility)
            : (true, "report.ok.pending", plausibility);
    }

    private static double Plausibility(decimal reported, decimal national, decimal marketAvg)
    {
        if (reported <= 0) return 0;

        var overMax = (double)(reported - national);
        var deviation = Math.Abs((double)(reported - marketAvg));

        var maxScore = overMax switch
        {
            <= 0 => 1.0,
            <= 0.5 => 0.6,
            <= 2.0 => 0.25,
            _ => 0.0
        };

        var avgScore = Math.Exp(-(deviation * deviation) / (2 * 4.0 * 4.0));

        if (marketAvg > 0 && (double)reported < (double)marketAvg * 0.85) avgScore *= 0.3;

        return Math.Round(Math.Clamp(0.55 * maxScore + 0.45 * avgScore, 0, 1), 3);
    }

    public async Task<bool> ModerateReportAsync(int reportId, bool approve, string moderatorId)
    {
        var report = await _db.PriceReports.FirstOrDefaultAsync(r => r.Id == reportId);
        if (report is null) return false;

        report.Status = approve ? ReportStatus.Approved : ReportStatus.Rejected;
        report.ReviewedAtUtc = DateTime.UtcNow;
        report.ReviewedByUserId = moderatorId;
        await _db.SaveChangesAsync();

        if (approve)
            await ApplyPriceAsync(report.StationId, report.FuelTypeId, report.Price, PriceSource.UserReport, 0.8, report.UserId);

        return true;
    }

    public async Task ApplyPriceAsync(int stationId, int fuelTypeId, decimal price, PriceSource source, double confidence, string? userId = null)
    {
        var existing = await _db.StationPrices
            .Where(p => p.StationId == stationId && p.FuelTypeId == fuelTypeId && p.IsCurrent)
            .ToListAsync();

        foreach (var e in existing) e.IsCurrent = false;

        _db.StationPrices.Add(new StationPrice
        {
            StationId = stationId,
            FuelTypeId = fuelTypeId,
            Price = price,
            Source = source,
            Confidence = confidence,
            IsCurrent = true,
            ObservedAtUtc = DateTime.UtcNow,
            ReportedByUserId = userId
        });

        await _db.SaveChangesAsync();
    }

    public async Task<int> ApplyNationalPricesToStationsAsync()
    {
        var fuels = await _db.FuelTypes.ToListAsync();
        var updated = 0;

        foreach (var fuel in fuels)
        {
            var last2 = await _db.NationalPrices
                .Where(p => p.FuelTypeId == fuel.Id)
                .OrderByDescending(p => p.EffectiveFrom)
                .Take(2)
                .ToListAsync();

            if (last2.Count < 2) continue;

            var delta = last2[0].Price - last2[1].Price;
            if (Math.Abs(delta) < 0.005m) continue;

            var decisionUtc = DateTime.SpecifyKind(last2[0].EffectiveFrom, DateTimeKind.Utc);

            var current = await _db.StationPrices
                .Where(p => p.FuelTypeId == fuel.Id && p.IsCurrent && p.ObservedAtUtc < decisionUtc)
                .ToListAsync();

            foreach (var price in current)
            {
                price.IsCurrent = false;

                var newPrice = Math.Round(price.Price + delta, 2);
                if (fuel.IsRegulated && newPrice > last2[0].Price) newPrice = last2[0].Price;

                _db.StationPrices.Add(new StationPrice
                {
                    StationId = price.StationId,
                    FuelTypeId = fuel.Id,
                    Price = newPrice,
                    Source = PriceSource.Estimated,
                    Confidence = 0.65,
                    IsCurrent = true,
                    ObservedAtUtc = DateTime.UtcNow
                });
                updated++;
            }
        }

        if (updated > 0) await _db.SaveChangesAsync();
        _logger.LogInformation("Propagated national price change to {Count} station prices.", updated);
        return updated;
    }
}
