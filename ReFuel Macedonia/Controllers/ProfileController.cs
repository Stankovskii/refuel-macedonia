using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;

namespace ReFuel_Macedonia.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IPriceService _prices;
    private readonly IStationService _stations;
    private readonly INotificationService _notifications;

    public ProfileController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IPriceService prices,
        IStationService stations,
        INotificationService notifications)
    {
        _db = db;
        _users = users;
        _prices = prices;
        _stations = stations;
        _notifications = notifications;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        var delivered = await _db.Orders
            .Where(o => o.UserId == user.Id && o.Status == OrderStatus.Delivered)
            .Select(o => new { o.Total, o.Litres, o.DiscountAmount })
            .ToListAsync();

        var favoriteIds = await _db.FavoriteStations
            .Where(f => f.UserId == user.Id)
            .Select(f => f.StationId)
            .ToListAsync();

        var favorites = await _db.Stations
            .Where(s => favoriteIds.Contains(s.Id))
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
                IsVerified = s.IsVerified
            })
            .ToListAsync();

        var (next, needed) = NextTier(user.LoyaltyPoints);

        var vm = new ProfileVm
        {
            User = user,
            Vehicles = await _db.Vehicles.Include(v => v.FuelType).Where(v => v.UserId == user.Id).ToListAsync(),
            Favorites = favorites,
            Notifications = await _notifications.GetAsync(user.Id, 15),
            Fuels = await _prices.GetFuelTypesAsync(),
            OrderCount = await _db.Orders.CountAsync(o => o.UserId == user.Id),
            TotalSpent = delivered.Count == 0 ? 0 : delivered.Sum(o => o.Total),
            TotalLitres = delivered.Count == 0 ? 0 : Math.Round(delivered.Sum(o => o.Litres), 1),
            EstimatedSavings = delivered.Count == 0 ? 0 : delivered.Sum(o => o.DiscountAmount),
            NextTier = next,
            PointsToNextTier = needed
        };

        return View(vm);
    }

    private static (LoyaltyTier Next, int Needed) NextTier(int points) => points switch
    {
        < 700 => (LoyaltyTier.Silver, 700 - points),
        < 2000 => (LoyaltyTier.Gold, 2000 - points),
        < 4000 => (LoyaltyTier.Platinum, 4000 - points),
        _ => (LoyaltyTier.Platinum, 0)
    };

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        string fullName, string? phoneNumber, string? city, string? defaultAddress,
        double? homeLatitude, double? homeLongitude, int? preferredFuelTypeId,
        double maxDetourKm, double distanceWeight, string preferredLanguage,
        bool notifyPriceDrops, bool notifyPromotions)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        user.FullName = fullName ?? user.FullName;
        user.PhoneNumber = phoneNumber;
        user.City = city;
        user.DefaultAddress = defaultAddress;
        user.HomeLatitude = homeLatitude;
        user.HomeLongitude = homeLongitude;
        user.PreferredFuelTypeId = preferredFuelTypeId is > 0 ? preferredFuelTypeId : null;
        user.MaxDetourKm = Math.Clamp(maxDetourKm, 1, 60);
        user.DistanceWeight = Math.Clamp(distanceWeight, 0, 1);
        user.PreferredLanguage = SupportedCultures.Normalize(preferredLanguage);
        user.NotifyPriceDrops = notifyPriceDrops;
        user.NotifyPromotions = notifyPromotions;

        await _users.UpdateAsync(user);

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.PreferredLanguage)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, Path = "/" });

        TempData["Message"] = "profile.saved";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddVehicle(string make, string model, string? plateNumber, int? year,
        int fuelTypeId, double tankCapacityLitres, double avgConsumption, bool isDefault)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        if (string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model) || fuelTypeId <= 0)
        {
            TempData["Message"] = "vehicle.error";
            return RedirectToAction(nameof(Index));
        }

        if (isDefault)
        {
            foreach (var v in await _db.Vehicles.Where(v => v.UserId == user.Id).ToListAsync())
                v.IsDefault = false;
        }

        _db.Vehicles.Add(new Vehicle
        {
            UserId = user.Id,
            Make = make.Trim(),
            Model = model.Trim(),
            PlateNumber = plateNumber,
            Year = year,
            FuelTypeId = fuelTypeId,
            TankCapacityLitres = Math.Clamp(tankCapacityLitres, 10, 200),
            AvgConsumption = Math.Clamp(avgConsumption, 1, 40),
            IsDefault = isDefault
        });

        await _db.SaveChangesAsync();
        TempData["Message"] = "vehicle.saved";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVehicle(int id)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.UserId == user.Id);
        if (vehicle is not null)
        {
            _db.Vehicles.Remove(vehicle);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationsRead(int? id)
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return Challenge();

        await _notifications.MarkReadAsync(userId, id);
        return RedirectToAction(nameof(Index));
    }
}
