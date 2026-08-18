using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services;

public interface IStationService
{
    Task<StationSearchVm> SearchAsync(StationSearchVm query);
    Task<StationDetailsVm?> GetDetailsAsync(int id, string? userId, int? fuelTypeId = null);
    Task<List<MapStationDto>> GetMapDataAsync(int? fuelTypeId, string? city = null, int? brandId = null);
    Task<List<string>> GetCitiesAsync();
    Task<List<Brand>> GetBrandsAsync();
    Task<bool> ToggleFavoriteAsync(int stationId, string userId);
    Task<bool> AddReviewAsync(int stationId, string userId, int rating, string? comment);
    Task<List<StationListItemVm>> GetNearbyAsync(double lat, double lon, double radiusKm, int? fuelTypeId, int take = 20);
}

public class StationService : IStationService
{
    private readonly ApplicationDbContext _db;

    public StationService(ApplicationDbContext db) => _db = db;

    public async Task<List<string>> GetCitiesAsync()
        => await _db.Stations.Where(s => s.IsActive)
                             .Select(s => s.City)
                             .Distinct()
                             .OrderBy(c => c)
                             .ToListAsync();

    public async Task<List<Brand>> GetBrandsAsync()
        => await _db.Brands.OrderBy(b => b.Name).ToListAsync();

    public async Task<StationSearchVm> SearchAsync(StationSearchVm q)
    {
        q.Brands = await GetBrandsAsync();
        q.Fuels = await _db.FuelTypes.OrderBy(f => f.SortOrder).ToListAsync();
        q.Cities = await GetCitiesAsync();

        if (q.FuelTypeId is null or 0)
            q.FuelTypeId = q.Fuels.FirstOrDefault()?.Id;

        var stations = _db.Stations.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(q.City)) stations = stations.Where(s => s.City == q.City);
        if (q.BrandId is > 0) stations = stations.Where(s => s.BrandId == q.BrandId);
        if (q.Open24h) stations = stations.Where(s => s.IsOpen24h);
        if (q.WithLpg) stations = stations.Where(s => s.HasLpg);
        if (q.WithShop) stations = stations.Where(s => s.HasShop);
        if (q.WithEv) stations = stations.Where(s => s.HasEvCharger);

        if (q.Latitude.HasValue && q.Longitude.HasValue && q.RadiusKm > 0)
        {
            var box = GeoUtils.BoundingBox(q.Latitude.Value, q.Longitude.Value, q.RadiusKm);
            stations = stations.Where(s => s.Latitude >= box.MinLat && s.Latitude <= box.MaxLat
                                        && s.Longitude >= box.MinLon && s.Longitude <= box.MaxLon);
        }

        var fuelId = q.FuelTypeId ?? 0;

        var rows = await stations
            .Select(s => new StationListItemVm
            {
                Id = s.Id,
                Name = s.Name,
                SearchKey = s.SearchKey,
                Brand = s.Brand != null ? s.Brand.Name : null,
                BrandColor = s.Brand != null ? s.Brand.ColorHex : "#64748B",
                City = s.City,
                Address = s.Address,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                IsOpen24h = s.IsOpen24h,
                HasLpg = s.HasLpg,
                HasShop = s.HasShop,
                HasCarWash = s.HasCarWash,
                HasEvCharger = s.HasEvCharger,
                IsVerified = s.IsVerified,
                ReviewCount = s.Reviews.Count(),
                Price = s.Prices.Where(p => p.FuelTypeId == fuelId && p.IsCurrent)
                                .Select(p => (decimal?)p.Price)
                                .FirstOrDefault(),
                PriceObservedAtUtc = s.Prices.Where(p => p.FuelTypeId == fuelId && p.IsCurrent)
                                             .Select(p => (DateTime?)p.ObservedAtUtc)
                                             .FirstOrDefault()
            })
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(q.Query))
        {
            rows = rows.Where(r =>
            {
                var key = string.IsNullOrEmpty(r.SearchKey)
                    ? Translit.BuildSearchKey(r.Name, r.Brand, r.City, r.Address)
                    : r.SearchKey;

                return Translit.Matches(key, q.Query!);
            }).ToList();
        }

        if (q.Latitude.HasValue && q.Longitude.HasValue)
        {
            foreach (var r in rows)
                r.DistanceKm = Math.Round(GeoUtils.HaversineKm(q.Latitude.Value, q.Longitude.Value, r.Latitude, r.Longitude), 2);

            if (q.RadiusKm > 0)
                rows = rows.Where(r => r.DistanceKm <= q.RadiusKm).ToList();
        }

        rows = q.Sort switch
        {
            "distance" => rows.OrderBy(r => r.DistanceKm).ThenBy(r => r.Price ?? decimal.MaxValue).ToList(),
            "name" => rows.OrderBy(r => r.Name).ToList(),
            "city" => rows.OrderBy(r => r.City).ThenBy(r => r.Name).ToList(),
            _ => rows.OrderBy(r => r.Price ?? decimal.MaxValue).ThenBy(r => r.DistanceKm).ToList()
        };

        q.TotalCount = rows.Count;
        if (q.Page < 1) q.Page = 1;
        if (q.PageSize is < 5 or > 200) q.PageSize = 20;

        q.Results = rows.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();
        return q;
    }

    public async Task<StationDetailsVm?> GetDetailsAsync(int id, string? userId, int? fuelTypeId = null)
    {
        var station = await _db.Stations
            .Include(s => s.Brand)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (station is null) return null;

        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var fuels = await _db.FuelTypes.OrderBy(f => f.SortOrder).ToListAsync();

        var current = await _db.StationPrices
            .Where(p => p.StationId == id && p.IsCurrent)
            .ToListAsync();

        var nationalWindow = await _db.NationalPrices
            .Where(p => p.EffectiveFrom >= DateTime.UtcNow.Date.AddDays(-120))
            .ToListAsync();

        var nationalLatest = nationalWindow
            .GroupBy(p => p.FuelTypeId)
            .Select(g => g.OrderByDescending(x => x.EffectiveFrom).First())
            .ToList();

        var prices = new List<StationFuelPriceVm>();
        foreach (var p in current.OrderBy(p => fuels.FirstOrDefault(f => f.Id == p.FuelTypeId)?.SortOrder ?? 99))
        {
            var fuel = fuels.FirstOrDefault(f => f.Id == p.FuelTypeId);
            if (fuel is null) continue;

            var national = nationalLatest.FirstOrDefault(n => n.FuelTypeId == fuel.Id)?.Price ?? p.Price;

            prices.Add(new StationFuelPriceVm
            {
                FuelTypeId = fuel.Id,
                Fuel = fuel.LocalizedName(culture),
                Unit = fuel.Unit,
                Color = fuel.ColorHex,
                Price = p.Price,
                NationalMax = national,
                DiffToNational = Math.Round(p.Price - national, 2),
                ObservedAtUtc = p.ObservedAtUtc,
                Source = p.Source,
                Confidence = p.Confidence
            });
        }

        var historyFuelId = fuelTypeId ?? prices.FirstOrDefault()?.FuelTypeId ?? fuels.First().Id;

        var history = await _db.StationPrices
            .Where(p => p.StationId == id && p.FuelTypeId == historyFuelId)
            .OrderBy(p => p.ObservedAtUtc)
            .Select(p => new PriceHistoryPointVm { Date = p.ObservedAtUtc, Price = p.Price })
            .ToListAsync();

        var reviews = await _db.StationReviews
            .Include(r => r.User)
            .Where(r => r.StationId == id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(20)
            .ToListAsync();

        var nearby = await GetNearbyAsync(station.Latitude, station.Longitude, 12, historyFuelId, 8);
        nearby = nearby.Where(n => n.Id != id).ToList();

        var vm = new StationDetailsVm
        {
            Station = station,
            Prices = prices,
            History = history,
            HistoryFuelTypeId = historyFuelId,
            Reviews = reviews,
            Nearby = nearby,
            AverageRating = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 2) : 0,
            IsFavorite = userId is not null &&
                         await _db.FavoriteStations.AnyAsync(f => f.StationId == id && f.UserId == userId)
        };

        var here = prices.FirstOrDefault(p => p.FuelTypeId == historyFuelId)?.Price;
        var best = nearby.Where(n => n.Price.HasValue).OrderBy(n => n.Price).FirstOrDefault();

        if (here.HasValue && best?.Price is not null && best.Price < here)
        {
            vm.CheapestNearbyPrice = best.Price.Value;
            vm.CheapestNearbyName = best.Name;
            vm.PotentialSavingPerTank = Math.Round((here.Value - best.Price.Value) * 50m, 0);
        }

        return vm;
    }

    public async Task<List<StationListItemVm>> GetNearbyAsync(double lat, double lon, double radiusKm, int? fuelTypeId, int take = 20)
    {
        var box = GeoUtils.BoundingBox(lat, lon, radiusKm);
        var fuelId = fuelTypeId ?? 0;

        var rows = await _db.Stations
            .Where(s => s.IsActive
                     && s.Latitude >= box.MinLat && s.Latitude <= box.MaxLat
                     && s.Longitude >= box.MinLon && s.Longitude <= box.MaxLon)
            .Select(s => new StationListItemVm
            {
                Id = s.Id,
                Name = s.Name,
                Brand = s.Brand != null ? s.Brand.Name : null,
                BrandColor = s.Brand != null ? s.Brand.ColorHex : "#64748B",
                City = s.City,
                Address = s.Address,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                IsOpen24h = s.IsOpen24h,
                HasLpg = s.HasLpg,
                HasShop = s.HasShop,
                HasEvCharger = s.HasEvCharger,
                IsVerified = s.IsVerified,
                Price = s.Prices.Where(p => p.FuelTypeId == fuelId && p.IsCurrent)
                                .Select(p => (decimal?)p.Price)
                                .FirstOrDefault()
            })
            .ToListAsync();

        foreach (var r in rows)
            r.DistanceKm = Math.Round(GeoUtils.HaversineKm(lat, lon, r.Latitude, r.Longitude), 2);

        return rows.Where(r => r.DistanceKm <= radiusKm)
                   .OrderBy(r => r.DistanceKm)
                   .Take(take)
                   .ToList();
    }

    public async Task<List<MapStationDto>> GetMapDataAsync(int? fuelTypeId, string? city = null, int? brandId = null)
    {
        var fuelId = fuelTypeId ?? 0;
        var fuelName = await _db.FuelTypes.Where(f => f.Id == fuelId).Select(f => f.Code).FirstOrDefaultAsync();

        var query = _db.Stations.Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(s => s.City == city);
        if (brandId is > 0) query = query.Where(s => s.BrandId == brandId);

        return await query
            .Select(s => new MapStationDto
            {
                Id = s.Id,
                Name = s.Name,
                Brand = s.Brand != null ? s.Brand.Name : null,
                Color = s.Brand != null ? s.Brand.ColorHex : "#64748B",
                City = s.City,
                Lat = s.Latitude,
                Lng = s.Longitude,
                Open24h = s.IsOpen24h,
                Lpg = s.HasLpg,
                Verified = s.IsVerified,
                Fuel = fuelName,
                Price = s.Prices.Where(p => p.FuelTypeId == fuelId && p.IsCurrent)
                                .Select(p => (decimal?)p.Price)
                                .FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<bool> ToggleFavoriteAsync(int stationId, string userId)
    {
        var existing = await _db.FavoriteStations
            .FirstOrDefaultAsync(f => f.StationId == stationId && f.UserId == userId);

        if (existing is not null)
        {
            _db.FavoriteStations.Remove(existing);
            await _db.SaveChangesAsync();
            return false;
        }

        _db.FavoriteStations.Add(new FavoriteStation { StationId = stationId, UserId = userId });
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AddReviewAsync(int stationId, string userId, int rating, string? comment)
    {
        if (rating is < 1 or > 5) return false;

        var existing = await _db.StationReviews
            .FirstOrDefaultAsync(r => r.StationId == stationId && r.UserId == userId);

        if (existing is not null)
        {
            existing.Rating = rating;
            existing.Comment = comment;
            existing.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _db.StationReviews.Add(new StationReview
            {
                StationId = stationId,
                UserId = userId,
                Rating = rating,
                Comment = comment
            });
        }

        await _db.SaveChangesAsync();
        return true;
    }
}
