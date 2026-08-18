using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services.Ai;

public interface IRecommendationService
{
    Task<RecommendationResultVm> RecommendAsync(
        string? userId, double? lat, double? lon, int? fuelTypeId,
        double? tankLitres = null, double? consumption = null, int take = 6);

    Task<SavingsCalculatorVm> CalculateSavingsAsync(SavingsCalculatorVm input);
}

public class RecommendationService : IRecommendationService
{
    private readonly ApplicationDbContext _db;
    private readonly IStationService _stations;

    public RecommendationService(ApplicationDbContext db, IStationService stations)
    {
        _db = db;
        _stations = stations;
    }

    private const double TimeValuePerMinute = 3.0;

    public async Task<RecommendationResultVm> RecommendAsync(
        string? userId, double? lat, double? lon, int? fuelTypeId,
        double? tankLitres = null, double? consumption = null, int take = 6)
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        ApplicationUser? user = null;
        if (!string.IsNullOrEmpty(userId))
            user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);

        var requested = fuelTypeId is > 0 ? fuelTypeId : null;
        var fuelId = requested ?? user?.PreferredFuelTypeId ?? 0;

        var fuel = await _db.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelId)
                   ?? await _db.FuelTypes.OrderBy(f => f.SortOrder).FirstAsync();

        fuelId = fuel.Id;

        var latitude = lat ?? user?.HomeLatitude ?? GeoUtils.CountryCenterLat;
        var longitude = lon ?? user?.HomeLongitude ?? GeoUtils.CountryCenterLon;
        var radius = Math.Max(3, user?.MaxDetourKm ?? 12);

        var vehicle = user is null
            ? null
            : await _db.Vehicles.Where(v => v.UserId == user.Id)
                                .OrderByDescending(v => v.IsDefault)
                                .FirstOrDefaultAsync();

        var litres = tankLitres ?? vehicle?.TankCapacityLitres ?? 45;
        var lPer100 = consumption ?? vehicle?.AvgConsumption ?? 7.0;

        var result = new RecommendationResultVm
        {
            FuelTypeId = fuelId,
            FuelName = fuel.LocalizedName(culture),
            TankLitres = litres,
            Consumption = lPer100,
            Latitude = latitude,
            Longitude = longitude
        };

        var candidates = await _stations.GetNearbyAsync(latitude, longitude, radius, fuelId, 120);
        candidates = candidates.Where(c => c.Price.HasValue && c.Price > 0).ToList();

        if (candidates.Count == 0)
        {
            candidates = (await _stations.GetNearbyAsync(latitude, longitude, radius * 3, fuelId, 120))
                         .Where(c => c.Price.HasValue && c.Price > 0).ToList();
        }

        if (candidates.Count == 0)
        {
            result.Explanation = "ai.reco.nodata";
            return result;
        }

        var ids = candidates.Select(c => c.Id).ToList();
        var ratingRows = await _db.StationReviews
            .Where(r => ids.Contains(r.StationId))
            .Select(r => new { r.StationId, r.Rating })
            .ToListAsync();

        var ratings = ratingRows.GroupBy(r => r.StationId)
                                .ToDictionary(g => g.Key, g => g.Average(x => (double)x.Rating));

        var priceAges = await _db.StationPrices
            .Where(p => ids.Contains(p.StationId) && p.FuelTypeId == fuelId && p.IsCurrent)
            .Select(p => new { p.StationId, p.ObservedAtUtc, p.Confidence })
            .ToListAsync();

        var ageMap = priceAges.ToDictionary(p => p.StationId, p => p);

        var referencePrice = candidates.Max(c => c.Price!.Value);   // most expensive nearby = baseline
        var minPrice = candidates.Min(c => c.Price!.Value);
        var maxDistance = Math.Max(0.1, candidates.Max(c => c.DistanceKm));

        var distanceWeight = Math.Clamp(user?.DistanceWeight ?? 0.4, 0, 1);
        var priceWeight = 1 - distanceWeight;

        var scored = new List<RecommendedStationVm>();

        foreach (var c in candidates)
        {
            var price = c.Price!.Value;

            var roadKm = GeoUtils.RoadDistanceKm(latitude, longitude, c.Latitude, c.Longitude) * 2;
            var detourLitres = roadKm * lPer100 / 100.0;
            var detourCost = detourLitres * (double)price;
            var timeCost = GeoUtils.TravelMinutes(roadKm) * TimeValuePerMinute;

            var grossSaving = (double)(referencePrice - price) * litres;
            var netSaving = grossSaving - detourCost - timeCost;

            var priceScore = referencePrice == minPrice
                ? 1.0
                : (double)((referencePrice - price) / (referencePrice - minPrice));
            var distanceScore = 1.0 - c.DistanceKm / maxDistance;

            var score = priceWeight * priceScore + distanceWeight * distanceScore;

            var reasons = new List<string>();

            if (price <= minPrice + 0.2m) { score += 0.08; reasons.Add("ai.reason.cheapest"); }
            if (c.DistanceKm <= 2) { score += 0.05; reasons.Add("ai.reason.veryclose"); }
            if (c.IsOpen24h) { score += 0.03; reasons.Add("ai.reason.open24"); }
            if (c.IsVerified) { score += 0.03; reasons.Add("ai.reason.verified"); }

            if (ratings.TryGetValue(c.Id, out var rating))
            {
                c.Rating = Math.Round(rating, 2);
                score += (rating - 3.0) / 20.0;
                if (rating >= 4.3) reasons.Add("ai.reason.wellrated");
            }

            if (ageMap.TryGetValue(c.Id, out var age))
            {
                var days = (DateTime.UtcNow - age.ObservedAtUtc).TotalDays;
                if (days <= 3) { score += 0.04; reasons.Add("ai.reason.fresh"); }
                else if (days > 21) score -= 0.05;
                score += (age.Confidence - 0.5) / 25.0;
            }

            if (netSaving > 0) reasons.Add("ai.reason.netsaving");

            scored.Add(new RecommendedStationVm
            {
                Station = c,
                Price = price,
                DistanceKm = c.DistanceKm,
                DetourCostMkd = Math.Round(detourCost + timeCost, 1),
                SavingPerTank = Math.Round((decimal)grossSaving, 0),
                NetSaving = Math.Round((decimal)netSaving, 0),
                Score = Math.Round(Math.Clamp(score, 0, 1.5), 4),
                Reasons = reasons
            });
        }

        result.Items = scored.OrderByDescending(s => s.Score)
                             .ThenByDescending(s => s.NetSaving)
                             .Take(take)
                             .ToList();

        result.Explanation = "ai.reco.explained";
        return result;
    }

    public async Task<SavingsCalculatorVm> CalculateSavingsAsync(SavingsCalculatorVm input)
    {
        if (input.FuelTypeId <= 0)
            input.FuelTypeId = await _db.FuelTypes.OrderBy(f => f.SortOrder).Select(f => f.Id).FirstAsync();

        var prices = await _db.StationPrices
            .Where(p => p.FuelTypeId == input.FuelTypeId && p.IsCurrent)
            .Select(p => p.Price)
            .ToListAsync();

        if (prices.Count == 0) return input;

        input.MonthlyKm = Math.Clamp(input.MonthlyKm, 50, 20000);
        input.Consumption = Math.Clamp(input.Consumption, 2, 30);

        input.LitresPerMonth = Math.Round(input.MonthlyKm * input.Consumption / 100.0, 1);
        input.AvgPrice = Math.Round(prices.Average(), 2);
        input.BestPrice = prices.Min();

        input.MonthlyCostAverage = Math.Round(input.AvgPrice * (decimal)input.LitresPerMonth, 0);
        input.MonthlyCostBest = Math.Round(input.BestPrice * (decimal)input.LitresPerMonth, 0);
        input.MonthlySaving = input.MonthlyCostAverage - input.MonthlyCostBest;
        input.YearlySaving = input.MonthlySaving * 12;

        return input;
    }
}
