using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;
using ReFuel_Macedonia.Services.Ai;

namespace ReFuel_Macedonia.Controllers;

[Authorize]
public class OrdersController : Controller
{
    private readonly IOrderService _orders;
    private readonly IPriceService _prices;
    private readonly IPromotionService _promotions;
    private readonly IRecommendationService _recommendations;
    private readonly ITrackingService _tracking;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public OrdersController(
        IOrderService orders,
        IPriceService prices,
        IPromotionService promotions,
        IRecommendationService recommendations,
        ITrackingService tracking,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users)
    {
        _orders = orders;
        _prices = prices;
        _promotions = promotions;
        _recommendations = recommendations;
        _tracking = tracking;
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Index(OrderStatus? status, int page = 1)
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return Challenge();

        var vm = await _orders.ListAsync(userId, status, page);
        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var userId = _users.GetUserId(User);
        var order = await _orders.GetAsync(id, userId);
        if (order is null) return NotFound();
        return View(order);
    }

    public async Task<IActionResult> Create(int? stationId, int? fuelTypeId, double? litres, OrderType type = OrderType.Standard)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        // Address, city, phone and the pin start empty on purpose: the customer confirms where the
        // tanker should come and how to reach them on every order.
        var vm = new CreateOrderVm
        {
            StationId = stationId,
            Type = type,
            FuelTypeId = fuelTypeId ?? user.PreferredFuelTypeId ?? 0
        };

        if (litres is > 0) vm.Litres = Math.Clamp(litres.Value, 10, 120);

        await FillAsync(vm, user);

        if (vm.FuelTypeId <= 0) vm.FuelTypeId = vm.Fuels.FirstOrDefault()?.Id ?? 0;

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateOrderVm model, string? action)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        await FillAsync(model, user);

        // A price needs a place: without the pin there is no distance, no zone and no fee.
        if (!model.HasLocation)
            ModelState.AddModelError(nameof(model.DeliveryLatitude), "order.error.location");

        var isQuote = string.Equals(action, "quote", StringComparison.OrdinalIgnoreCase);

        if (isQuote)
        {
            // Recalculating only needs the place and the fuel; the rest is checked when the order is placed.
            model.Quote = model.HasLocation ? await _orders.QuoteAsync(model, user.Id) : null;

            foreach (var key in new[] { nameof(model.DeliveryAddress), nameof(model.DeliveryCity), nameof(model.ContactPhone) })
                ModelState.Remove(key);

            return View(model);
        }

        if (!ModelState.IsValid)
        {
            model.Quote = model.HasLocation ? await _orders.QuoteAsync(model, user.Id) : null;
            return View(model);
        }

        var (ok, messageKey, order) = await _orders.PlaceAsync(model, user.Id);

        if (!ok || order is null)
        {
            ModelState.AddModelError(string.Empty, messageKey);
            model.Quote = await _orders.QuoteAsync(model, user.Id);
            return View(model);
        }

        TempData["Message"] = "order.placed";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quote([FromForm] CreateOrderVm model)
    {
        var userId = _users.GetUserId(User);
        var quote = await _orders.QuoteAsync(model, userId);
        return Json(quote);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return Challenge();

        var ok = await _orders.CancelAsync(id, userId);
        TempData["Message"] = ok ? "order.cancelled" : "order.cancel.error";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Track(int id)
    {
        var userId = _users.GetUserId(User);
        var vm = await _tracking.GetTrackingAsync(id, userId, User.IsInRole(DbSeeder.RoleAdmin));
        if (vm is null) return NotFound();
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Live(int id)
    {
        var userId = _users.GetUserId(User);
        var vm = await _tracking.GetTrackingAsync(id, userId, User.IsInRole(DbSeeder.RoleAdmin));
        if (vm is null) return NotFound();

        object? driver = null;
        if (vm.Driver is not null)
        {
            driver = new
            {
                name = vm.Driver.DriverName,
                photo = vm.Driver.DriverPhotoUrl,
                phone = vm.Driver.Phone,
                rating = vm.Driver.DriverRating,
                truck = vm.Driver.TruckModel,
                plate = vm.Driver.PlateNumber,
                initials = vm.Driver.Initials
            };
        }

        return Json(new
        {
            status = vm.Order.Status.ToString(),
            statusKey = Infrastructure.Fmt.StatusKey(vm.Order.Status),
            progress = vm.ProgressPercent,
            currentLat = vm.CurrentLat,
            currentLng = vm.CurrentLng,
            remainingKm = vm.RemainingKm,
            remainingMinutes = vm.RemainingMinutes,
            etaUtc = vm.EtaUtc,
            driver
        });
    }

    private async Task FillAsync(CreateOrderVm vm, ApplicationUser user)
    {
        vm.Fuels = await _prices.GetFuelTypesAsync(onlyDeliverable: true);

        vm.Vehicles = await _db.Vehicles
            .Include(v => v.FuelType)
            .Where(v => v.UserId == user.Id)
            .OrderByDescending(v => v.IsDefault)
            .ToListAsync();

        vm.AvailablePromotions = await _promotions.GetLivePromotionsAsync(8);

        if (vm.FuelTypeId > 0)
        {
            var reco = await _recommendations.RecommendAsync(
                user.Id, vm.DeliveryLatitude, vm.DeliveryLongitude, vm.FuelTypeId, vm.Litres, null, 5);

            vm.SuggestedStations = reco.Items.Select(i => i.Station).ToList();
        }
    }
}
