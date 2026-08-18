using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services;

namespace ReFuel_Macedonia.Controllers;

[Authorize]
public class BusinessController : Controller
{
    private readonly IBusinessService _business;
    private readonly ApplicationDbContext _db;
    private readonly IPricingService _pricing;
    private readonly UserManager<ApplicationUser> _users;

    public BusinessController(
        IBusinessService business,
        ApplicationDbContext db,
        IPricingService pricing,
        UserManager<ApplicationUser> users)
    {
        _business = business;
        _db = db;
        _pricing = pricing;
        _users = users;
    }

    private bool IsAdmin => User.IsInRole(DbSeeder.RoleAdmin);

    public async Task<IActionResult> Index(int? accountId)
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return Challenge();

        var account = accountId is > 0 && IsAdmin
            ? (await _business.GetAccountsAsync()).FirstOrDefault(a => a.Id == accountId)
            : await _business.GetAccountForUserAsync(userId);

        if (account is null && IsAdmin)
            account = (await _business.GetAccountsAsync()).FirstOrDefault();

        if (account is null)
        {
            ViewBag.Accounts = IsAdmin ? await _business.GetAccountsAsync() : new List<BusinessAccount>();
            return View("NoAccount");
        }

        var overview = await _business.GetOverviewAsync(account.Id);
        if (overview is null) return NotFound();

        ViewBag.Accounts = IsAdmin ? await _business.GetAccountsAsync() : new List<BusinessAccount>();
        return View(overview);
    }

    public async Task<IActionResult> Contract(int id)
    {
        var vm = await _business.GetContractAsync(id);
        if (vm is null) return NotFound();

        if (!IsAdmin)
        {
            var userId = _users.GetUserId(User);
            var account = await _business.GetAccountForUserAsync(userId!);
            if (account is null || account.Id != vm.Contract.BusinessAccountId) return Forbid();
        }

        return View(vm);
    }

    public async Task<IActionResult> Proof(int id)
    {
        var proof = await _business.GetProofAsync(id);
        if (proof is null) return NotFound();

        var owner = proof.ContractDelivery?.SupplyContract?.BusinessAccountId;
        if (!await CanSeeAccountAsync(owner)) return Forbid();

        return View(proof);
    }

    public async Task<IActionResult> Invoice(int id)
    {
        var invoice = await _business.GetInvoiceAsync(id);
        if (invoice is null) return NotFound();

        if (!await CanSeeAccountAsync(invoice.BusinessAccountId)) return Forbid();

        return View(invoice);
    }

    private async Task<bool> CanSeeAccountAsync(int? businessAccountId)
    {
        if (IsAdmin) return true;
        if (businessAccountId is not > 0) return false;

        var userId = _users.GetUserId(User);
        if (userId is null) return false;

        var own = await _business.GetAccountForUserAsync(userId);
        return own is not null && own.Id == businessAccountId;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = DbSeeder.RoleAdmin + "," + DbSeeder.RoleDriver)]
    public async Task<IActionResult> ConfirmDelivery(int id, double litres, string receivedBy, string driverName, int contractId)
    {
        var (ok, messageKey) = await _business.ConfirmDeliveryAsync(id, litres, receivedBy, driverName);
        TempData["Message"] = messageKey;
        return RedirectToAction(nameof(Contract), new { id = contractId });
    }

    /// <summary>The driver's screen at the depot: litres, who received, a hand-drawn signature.</summary>
    [Authorize(Roles = DbSeeder.RoleAdmin + "," + DbSeeder.RoleDriver)]
    public async Task<IActionResult> Confirm(int id)
    {
        var delivery = await _db.ContractDeliveries
            .Include(d => d.SupplyContract).ThenInclude(c => c!.BusinessAccount)
            .Include(d => d.ContractAsset)
            .Include(d => d.DeliveryVehicle)
            .Include(d => d.Proof)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (delivery is null) return NotFound();
        if (delivery.Proof is not null) return RedirectToAction(nameof(Proof), new { id });

        return View(delivery);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = DbSeeder.RoleAdmin + "," + DbSeeder.RoleDriver)]
    public async Task<IActionResult> Confirm(int id, double litres, string receivedBy, string driverName, string? signatureData, string? remarks)
    {
        if (string.IsNullOrWhiteSpace(signatureData) || !signatureData.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
        {
            TempData["Message"] = "confirm.error.signature";
            return RedirectToAction(nameof(Confirm), new { id });
        }

        var (ok, messageKey) = await _business.ConfirmDeliveryAsync(id, litres, receivedBy, driverName, signatureData, remarks);
        TempData["Message"] = messageKey;

        return ok
            ? RedirectToAction(nameof(Proof), new { id })
            : RedirectToAction(nameof(Confirm), new { id });
    }

    /// <summary>
    /// Public check of a printed document: the QR code on a proof of delivery or an invoice leads
    /// here. The hash is recomputed from what the database holds, so a document whose numbers were
    /// altered after printing no longer matches. Only the facts needed to compare are shown.
    /// </summary>
    [AllowAnonymous]
    public async Task<IActionResult> Verify(string? proof, string? invoice, string? h)
    {
        var vm = new VerifyDocumentVm { Hash = h?.Trim().ToUpperInvariant() };

        if (!string.IsNullOrWhiteSpace(proof))
        {
            vm.Kind = "proof";
            vm.Number = proof.Trim().ToUpperInvariant();

            var record = await _business.FindProofAsync(vm.Number);
            if (record is not null && record.ContractDelivery is not null)
            {
                var expected = BusinessHash.Compute(record.ProofNumber, record.ContractDelivery.Reference, record.LitresDelivered, record.IssuedAtUtc);
                vm.Found = true;

                // The code on the paper must be presented and must match: without it, only "exists".
                vm.IsValid = vm.Hash is not null
                             && string.Equals(expected, vm.Hash, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(expected, record.VerificationHash, StringComparison.OrdinalIgnoreCase);

                if (vm.IsValid)
                {
                    vm.IssuedOn = record.IssuedAtUtc;
                    vm.Litres = record.LitresDelivered;
                    vm.Company = record.ContractDelivery.SupplyContract?.BusinessAccount?.CompanyName;
                    vm.Reference = record.ContractDelivery.Reference;
                    vm.ExpectedHash = expected;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(invoice))
        {
            vm.Kind = "invoice";
            vm.Number = invoice.Trim().ToUpperInvariant();

            var record = await _db.Invoices.Include(i => i.BusinessAccount).FirstOrDefaultAsync(i => i.InvoiceNumber == vm.Number);
            if (record is not null)
            {
                var expected = InvoiceHash(record);
                vm.Found = true;
                vm.IsValid = vm.Hash is not null && string.Equals(expected, vm.Hash, StringComparison.OrdinalIgnoreCase);

                if (vm.IsValid)
                {
                    vm.IssuedOn = record.IssuedOn;
                    vm.Total = record.Total;
                    vm.Company = record.BusinessAccount?.CompanyName;
                    vm.ExpectedHash = expected;
                }
            }
        }

        return View(vm);
    }

    /// <summary>Invoices carry no stored hash; it is derived from the fields a reader can see on paper.</summary>
    public static string InvoiceHash(Invoice invoice)
        => BusinessHash.Compute(invoice.InvoiceNumber, invoice.Total, invoice.IssuedOn.Date);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = DbSeeder.RoleAdmin)]
    public async Task<IActionResult> GenerateInvoice(int contractId, DateTime periodStart, DateTime periodEnd)
    {
        var (ok, messageKey, invoice) = await _business.GenerateInvoiceAsync(contractId, periodStart, periodEnd);
        TempData["Message"] = messageKey;

        return ok && invoice is not null
            ? RedirectToAction(nameof(Invoice), new { id = invoice.Id })
            : RedirectToAction(nameof(Contract), new { id = contractId });
    }

    public async Task<IActionResult> Fleet(int? accountId)
    {
        var account = await ResolveAccountAsync(accountId);
        if (account is null) return View("NoAccount");

        var vm = await BuildFleetAsync(account);

        ViewBag.Accounts = IsAdmin ? await _business.GetAccountsAsync() : new List<BusinessAccount>();
        return View(vm);
    }

    public async Task<IActionResult> Sites(int? accountId)
    {
        var account = await ResolveAccountAsync(accountId);
        if (account is null) return View("NoAccount");

        ViewBag.Account = account;
        ViewBag.Fuels = await _db.FuelTypes.Where(f => f.IsDeliverable).OrderBy(f => f.SortOrder).ToListAsync();

        return View(await _db.ConstructionSites
            .Include(s => s.PrimaryFuelType)
            .Include(s => s.Machines)
            .Where(s => s.BusinessAccountId == account.Id)
            .OrderBy(s => s.Name)
            .ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSite(SiteFormVm model)
    {
        var account = await ResolveAccountAsync(model.BusinessAccountId);
        if (account is null) return Forbid();

        if (!ModelState.IsValid)
        {
            TempData["Message"] = "common.invalid";
            return RedirectToAction(nameof(Sites), new { accountId = account.Id });
        }

        var site = model.Id > 0
            ? await _db.ConstructionSites.FirstOrDefaultAsync(s => s.Id == model.Id && s.BusinessAccountId == account.Id)
            : new ConstructionSite { BusinessAccountId = account.Id };

        if (site is null) return NotFound();

        site.Name = model.Name;
        site.Address = model.Address ?? string.Empty;
        site.City = model.City ?? account.City;
        site.Latitude = model.Latitude;
        site.Longitude = model.Longitude;
        site.PrimaryFuelTypeId = model.PrimaryFuelTypeId is > 0 ? model.PrimaryFuelTypeId : null;
        site.WeeklyConsumptionLitres = Math.Max(0, model.WeeklyConsumptionLitres);
        site.ContactPerson = model.ContactPerson ?? string.Empty;
        site.ContactPhone = model.ContactPhone ?? string.Empty;
        site.AccessFrom = new TimeSpan(Math.Clamp(model.AccessFromHour, 0, 23), 0, 0);
        site.AccessTo = new TimeSpan(Math.Clamp(model.AccessToHour, 0, 23), 0, 0);
        site.Notes = model.Notes;

        if (model.Id <= 0) _db.ConstructionSites.Add(site);

        await _db.SaveChangesAsync();

        TempData["Message"] = "business.site.saved";
        return RedirectToAction(nameof(Sites), new { accountId = account.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSite(int id)
    {
        var site = await _db.ConstructionSites.FirstOrDefaultAsync(s => s.Id == id);
        if (site is null) return NotFound();

        var account = await ResolveAccountAsync(site.BusinessAccountId);
        if (account is null || account.Id != site.BusinessAccountId) return Forbid();

        site.IsActive = !site.IsActive;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Sites), new { accountId = site.BusinessAccountId });
    }

    public async Task<IActionResult> Schedules(int? accountId)
    {
        var account = await ResolveAccountAsync(accountId);
        if (account is null) return View("NoAccount");

        ViewBag.Account = account;
        ViewBag.Fuels = await _db.FuelTypes.Where(f => f.IsDeliverable).OrderBy(f => f.SortOrder).ToListAsync();
        ViewBag.Sites = await _db.ConstructionSites
            .Where(s => s.BusinessAccountId == account.Id && s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();

        return View(await _db.DeliverySchedules
            .Include(s => s.FuelType)
            .Include(s => s.ConstructionSite)
            .Where(s => s.BusinessAccountId == account.Id)
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.Title)
            .ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSchedule(ScheduleFormVm model)
    {
        var account = await ResolveAccountAsync(model.BusinessAccountId);
        if (account is null) return Forbid();

        if (!ModelState.IsValid)
        {
            TempData["Message"] = "common.invalid";
            return RedirectToAction(nameof(Schedules), new { accountId = account.Id });
        }

        var schedule = model.Id > 0
            ? await _db.DeliverySchedules.FirstOrDefaultAsync(s => s.Id == model.Id && s.BusinessAccountId == account.Id)
            : new DeliverySchedule { BusinessAccountId = account.Id, StartsOn = DateTime.UtcNow.Date };

        if (schedule is null) return NotFound();

        schedule.Title = model.Title;
        schedule.Kind = model.Kind;
        schedule.Frequency = model.Frequency;
        schedule.FuelTypeId = model.FuelTypeId;
        schedule.ConstructionSiteId = model.ConstructionSiteId is > 0 ? model.ConstructionSiteId : null;
        schedule.DaysOfWeek = model.Days.Count == 0 ? "1,3,5" : string.Join(",", model.Days.Distinct().OrderBy(d => d));
        schedule.WindowStart = new TimeSpan(Math.Clamp(model.WindowStartHour, 0, 23), 0, 0);
        schedule.WindowEnd = new TimeSpan(Math.Clamp(model.WindowEndHour, 0, 23), 0, 0);
        schedule.LitresPerVisit = Math.Max(0, model.LitresPerVisit);
        schedule.Address = model.Address ?? account.Address;
        schedule.City = model.City ?? account.City;
        schedule.Latitude = model.Latitude;
        schedule.Longitude = model.Longitude;
        schedule.TriggerBelowPercent = model.Kind == ScheduleKind.SensorTriggered ? model.TriggerBelowPercent : null;

        if (model.Id <= 0) _db.DeliverySchedules.Add(schedule);

        await _db.SaveChangesAsync();

        TempData["Message"] = "business.schedule.saved";
        return RedirectToAction(nameof(Schedules), new { accountId = account.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSchedule(int id)
    {
        var schedule = await _db.DeliverySchedules.FirstOrDefaultAsync(s => s.Id == id);
        if (schedule is null) return NotFound();

        var account = await ResolveAccountAsync(schedule.BusinessAccountId);
        if (account is null || account.Id != schedule.BusinessAccountId) return Forbid();

        schedule.IsActive = !schedule.IsActive;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Schedules), new { accountId = schedule.BusinessAccountId });
    }

    [AllowAnonymous]
    public async Task<IActionResult> Savings(SavingsInputVm input)
    {
        if (input.LitresPerMonth <= 0) input = new SavingsInputVm();

        var diesel = await _db.FuelTypes.FirstOrDefaultAsync(f => f.Code == "EURODIESEL");

        if (diesel is not null && input.StationPricePerLitre <= 0)
        {
            var price = await _db.NationalPrices
                .Where(p => p.FuelTypeId == diesel.Id)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync();

            if (price is not null)
            {
                input.StationPricePerLitre = price.Price;
                input.ReFuelPricePerLitre = Math.Round(price.Price + 2.5m, 2);
            }
        }

        return View(_pricing.CalculateSavings(input));
    }

    private async Task<BusinessAccount?> ResolveAccountAsync(int? accountId)
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return null;

        if (accountId is > 0 && IsAdmin)
            return await _db.BusinessAccounts.FirstOrDefaultAsync(a => a.Id == accountId);

        var own = await _business.GetAccountForUserAsync(userId);
        if (own is not null) return own;

        if (accountId is > 0)
        {
            var requested = await _db.BusinessAccounts.FirstOrDefaultAsync(a => a.Id == accountId);
            if (requested is not null && requested.OwnerUserId == userId) return requested;
        }

        return IsAdmin ? await _db.BusinessAccounts.OrderBy(a => a.Id).FirstOrDefaultAsync() : null;
    }

    private async Task<FleetDashboardVm> BuildFleetAsync(BusinessAccount account)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var previousStart = monthStart.AddMonths(-1);

        var contracts = await _db.SupplyContracts
            .Include(c => c.FuelType)
            .Where(c => c.BusinessAccountId == account.Id)
            .OrderByDescending(c => c.Status == ContractStatus.Active)
            .ToListAsync();

        var contractIds = contracts.Select(c => c.Id).ToList();

        var deliveries = await _db.ContractDeliveries
            .Include(d => d.SupplyContract).ThenInclude(c => c!.FuelType)
            .Include(d => d.ContractAsset)
            .Where(d => contractIds.Contains(d.SupplyContractId))
            .OrderByDescending(d => d.ScheduledForUtc)
            .Take(400)
            .ToListAsync();

        var assets = await _db.ContractAssets
            .Include(a => a.ConstructionSite)
            .Where(a => contractIds.Contains(a.SupplyContractId))
            .ToListAsync();

        var sites = await _db.ConstructionSites
            .Include(s => s.Machines)
            .Where(s => s.BusinessAccountId == account.Id)
            .ToListAsync();

        var schedules = await _db.DeliverySchedules
            .Include(s => s.FuelType)
            .Where(s => s.BusinessAccountId == account.Id)
            .ToListAsync();

        var invoices = await _db.Invoices
            .Where(i => i.BusinessAccountId == account.Id)
            .ToListAsync();

        var thisMonth = deliveries.Where(d => d.ScheduledForUtc >= monthStart && d.Status == OrderStatus.Delivered).ToList();
        var lastMonth = deliveries.Where(d => d.ScheduledForUtc >= previousStart && d.ScheduledForUtc < monthStart && d.Status == OrderStatus.Delivered).ToList();

        var litresThisMonth = thisMonth.Sum(d => d.DeliveredLitres);
        var spendThisMonth = thisMonth.Sum(d => d.LineTotal);

        var tier = account.PricingTierId is int tierId
            ? await _pricing.GetTierAsync(tierId)
            : await _pricing.ResolveTierAsync(account.CustomerType);

        var monthly = await _pricing.GetMonthlyLitresAsync(null, account.Id);
        var bands = await _pricing.GetBandsAsync(tier?.Id);
        var band = bands.FirstOrDefault(b => b.Covers(monthly));

        var discount = contracts.Where(c => c.Status == ContractStatus.Active)
            .Select(c => c.DiscountPerLitre)
            .DefaultIfEmpty(0m)
            .Max();

        var byMonth = deliveries
            .Where(d => d.Status == OrderStatus.Delivered && d.ScheduledForUtc >= DateTime.UtcNow.AddMonths(-8))
            .GroupBy(d => new { d.ScheduledForUtc.Year, d.ScheduledForUtc.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .ToDictionary(
                g => $"{g.Key.Month:D2}.{g.Key.Year}",
                g => Math.Round(g.Sum(d => d.DeliveredLitres), 0));

        var byKind = assets
            .GroupBy(a => a.Kind)
            .ToDictionary(
                g => g.Key.ToString(),
                g => Math.Round(g.Sum(a => a.AverageLitresPerWeek > 0 ? a.AverageLitresPerWeek : a.TankCapacityLitres / 2), 0));

        return new FleetDashboardVm
        {
            Account = account,
            Contracts = contracts,
            Assets = assets,
            Sites = sites,
            Schedules = schedules,
            UpcomingDeliveries = deliveries
                .Where(d => d.ScheduledForUtc >= DateTime.UtcNow && d.Status != OrderStatus.Delivered)
                .OrderBy(d => d.ScheduledForUtc)
                .Take(12)
                .ToList(),
            Alerts = await _db.FuelAnomalies
                .Where(a => a.BusinessAccountId == account.Id && a.Status == AnomalyStatus.Open)
                .OrderByDescending(a => a.DetectedAtUtc)
                .Take(6)
                .ToListAsync(),
            LitresThisMonth = Math.Round(litresThisMonth, 0),
            LitresLastMonth = Math.Round(lastMonth.Sum(d => d.DeliveredLitres), 0),
            SpendThisMonth = spendThisMonth,
            AveragePricePerLitre = litresThisMonth <= 0 ? 0 : Math.Round(spendThisMonth / (decimal)litresThisMonth, 2),
            DiscountPerLitre = discount,
            SavedByDiscount = Math.Round(discount * (decimal)litresThisMonth, 0),
            ActiveAssets = assets.Count(a => a.IsActive),
            ActiveSites = sites.Count(s => s.IsActive),
            OpenInvoices = invoices.Count(i => i.Status == InvoiceStatus.Issued),
            OpenInvoiceTotal = invoices.Where(i => i.Status == InvoiceStatus.Issued).Sum(i => i.Total),
            Tier = tier,
            VolumeBandLabel = band?.LocalizedLabel(
                SupportedCultures.Normalize(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)),
            LitresByMonth = byMonth,
            LitresByAssetKind = byKind
        };
    }
}
