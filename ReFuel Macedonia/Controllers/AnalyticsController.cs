using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;
using ReFuel_Macedonia.Services.Ai;

namespace ReFuel_Macedonia.Controllers;

public class AnalyticsController : Controller
{
    private readonly IPricePredictionService _predictions;
    private readonly IRecommendationService _recommendations;
    private readonly IPriceService _prices;
    private readonly UserManager<ApplicationUser> _users;

    public AnalyticsController(
        IPricePredictionService predictions,
        IRecommendationService recommendations,
        IPriceService prices,
        UserManager<ApplicationUser> users)
    {
        _predictions = predictions;
        _recommendations = recommendations;
        _prices = prices;
        _users = users;
    }

    public async Task<IActionResult> Index(int? fuelTypeId, double? lat, double? lng,
        double monthlyKm = 1200, double consumption = 6.5)
    {
        var fuels = await _prices.GetFuelTypesAsync();
        var selected = fuelTypeId ?? fuels.FirstOrDefault()?.Id ?? 0;

        var forecasts = new List<ForecastVm>();
        foreach (var fuel in fuels.Where(f => f.IsRegulated || f.Code == "LPG"))
            forecasts.Add(await _predictions.ForecastAsync(fuel.Id, 4));

        var vm = new AnalyticsVm
        {
            Fuels = fuels,
            SelectedFuelId = selected,
            Forecasts = forecasts,
            ByBrand = await _prices.GetBrandComparisonAsync(selected),
            ByCity = await _prices.GetCityComparisonAsync(selected, 20),
            Calculator = await _recommendations.CalculateSavingsAsync(new SavingsCalculatorVm
            {
                FuelTypeId = selected,
                MonthlyKm = monthlyKm,
                Consumption = consumption
            })
        };

        vm.Series.Add(await _prices.GetSeriesAsync(selected, 18));

        var userId = _users.GetUserId(User);
        if (userId is not null || (lat.HasValue && lng.HasValue))
            vm.Recommendation = await _recommendations.RecommendAsync(userId, lat, lng, selected, take: 8);

        return View(vm);
    }

    public async Task<IActionResult> Forecast(int fuelTypeId, int weeks = 6)
    {
        var forecast = await _predictions.ForecastAsync(fuelTypeId, Math.Clamp(weeks, 1, 12));
        ViewBag.Series = await _prices.GetSeriesAsync(fuelTypeId, 18);
        ViewBag.Fuels = await _prices.GetFuelTypesAsync();
        return View(forecast);
    }
}
