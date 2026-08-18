using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;

namespace ReFuel_Macedonia.Controllers;

public class StationsController : Controller
{
    private readonly IStationService _stations;
    private readonly IPriceService _prices;
    private readonly UserManager<ApplicationUser> _users;

    public StationsController(IStationService stations, IPriceService prices, UserManager<ApplicationUser> users)
    {
        _stations = stations;
        _prices = prices;
        _users = users;
    }

    public async Task<IActionResult> Index([FromQuery] StationSearchVm query)
    {
        query ??= new StationSearchVm();
        var result = await _stations.SearchAsync(query);
        return View(result);
    }

    public async Task<IActionResult> Map(int? fuelTypeId, string? city)
    {
        ViewBag.Fuels = await _prices.GetFuelTypesAsync();
        ViewBag.Cities = await _stations.GetCitiesAsync();
        ViewBag.Brands = await _stations.GetBrandsAsync();
        ViewBag.SelectedFuel = fuelTypeId ?? (await _prices.GetFuelTypesAsync()).FirstOrDefault()?.Id ?? 0;
        ViewBag.SelectedCity = city;
        return View();
    }

    public async Task<IActionResult> Details(int id, int? fuelTypeId)
    {
        var userId = _users.GetUserId(User);
        var vm = await _stations.GetDetailsAsync(id, userId, fuelTypeId);
        if (vm is null) return NotFound();
        return View(vm);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFavorite(int id, string? returnUrl)
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return Challenge();

        var isFavorite = await _stations.ToggleFavoriteAsync(id, userId);
        TempData["Message"] = isFavorite ? "station.favorite.added" : "station.favorite.removed";

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, int rating, string? comment)
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return Challenge();

        var ok = await _stations.AddReviewAsync(id, userId, rating, comment);
        TempData["Message"] = ok ? "station.review.saved" : "station.review.error";
        return RedirectToAction(nameof(Details), new { id });
    }
}
