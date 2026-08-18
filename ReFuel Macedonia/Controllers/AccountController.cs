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

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ApplicationDbContext _db;
    private readonly IPricingService _pricing;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        ApplicationDbContext db,
        IPricingService pricing,
        ILogger<AccountController> logger)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
        _pricing = pricing;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginVm { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _signIn.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _users.FindByEmailAsync(model.Email);
            if (user is not null)
            {
                user.LastLoginUtc = DateTime.UtcNow;
                await _users.UpdateAsync(user);
                ApplyLanguage(user.PreferredLanguage);
            }

            _logger.LogInformation("User {Email} signed in.", model.Email);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return LocalRedirect(model.ReturnUrl);

            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "account.error.lockout");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "account.error.invalid");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
        => View(new RegisterVm { ReturnUrl = returnUrl, PreferredLanguage = SupportedCultures.Normalize(
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterVm model)
    {
        if (!model.AcceptTerms)
            ModelState.AddModelError(nameof(model.AcceptTerms), "account.error.terms");

        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            FullName = model.FullName,
            PhoneNumber = model.PhoneNumber,
            City = model.City,
            DefaultAddress = model.City,
            PreferredLanguage = SupportedCultures.Normalize(model.PreferredLanguage),
            CustomerType = model.CustomerType
        };

        var result = await _users.CreateAsync(user, model.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _users.AddToRoleAsync(user, DbSeeder.RoleCustomer);

        if (model.CustomerType != CustomerType.Individual && !string.IsNullOrWhiteSpace(model.CompanyName))
        {
            var account = await CreateBusinessAccountAsync(user, model);

            if (account is not null)
            {
                user.BusinessAccountId = account.Id;
                await _users.UpdateAsync(user);
                await _users.AddToRoleAsync(user, DbSeeder.RoleBusiness);
            }
        }

        await _signIn.SignInAsync(user, isPersistent: true);
        ApplyLanguage(user.PreferredLanguage);

        _logger.LogInformation("New account created for {Email}.", model.Email);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return LocalRedirect(model.ReturnUrl);

        TempData["Message"] = "account.welcome";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Denied() => View();

    private async Task<BusinessAccount?> CreateBusinessAccountAsync(ApplicationUser user, RegisterVm model)
    {
        var taxNumber = string.IsNullOrWhiteSpace(model.TaxNumber)
            ? "MK" + DateTime.UtcNow.Ticks.ToString()[..11]
            : model.TaxNumber.Trim();

        if (await _db.BusinessAccounts.AnyAsync(a => a.TaxNumber == taxNumber)) return null;

        var tier = await _pricing.ResolveTierAsync(model.CustomerType);

        var account = new BusinessAccount
        {
            CompanyName = model.CompanyName!.Trim(),
            TaxNumber = taxNumber,
            Address = model.City ?? string.Empty,
            City = model.City ?? string.Empty,
            ContactPerson = model.FullName,
            ContactEmail = model.Email,
            ContactPhone = model.PhoneNumber ?? string.Empty,
            CustomerType = model.CustomerType,
            PricingTierId = tier.Id > 0 ? tier.Id : null,
            OwnerUserId = user.Id,
            IsActive = true
        };

        _db.BusinessAccounts.Add(account);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Business account {Company} created during registration.", account.CompanyName);
        return account;
    }

    private void ApplyLanguage(string language)
    {
        var culture = SupportedCultures.Normalize(language);
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, Path = "/" });
    }
}
