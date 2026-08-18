using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services.Ingestion;

namespace ReFuel_Macedonia.Controllers;

public class ConditionsController : Controller
{
    private readonly IWeatherService _weather;
    private readonly IRoadConditionService _roads;
    private readonly UserManager<ApplicationUser> _users;

    public ConditionsController(IWeatherService weather, IRoadConditionService roads, UserManager<ApplicationUser> users)
    {
        _weather = weather;
        _roads = roads;
        _users = users;
    }

    public async Task<IActionResult> Index(double? lat, double? lng)
    {
        var weather = await _weather.GetLatestAsync(20);
        var roads = await _roads.GetRoadsAsync();
        var borders = await _roads.GetBordersAsync();

        var vm = new ConditionsVm
        {
            Weather = weather.OrderBy(w => w.City).ToList(),
            Roads = roads,
            Borders = borders,
            HazardCount = weather.Count(w => w.IsHazardous) + roads.Count(r => r.Status != RoadStatus.Normal),
            LastUpdatedUtc = weather.Count > 0 ? weather.Max(w => w.ObservedAtUtc) : null
        };

        if (lat.HasValue && lng.HasValue)
        {
            vm.Local = await _weather.GetNearestAsync(lat.Value, lng.Value);
        }
        else
        {
            var user = await _users.GetUserAsync(User);
            if (user?.City is not null) vm.Local = await _weather.GetForCityAsync(user.City);
            vm.Local ??= await _weather.GetForCityAsync("Скопје");
        }

        return View(vm);
    }
}
