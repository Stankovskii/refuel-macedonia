using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;

namespace ReFuel_Macedonia.Controllers;

public class PricesController : Controller
{
    private readonly IPriceService _prices;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public PricesController(IPriceService prices, ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _prices = prices;
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Index(int? fuelTypeId, int months = 12)
    {
        var board = await _prices.GetBoardAsync();
        var selected = fuelTypeId ?? board.FirstOrDefault()?.FuelTypeId ?? 0;

        var vm = new PricesPageVm
        {
            Board = board,
            SelectedFuelId = selected,
            LastUpdateUtc = await _prices.GetLastUpdateAsync() ?? DateTime.UtcNow
        };

        if (selected > 0)
        {
            vm.Series.Add(await _prices.GetSeriesAsync(selected, months));
            vm.ByBrand = await _prices.GetBrandComparisonAsync(selected);
            vm.ByCity = await _prices.GetCityComparisonAsync(selected, 20);
        }

        return View(vm);
    }

    public async Task<IActionResult> History(int fuelTypeId, int months = 24)
    {
        var series = await _prices.GetSeriesAsync(fuelTypeId, months);
        ViewBag.Fuels = await _prices.GetFuelTypesAsync();
        ViewBag.Months = months;
        return View(series);
    }

    [Authorize]
    public async Task<IActionResult> Report(int stationId)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == stationId);
        if (station is null) return NotFound();

        var vm = new PriceReportVm
        {
            StationId = stationId,
            StationName = station.Name,
            Fuels = await _prices.GetFuelTypesAsync()
        };

        return View(vm);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Report(PriceReportVm model)
    {
        model.Fuels = await _prices.GetFuelTypesAsync();

        if (!ModelState.IsValid) return View(model);

        var userId = _users.GetUserId(User);
        if (userId is null) return Challenge();

        var (ok, messageKey, plausibility) = await _prices.SubmitReportAsync(model, userId);

        TempData["Message"] = messageKey;
        TempData["Plausibility"] = plausibility.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);

        if (!ok)
        {
            ModelState.AddModelError(string.Empty, messageKey);
            return View(model);
        }

        return RedirectToAction("Details", "Stations", new { id = model.StationId });
    }

    [Authorize]
    public async Task<IActionResult> MyReports()
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return Challenge();

        var reports = await _db.PriceReports
            .Include(r => r.Station)
            .Include(r => r.FuelType)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(50)
            .ToListAsync();

        return View(reports);
    }
}
