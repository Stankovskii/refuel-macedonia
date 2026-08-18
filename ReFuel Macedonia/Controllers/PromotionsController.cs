using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Services;

namespace ReFuel_Macedonia.Controllers;

public class PromotionsController : Controller
{
    private readonly IPromotionService _promotions;
    private readonly UserManager<ApplicationUser> _users;

    public PromotionsController(IPromotionService promotions, UserManager<ApplicationUser> users)
    {
        _promotions = promotions;
        _users = users;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _users.GetUserId(User);
        var vm = await _promotions.GetPageAsync(userId);
        return View(vm);
    }
}
