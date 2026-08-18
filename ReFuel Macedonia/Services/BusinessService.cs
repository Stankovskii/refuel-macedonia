using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services;

public interface IBusinessService
{
    Task<List<BusinessAccount>> GetAccountsAsync();
    Task<BusinessAccount?> GetAccountForUserAsync(string userId);
    Task<BusinessOverviewVm?> GetOverviewAsync(int accountId);
    Task<ContractDetailsVm?> GetContractAsync(int contractId);
    Task<List<ContractDelivery>> GetUpcomingAsync(int accountId, int take = 10);
    Task<DeliveryProof?> GetProofAsync(int deliveryId);
    Task<Invoice?> GetInvoiceAsync(int invoiceId);
    Task<List<Invoice>> GetInvoicesAsync(int accountId);
    Task<(bool Ok, string MessageKey)> ConfirmDeliveryAsync(int deliveryId, double litres, string receivedBy, string driverName,
        string? signatureData = null, string? remarks = null);
    Task<DeliveryProof?> FindProofAsync(string proofNumber);
    Task<(bool Ok, string MessageKey, Invoice? Invoice)> GenerateInvoiceAsync(int contractId, DateTime periodStart, DateTime periodEnd);
    Task<int> GenerateUpcomingDeliveriesAsync(int days = 7);
}

public class BusinessService : IBusinessService
{
    private readonly ApplicationDbContext _db;
    private readonly IPricingService _pricing;
    private readonly ReFuelOptions _options;
    private readonly ILogger<BusinessService> _logger;

    public BusinessService(ApplicationDbContext db, IPricingService pricing, IOptions<ReFuelOptions> options, ILogger<BusinessService> logger)
    {
        _db = db;
        _pricing = pricing;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>What a delivery saved the customer against the pump price: exact when the reference price was recorded, the contract discount otherwise.</summary>
    private static decimal SavedOn(ContractDelivery d, decimal fallbackDiscount)
        => Math.Round((d.ReferencePricePerLitre > 0 ? d.DiscountPerLitre : fallbackDiscount) * (decimal)d.DeliveredLitres, 2);

    public Task<List<BusinessAccount>> GetAccountsAsync()
        => _db.BusinessAccounts.Include(a => a.Contracts).OrderBy(a => a.CompanyName).ToListAsync();

    public Task<BusinessAccount?> GetAccountForUserAsync(string userId)
        => _db.BusinessAccounts.Include(a => a.Contracts).FirstOrDefaultAsync(a => a.OwnerUserId == userId);

    public async Task<BusinessOverviewVm?> GetOverviewAsync(int accountId)
    {
        var account = await _db.BusinessAccounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return null;

        var contracts = await _db.SupplyContracts
            .Include(c => c.FuelType)
            .Include(c => c.Assets)
            .Where(c => c.BusinessAccountId == accountId)
            .ToListAsync();

        var contractIds = contracts.Select(c => c.Id).ToList();

        var deliveries = await _db.ContractDeliveries
            .Include(d => d.ContractAsset)
            .Include(d => d.DeliveryVehicle)
            .Include(d => d.SupplyContract)
            .Where(d => contractIds.Contains(d.SupplyContractId))
            .ToListAsync();

        var invoices = await _db.Invoices
            .Where(i => i.BusinessAccountId == accountId)
            .OrderByDescending(i => i.IssuedOn)
            .ToListAsync();

        var delivered = deliveries.Where(d => d.Status == OrderStatus.Delivered).ToList();
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        return new BusinessOverviewVm
        {
            Account = account,
            Contracts = contracts,
            Upcoming = deliveries
                .Where(d => d.Status != OrderStatus.Delivered && d.ScheduledForUtc >= DateTime.UtcNow.AddHours(-6))
                .OrderBy(d => d.ScheduledForUtc)
                .Take(12)
                .ToList(),
            Recent = delivered.OrderByDescending(d => d.CompletedAtUtc ?? d.ScheduledForUtc).Take(12).ToList(),
            Invoices = invoices.Take(12).ToList(),
            TotalAssets = contracts.Sum(c => c.Assets.Count),
            LitresThisMonth = Math.Round(delivered.Where(d => d.ScheduledForUtc >= monthStart).Sum(d => d.DeliveredLitres), 0),
            LitresTotal = Math.Round(delivered.Sum(d => d.DeliveredLitres), 0),
            SpendThisMonth = Math.Round(delivered.Where(d => d.ScheduledForUtc >= monthStart).Sum(d => d.LineTotal), 0),
            SavedByContract = Math.Round(delivered.Sum(d => SavedOn(d, d.SupplyContract?.DiscountPerLitre ?? 0m)), 0),
            OutstandingAmount = Math.Round(invoices.Where(i => i.Status == InvoiceStatus.Issued).Sum(i => i.Total), 0),
            OverdueCount = invoices.Count(i => i.IsOverdue),
            DeliveriesCompleted = delivered.Count
        };
    }

    public async Task<ContractDetailsVm?> GetContractAsync(int contractId)
    {
        var contract = await _db.SupplyContracts
            .Include(c => c.BusinessAccount)
            .Include(c => c.FuelType)
            .Include(c => c.Assets)
            .FirstOrDefaultAsync(c => c.Id == contractId);

        if (contract is null) return null;

        var deliveries = await _db.ContractDeliveries
            .Include(d => d.ContractAsset)
            .Include(d => d.DeliveryVehicle)
            .Include(d => d.Proof)
            .Where(d => d.SupplyContractId == contractId)
            .OrderByDescending(d => d.ScheduledForUtc)
            .Take(60)
            .ToListAsync();

        var invoices = await _db.Invoices
            .Where(i => i.SupplyContractId == contractId)
            .OrderByDescending(i => i.IssuedOn)
            .ToListAsync();

        var delivered = deliveries.Where(d => d.Status == OrderStatus.Delivered).ToList();
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var thisMonth = delivered.Where(d => d.ScheduledForUtc >= monthStart).Sum(d => d.DeliveredLitres);

        return new ContractDetailsVm
        {
            Contract = contract,
            Deliveries = deliveries,
            Invoices = invoices,
            LitresThisMonth = Math.Round(thisMonth, 0),
            CommitmentPercent = contract.CommittedLitresPerMonth <= 0
                ? 0
                : Math.Round(thisMonth / contract.CommittedLitresPerMonth * 100, 1),
            SavedByContract = Math.Round(delivered.Sum(d => SavedOn(d, contract.DiscountPerLitre)), 0),
            NextDelivery = deliveries.Where(d => d.Status != OrderStatus.Delivered)
                                     .OrderBy(d => d.ScheduledForUtc)
                                     .FirstOrDefault()
        };
    }

    public Task<List<ContractDelivery>> GetUpcomingAsync(int accountId, int take = 10)
        => _db.ContractDeliveries
              .Include(d => d.SupplyContract)
              .Include(d => d.ContractAsset)
              .Include(d => d.DeliveryVehicle)
              .Where(d => d.SupplyContract!.BusinessAccountId == accountId && d.Status != OrderStatus.Delivered)
              .OrderBy(d => d.ScheduledForUtc)
              .Take(take)
              .ToListAsync();

    public Task<DeliveryProof?> GetProofAsync(int deliveryId)
        => _db.DeliveryProofs
              .Include(p => p.ContractDelivery).ThenInclude(d => d!.SupplyContract).ThenInclude(c => c!.BusinessAccount)
              .Include(p => p.ContractDelivery).ThenInclude(d => d!.SupplyContract).ThenInclude(c => c!.FuelType)
              .Include(p => p.ContractDelivery).ThenInclude(d => d!.ContractAsset)
              .Include(p => p.ContractDelivery).ThenInclude(d => d!.DeliveryVehicle)
              .FirstOrDefaultAsync(p => p.ContractDeliveryId == deliveryId);

    public Task<Invoice?> GetInvoiceAsync(int invoiceId)
        => _db.Invoices
              .Include(i => i.BusinessAccount)
              .Include(i => i.SupplyContract).ThenInclude(c => c!.FuelType)
              .Include(i => i.Lines)
              .FirstOrDefaultAsync(i => i.Id == invoiceId);

    public Task<List<Invoice>> GetInvoicesAsync(int accountId)
        => _db.Invoices
              .Include(i => i.SupplyContract)
              .Where(i => i.BusinessAccountId == accountId)
              .OrderByDescending(i => i.IssuedOn)
              .ToListAsync();

    public Task<DeliveryProof?> FindProofAsync(string proofNumber)
        => _db.DeliveryProofs
              .Include(p => p.ContractDelivery).ThenInclude(d => d!.SupplyContract).ThenInclude(c => c!.BusinessAccount)
              .FirstOrDefaultAsync(p => p.ProofNumber == proofNumber);

    public async Task<(bool Ok, string MessageKey)> ConfirmDeliveryAsync(int deliveryId, double litres, string receivedBy, string driverName,
        string? signatureData = null, string? remarks = null)
    {
        var delivery = await _db.ContractDeliveries
            .Include(d => d.SupplyContract)
            .Include(d => d.Proof)
            .FirstOrDefaultAsync(d => d.Id == deliveryId);

        if (delivery is null) return (false, "business.error.delivery");
        if (delivery.Status == OrderStatus.Delivered) return (false, "business.error.alreadydelivered");
        if (litres <= 0) return (false, "business.error.litres");

        var lastProof = await _db.DeliveryProofs
            .OrderByDescending(p => p.Id)
            .Select(p => (double?)p.MeterEnd)
            .FirstOrDefaultAsync() ?? 100000;

        delivery.DeliveredLitres = Math.Round(litres, 1);
        delivery.CompletedAtUtc = DateTime.UtcNow;
        delivery.Status = OrderStatus.Delivered;
        delivery.LineTotal = Math.Round(delivery.UnitPrice * (decimal)delivery.DeliveredLitres, 2);

        var proofPrefix = $"POD-{DateTime.UtcNow:yyyy}-";
        var proofNumber = NextReference(await _db.DeliveryProofs.Select(x => x.ProofNumber).ToListAsync(), proofPrefix);

        var proof = new DeliveryProof
        {
            ProofNumber = $"{proofPrefix}{proofNumber:D6}",
            ContractDeliveryId = delivery.Id,
            IssuedAtUtc = DateTime.UtcNow,
            LitresDelivered = delivery.DeliveredLitres,
            MeterStart = lastProof,
            MeterEnd = Math.Round(lastProof + delivery.DeliveredLitres, 1),
            DriverName = string.IsNullOrWhiteSpace(driverName) ? "ReFuel возач" : driverName.Trim(),
            ReceivedBy = string.IsNullOrWhiteSpace(receivedBy) ? "Одговорно лице" : receivedBy.Trim(),
            SignatureData = IsSignatureImage(signatureData) ? signatureData! : "signed-on-device",
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(),
            Latitude = delivery.SupplyContract?.DefaultLatitude ?? 0,
            Longitude = delivery.SupplyContract?.DefaultLongitude ?? 0
        };

        proof.VerificationHash = BusinessHash.Compute(proof.ProofNumber, delivery.Reference, proof.LitresDelivered, proof.IssuedAtUtc);

        _db.DeliveryProofs.Add(proof);
        await _db.SaveChangesAsync();

        return (true, "business.ok.delivered");
    }

    public async Task<(bool Ok, string MessageKey, Invoice? Invoice)> GenerateInvoiceAsync(int contractId, DateTime periodStart, DateTime periodEnd)
    {
        var contract = await _db.SupplyContracts.FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract is null) return (false, "business.error.contract", null);

        var deliveries = await _db.ContractDeliveries
            .Include(d => d.Proof)
            .Where(d => d.SupplyContractId == contractId
                     && d.Status == OrderStatus.Delivered
                     && d.InvoiceId == null
                     && d.ScheduledForUtc >= periodStart
                     && d.ScheduledForUtc <= periodEnd)
            .ToListAsync();

        if (deliveries.Count == 0) return (false, "business.error.nodeliveries", null);

        var invoicePrefix = $"RF-INV-{DateTime.UtcNow:yyyy}-";
        var invoiceNumber = NextReference(await _db.Invoices.Select(i => i.InvoiceNumber).ToListAsync(), invoicePrefix);

        var invoice = new Invoice
        {
            InvoiceNumber = $"{invoicePrefix}{invoiceNumber:D5}",
            BusinessAccountId = contract.BusinessAccountId,
            SupplyContractId = contract.Id,
            PeriodStart = periodStart.Date,
            PeriodEnd = periodEnd.Date,
            IssuedOn = DateTime.UtcNow.Date,
            DueOn = DateTime.UtcNow.Date.AddDays(30),
            VatRate = _options.VatRate,
            Status = InvoiceStatus.Issued
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        foreach (var delivery in deliveries)
        {
            _db.InvoiceLines.Add(new InvoiceLine
            {
                InvoiceId = invoice.Id,
                Description = $"{contract.Title} · {delivery.Reference}",
                DeliveredOn = delivery.ScheduledForUtc.Date,
                Litres = delivery.DeliveredLitres,
                UnitPrice = delivery.UnitPrice,
                Discount = SavedOn(delivery, contract.DiscountPerLitre),
                LineTotal = delivery.LineTotal,
                ProofNumber = delivery.Proof?.ProofNumber
            });

            delivery.InvoiceId = invoice.Id;
        }

        invoice.Subtotal = Math.Round(deliveries.Sum(d => d.LineTotal), 2);
        invoice.VatAmount = Math.Round(invoice.Subtotal * invoice.VatRate / 100m, 2);
        invoice.Total = invoice.Subtotal + invoice.VatAmount;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Generated invoice {Number} for contract {Contract}.", invoice.InvoiceNumber, contract.ContractNumber);
        return (true, "business.ok.invoice", invoice);
    }

    public async Task<int> GenerateUpcomingDeliveriesAsync(int days = 7)
    {
        var contracts = await _db.SupplyContracts
            .Include(c => c.Assets)
            .Where(c => c.Status == ContractStatus.Active)
            .ToListAsync();

        if (contracts.Count == 0) return 0;

        var fleet = await _db.DeliveryVehicles.Where(v => v.IsAvailable).ToListAsync();
        var created = 0;
        var horizon = DateTime.UtcNow.Date.AddDays(days);

        var prefix = $"RF-D-{DateTime.UtcNow:yyyy}-";
        var counter = NextReference(await _db.ContractDeliveries.Select(d => d.Reference).ToListAsync(), prefix);

        foreach (var contract in contracts)
        {
            var assets = contract.Assets.Where(a => a.IsActive).ToList();
            if (assets.Count == 0) continue;

            var step = contract.Frequency switch
            {
                DeliveryFrequency.Nightly => 1,
                DeliveryFrequency.Weekly => 7,
                DeliveryFrequency.Biweekly => 14,
                DeliveryFrequency.Monthly => 30,
                _ => 0
            };

            if (step == 0) continue;

            var latest = await _db.ContractDeliveries
                .Where(d => d.SupplyContractId == contract.Id)
                .OrderByDescending(d => d.ScheduledForUtc)
                .Select(d => (DateTime?)d.ScheduledForUtc)
                .FirstOrDefaultAsync();

            var (referencePrice, unitPrice) = await CurrentNetPriceAsync(contract);

            // The tanker based nearest to the site does the run; the committed volume decides how
            // many loads a step needs, so a nightly fleet gets three loads a night, not one.
            var boats = assets.Any(a => a.Kind == AssetKind.Boat);
            var truck = NearestTanker(fleet, contract.DefaultLatitude, contract.DefaultLongitude, boats);
            var capacity = truck?.CapacityLitres is > 0 ? truck.CapacityLitres : 1200;

            var perStep = contract.CommittedLitresPerMonth / (30.0 / step);
            var visits = Math.Max(1, (int)Math.Ceiling(perStep / capacity));
            var perVisit = Math.Round(perStep / visits, 0);
            perVisit = Math.Clamp(perVisit, contract.MinLitresPerDelivery, Math.Max(contract.MinLitresPerDelivery, capacity));

            // The last load of a step sits up to (visits−1)·step/visits days after the step start;
            // anchor the next step on the start, not on that last load, or the rhythm drifts.
            var lastOffset = (int)Math.Floor((visits - 1) * step / (double)visits);
            var next = latest.HasValue
                ? latest.Value.Date.AddDays(step - lastOffset)
                : DateTime.UtcNow.Date.AddDays(step);

            var index = 0;
            while (next <= horizon)
            {
                for (var v = 0; v < visits; v++)
                {
                    var asset = assets[index++ % assets.Count];
                    var scheduled = step == 1
                        ? next.Add(contract.DeliveryWindowStart).AddMinutes(v * 50)
                        : next.AddDays(Math.Floor(v * step / (double)visits)).Add(contract.DeliveryWindowStart);

                    _db.ContractDeliveries.Add(new ContractDelivery
                    {
                        Reference = $"{prefix}{counter++:D5}",
                        SupplyContractId = contract.Id,
                        ContractAssetId = asset.Id,
                        DeliveryVehicleId = truck?.Id,
                        ScheduledForUtc = scheduled,
                        Status = OrderStatus.Confirmed,
                        PlannedLitres = perVisit,
                        ReferencePricePerLitre = referencePrice,
                        UnitPrice = unitPrice,
                        LineTotal = 0
                    });

                    created++;
                }

                next = next.AddDays(step);
            }
        }

        if (created > 0) await _db.SaveChangesAsync();
        return created;
    }

    private static DeliveryVehicle? NearestTanker(IEnumerable<DeliveryVehicle> fleet, double lat, double lon, bool boats)
    {
        var candidates = fleet.Where(v => !boats || v.CanServeBoats).ToList();
        if (candidates.Count == 0) candidates = fleet.ToList();

        return candidates
            .OrderBy(v => GeoUtils.HaversineKm(v.BaseLatitude, v.BaseLongitude, lat, lon))
            .FirstOrDefault();
    }

    private static int NextReference(IEnumerable<string> existing, string prefix)
    {
        var max = 0;
        foreach (var reference in existing)
        {
            if (!reference.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(reference[prefix.Length..], out var n) && n > max) max = n;
        }
        return max + 1;
    }

    /// <summary>Only a PNG data URL of sensible size is stored as a signature; anything else falls back to the placeholder.</summary>
    private static bool IsSignatureImage(string? data)
        => !string.IsNullOrWhiteSpace(data)
           && data.StartsWith("data:image/png;base64,", StringComparison.Ordinal)
           && data.Length is > 200 and < 400_000;

    /// <summary>
    /// A contract is priced the way the customer reads it: the regulator's pump price for the day
    /// minus the fixed discount per litre they negotiated. ReFuel's margin is what is left of the
    /// procurement spread after that discount, so the discount is capped at the tier's floor.
    /// </summary>
    private async Task<(decimal ReferencePrice, decimal UnitPrice)> CurrentNetPriceAsync(SupplyContract contract)
    {
        var price = await _db.NationalPrices
            .Where(p => p.FuelTypeId == contract.FuelTypeId)
            .OrderByDescending(p => p.EffectiveFrom)
            .Select(p => (decimal?)p.Price)
            .FirstOrDefaultAsync() ?? 90m;

        var discount = await AllowedContractDiscountAsync(contract);

        return (Math.Round(price, 2), Math.Round(Math.Max(1m, price - discount), 2));
    }

    /// <summary>The contract's discount, clamped so the tier's minimum margin survives — a warning to configure, not a silent loss.</summary>
    private async Task<decimal> AllowedContractDiscountAsync(SupplyContract contract)
    {
        var settings = await _pricing.GetSettingsAsync();

        var account = contract.BusinessAccount
                      ?? await _db.BusinessAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == contract.BusinessAccountId);

        var tier = account is null
            ? await _pricing.ResolveTierAsync(CustomerType.Fleet)
            : await _pricing.ResolveTierAsync(account.CustomerType, account.PricingTierId);

        var ceiling = Math.Max(0, settings.ProcurementSpreadPerLitre - tier.MinMarginPerLitre);
        var discount = Math.Max(0, contract.DiscountPerLitre);

        if (discount > ceiling)
        {
            _logger.LogWarning("Contract {Contract} grants {Discount} ден/л, above the {Ceiling} ден/л the {Tier} tier can carry; capped.",
                contract.ContractNumber, discount, ceiling, tier.Code);
            discount = ceiling;
        }

        return discount;
    }
}
