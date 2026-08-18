using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Services;

namespace ReFuel_Macedonia.Controllers;

[Authorize(Roles = DbSeeder.RoleAdmin + "," + DbSeeder.RoleDriver)]
public class DispatchController : Controller
{
    private readonly IDispatchService _dispatch;
    private readonly IFleetService _fleet;
    private readonly ApplicationDbContext _db;

    public DispatchController(IDispatchService dispatch, IFleetService fleet, ApplicationDbContext db)
    {
        _dispatch = dispatch;
        _fleet = fleet;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _dispatch.GetBoardAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuildRoutes()
    {
        var routes = await _dispatch.BuildZoneRoutesAsync(persist: true);
        TempData["Message"] = "dispatch.routes.saved";
        TempData["Detail"] = routes.Count(r => r.Stops.Count > 0).ToString();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int orderId, int tankerId)
    {
        var ok = await _dispatch.AssignAsync(orderId, tankerId);
        TempData["Message"] = ok ? "dispatch.assigned" : "dispatch.assign.error";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Tankers()
    {
        ViewBag.Fuels = await _db.FuelTypes.Where(f => f.IsDeliverable).OrderBy(f => f.SortOrder).ToListAsync();
        return View(await _fleet.GetTankersAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCompartment(int id, double litres, int? fuelTypeId)
    {
        await _fleet.SetCompartmentAsync(id, litres, fuelTypeId);
        TempData["Message"] = "dispatch.compartment.saved";
        return RedirectToAction(nameof(Tankers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refill(int id)
    {
        var count = await _fleet.RefillTankerAsync(id);
        TempData["Message"] = "dispatch.refilled";
        TempData["Detail"] = count.ToString();
        return RedirectToAction(nameof(Tankers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleMaintenance(int id)
    {
        await _fleet.ToggleMaintenanceAsync(id);
        return RedirectToAction(nameof(Tankers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = DbSeeder.RoleAdmin)]
    public async Task<IActionResult> SimulateTick()
    {
        var count = await _fleet.SimulateTickAsync();
        TempData["Message"] = "dispatch.simulated";
        TempData["Detail"] = count.ToString();
        return RedirectToAction(nameof(Tankers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = DbSeeder.RoleAdmin)]
    public async Task<IActionResult> RunDetection()
    {
        var count = await _fleet.DetectAnomaliesAsync();
        TempData["Message"] = "dispatch.detection.done";
        TempData["Detail"] = count.ToString();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Live()
    {
        var tankers = await _fleet.GetTankersAsync();

        return Json(tankers.Select(t => new
        {
            id = t.Tanker.Id,
            code = t.Tanker.Code,
            driver = t.Tanker.DriverName,
            plate = t.Tanker.PlateNumber,
            lat = t.Tanker.CurrentLatitude == 0 ? t.Tanker.BaseLatitude : t.Tanker.CurrentLatitude,
            lng = t.Tanker.CurrentLongitude == 0 ? t.Tanker.BaseLongitude : t.Tanker.CurrentLongitude,
            speed = t.Tanker.SpeedKph,
            pump = t.Tanker.PumpActive,
            litres = t.Tanker.LoadedLitres,
            capacity = t.Tanker.CompartmentCapacityLitres,
            zone = t.ZoneName,
            color = t.ZoneColor ?? "#FF6A00",
            fresh = t.Tanker.TelemetryIsFresh,
            maintenance = t.Tanker.IsInMaintenance,
            orders = t.OpenOrders
        }));
    }
}
