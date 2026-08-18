using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services;

public interface IPricingService
{
    Task<PriceBreakdown> QuoteAsync(PricingRequest request);

    Task<PricingSettings> GetSettingsAsync();
    Task SaveSettingsAsync(PricingSettings settings, string updatedBy);

    Task<List<PricingTier>> GetTiersAsync(bool onlyActive = true);
    Task<PricingTier?> GetTierAsync(int id);
    Task<PricingTier> ResolveTierAsync(CustomerType customerType, int? pricingTierId = null);
    Task SaveTierAsync(PricingTier tier);

    Task<List<VolumeDiscountBand>> GetBandsAsync(int? pricingTierId = null);
    Task SaveBandAsync(VolumeDiscountBand band);
    Task DeleteBandAsync(int id);

    Task<List<DeliveryZone>> GetZonesAsync(bool onlyActive = true);
    Task<DeliveryZone?> FindZoneAsync(double latitude, double longitude, string? city = null);
    Task SaveZoneAsync(DeliveryZone zone);

    Task<double> GetMonthlyLitresAsync(string? userId, int? businessAccountId);

    EconomicsResultVm CalculateEconomics(EconomicsInputVm input, PricingSettings settings);
    SavingsResultVm CalculateSavings(SavingsInputVm input);
}

public class PricingService : IPricingService
{
    private readonly ApplicationDbContext _db;
    private readonly ITranslator _t;
    private readonly ReFuelOptions _options;

    public PricingService(ApplicationDbContext db, ITranslator translator, IOptions<ReFuelOptions> options)
    {
        _db = db;
        _t = translator;
        _options = options.Value;
    }

    public async Task<PriceBreakdown> QuoteAsync(PricingRequest request)
    {
        var settings = await GetSettingsAsync();
        var tier = await ResolveTierAsync(request.CustomerType, request.PricingTierId);

        var orderType = request.IsEmergency ? OrderType.Emergency : request.OrderType;
        var isEmergency = orderType == OrderType.Emergency;
        var isScheduled = orderType == OrderType.Scheduled;

        // The quote is anchored on what the customer would pay at a station. ReFuel buys below that
        // price (the procurement spread) and quotes at or under it — the spread is the margin.
        var pump = request.PumpPricePerLitre > 0 ? request.PumpPricePerLitre : request.BaseCostPerLitre;
        var spread = Math.Max(0, settings.ProcurementSpreadPerLitre);

        var breakdown = new PriceBreakdown
        {
            PricingTierId = tier.Id,
            TierName = tier.LocalizedName(_t.Culture),
            CustomerType = tier.CustomerType,
            OrderType = orderType,
            Litres = Math.Round(request.Litres, 2),
            DistanceKm = Math.Round(Math.Max(0, request.DistanceKm), 2),
            MonthlyLitres = Math.Round(Math.Max(0, request.MonthlyLitres), 0),
            PumpPricePerLitre = Math.Round(Math.Max(0, pump), 2),
            ProcurementSpreadPerLitre = Math.Round(spread, 2),
            BaseCostPerLitre = Math.Round(Math.Max(0, pump - spread), 2),
            MinOrderLitres = tier.MinOrderLitres,
            MinDeliveryFee = tier.MinDeliveryFee,
            VatRate = settings.VatRate,
            ShowExplanation = settings.ShowPriceExplanation,
            ShowMargin = settings.ShowMarginToCustomer,
            MarketingMessage = tier.MarketingMessage(_t.Culture),
            ScheduledDiscountPercent = Math.Clamp(tier.ScheduledDeliveryDiscountPercent, 0, 100),
            TierEmergencySurcharge = Math.Round(tier.EmergencySurcharge, 0),
            FreeDeliveryScheduledOnly = tier.FreeDeliveryScheduledOnly
        };

        if (breakdown.PumpPricePerLitre <= 0 || breakdown.Litres <= 0)
        {
            breakdown.Warnings.Add(_t["pricing.warn.noprice"]);
            return breakdown;
        }

        if (breakdown.Litres < tier.MinOrderLitres)
            breakdown.Warnings.Add(_t.Format("pricing.warn.minlitres", Fmt.N(tier.MinOrderLitres, 0)));

        // Margin above the wholesale price, and the floor that discounts may not cross. Everything is
        // rounded to the денар-cent first so the parts always add up exactly on the customer's bill.
        var margin = Math.Round(Math.Max(0, request.NegotiatedMarginPerLitre ?? tier.MarginPerLitre), 2);
        var floor = Math.Round(Math.Clamp(tier.MinMarginPerLitre, 0, margin), 2);
        var room = margin - floor;
        var clamped = false;

        var band = await FindBandAsync(tier.Id, breakdown.MonthlyLitres);
        var volumeDiscount = Math.Round(Math.Max(0, band?.DiscountPerLitre ?? 0m), 2);
        if (volumeDiscount > room) { volumeDiscount = room; clamped = true; }

        var contractDiscount = Math.Round(Math.Max(0, request.ContractDiscountPerLitre), 2);
        if (contractDiscount > room - volumeDiscount) { contractDiscount = Math.Max(0, room - volumeDiscount); clamped = true; }

        breakdown.VolumeBandLabel = band?.LocalizedLabel(_t.Culture);
        breakdown.MarginPerLitre = margin;
        breakdown.MinMarginPerLitre = floor;
        breakdown.VolumeDiscountPerLitre = volumeDiscount;
        breakdown.ContractDiscountPerLitre = contractDiscount;

        breakdown.EffectivePricePerLitre = Math.Round(
            breakdown.BaseCostPerLitre + breakdown.MarginPerLitre - breakdown.VolumeDiscountPerLitre - breakdown.ContractDiscountPerLitre, 2);

        if (breakdown.EffectivePricePerLitre < breakdown.BaseCostPerLitre)
        {
            breakdown.EffectivePricePerLitre = breakdown.BaseCostPerLitre;
            breakdown.Warnings.Add(_t["pricing.warn.belowcost"]);
        }

        breakdown.TierDiscountPerLitre = Math.Round(spread - margin, 2);
        breakdown.CustomerDiscountPerLitre = Math.Round(breakdown.PumpPricePerLitre - breakdown.EffectivePricePerLitre, 2);

        if (breakdown.CustomerDiscountPerLitre < 0)
            breakdown.Warnings.Add(_t["pricing.warn.abovepump"]);

        if (clamped)
            breakdown.Warnings.Add(_t.Format("pricing.warn.marginfloor", Fmt.N(floor, 2)));

        var litres = (decimal)breakdown.Litres;

        breakdown.FuelCost = Math.Round(breakdown.BaseCostPerLitre * litres, 2);
        breakdown.FuelRevenue = Math.Round(breakdown.EffectivePricePerLitre * litres, 2);
        breakdown.GrossFuelMargin = Math.Round(breakdown.FuelRevenue - breakdown.FuelCost, 2);

        var zone = request.Latitude.HasValue && request.Longitude.HasValue
            ? await FindZoneAsync(request.Latitude.Value, request.Longitude.Value, request.City)
            : null;

        breakdown.DeliveryZoneId = zone?.Id;
        breakdown.ZoneName = zone?.Name;

        // Delivery fee: distance-based (zone rates, else tier rates), never under the tier minimum.
        var baseFee = zone?.BaseDeliveryFee ?? tier.MinDeliveryFee;
        var perKm = zone?.FeePerKm ?? tier.DeliveryFeePerKm;
        var distanceFee = baseFee + perKm * (decimal)Math.Round(breakdown.DistanceKm, 1);
        if (distanceFee < tier.MinDeliveryFee) distanceFee = tier.MinDeliveryFee;
        distanceFee = Math.Round(distanceFee, 0);

        decimal fee;

        if (request.ContractFixedDeliveryFee.HasValue)
        {
            fee = Math.Round(Math.Max(0, request.ContractFixedDeliveryFee.Value), 0);

            // A contract drop below the minimum size pays the normal distance fee, so a 300-litre
            // top-up never rides on a zero fixed fee that was negotiated for full-compartment drops.
            if (settings.ContractMinDropLitres > 0 && breakdown.Litres < settings.ContractMinDropLitres && distanceFee > fee)
                fee = distanceFee;

            breakdown.DeliveryFeeBeforeDiscount = fee;
        }
        else
        {
            fee = distanceFee;
            breakdown.DeliveryFeeBeforeDiscount = fee;

            if (isScheduled && breakdown.ScheduledDiscountPercent > 0)
            {
                breakdown.ScheduledFeeDiscount = Math.Round(fee * breakdown.ScheduledDiscountPercent / 100m, 0);
                fee -= breakdown.ScheduledFeeDiscount;
            }

            var freeAllowed = tier.FreeDeliveryThreshold > 0
                              && breakdown.FuelRevenue >= tier.FreeDeliveryThreshold
                              && (!tier.FreeDeliveryScheduledOnly || isScheduled);

            if (freeAllowed)
            {
                fee = 0;
                breakdown.ScheduledFeeDiscount = 0;
            }
        }

        breakdown.DeliveryFee = Math.Round(Math.Max(0, fee), 0);
        breakdown.EmergencySurcharge = isEmergency ? Math.Round(tier.EmergencySurcharge, 0) : 0m;
        breakdown.PromotionDiscount = Math.Round(Math.Max(0, request.PromotionDiscount), 2);

        // What the drop costs ReFuel: a share of a route for standard/scheduled orders, a tanker of
        // its own for emergencies and contract drops.
        var grouped = !isEmergency && !request.DedicatedTrip;
        var cost = DeliveryCosting.For(settings, breakdown.DistanceKm, grouped);

        breakdown.DeliveryCost = cost.Total;
        breakdown.DeliveryCostDriver = cost.Driver;
        breakdown.DeliveryCostTanker = cost.Tanker;
        breakdown.DeliveryCostDispatch = cost.Dispatch;
        breakdown.DeliveryCostKm = cost.KmDriven;
        breakdown.DeliveryCostHours = cost.Hours;
        breakdown.DeliveryGrouped = cost.Grouped;

        breakdown.Subtotal = Math.Round(
            breakdown.FuelRevenue + breakdown.DeliveryFee + breakdown.EmergencySurcharge - breakdown.PromotionDiscount, 2);

        if (breakdown.Subtotal < 0) breakdown.Subtotal = 0;

        breakdown.VatAmount = request.IncludeVat
            ? Math.Round(breakdown.Subtotal * settings.VatRate / 100m, 2)
            : 0m;

        breakdown.Total = Math.Round(breakdown.Subtotal + breakdown.VatAmount, 2);

        breakdown.GrossContribution = Math.Round(
            breakdown.GrossFuelMargin + breakdown.DeliveryFee + breakdown.EmergencySurcharge
            - breakdown.PromotionDiscount - breakdown.DeliveryCost, 2);

        BuildLines(breakdown, settings, tier);

        return breakdown;
    }

    private void BuildLines(PriceBreakdown b, PricingSettings settings, PricingTier tier)
    {
        var litres = (decimal)b.Litres;

        // --- what the customer sees: pump price first, then every reason the bill is lower ---

        var fuelAtPump = Math.Round(b.PumpPricePerLitre * litres, 2);
        var volumeAmount = b.VolumeDiscountPerLitre > 0 ? Math.Round(b.VolumeDiscountPerLitre * litres, 2) : 0m;
        var contractAmount = b.ContractDiscountPerLitre > 0 ? Math.Round(b.ContractDiscountPerLitre * litres, 2) : 0m;

        // The tier line is the residual, so pump − tier − volume − contract equals the fuel total exactly.
        var tierAmount = fuelAtPump - volumeAmount - contractAmount - b.FuelRevenue;

        b.Lines.Add(new PriceLine
        {
            Label = _t["pricing.line.fuel"],
            Detail = Fmt.N(b.Litres, 1) + " л × " + Fmt.N(b.PumpPricePerLitre, 2) + " ден · " + _t["pricing.pump"],
            Amount = fuelAtPump,
            Kind = "cost"
        });

        if (tierAmount > 0)
        {
            b.Lines.Add(new PriceLine
            {
                Label = _t["pricing.line.yourprice"],
                Detail = "−" + Fmt.N(Math.Max(0, b.TierDiscountPerLitre), 2) + " ден/л · " + b.TierName,
                Amount = -tierAmount,
                Kind = "discount"
            });
        }
        else if (tierAmount < 0)
        {
            b.Lines.Add(new PriceLine
            {
                Label = _t["pricing.line.premium"],
                Detail = "+" + Fmt.N(Math.Max(0, -b.TierDiscountPerLitre), 2) + " ден/л · " + b.TierName,
                Amount = -tierAmount,
                Kind = "margin"
            });
        }

        if (volumeAmount > 0)
        {
            b.Lines.Add(new PriceLine
            {
                Label = b.VolumeBandLabel ?? _t["pricing.line.volume"],
                Detail = "−" + Fmt.N(b.VolumeDiscountPerLitre, 2) + " ден/л · " + Fmt.N(b.MonthlyLitres, 0) + " л/" + _t["common.month"],
                Amount = -volumeAmount,
                Kind = "discount"
            });
        }

        if (contractAmount > 0)
        {
            b.Lines.Add(new PriceLine
            {
                Label = _t["pricing.line.contract"],
                Detail = "−" + Fmt.N(b.ContractDiscountPerLitre, 2) + " ден/л",
                Amount = -contractAmount,
                Kind = "discount"
            });
        }

        string deliveryDetail;
        if (b.DeliveryFee <= 0)
        {
            deliveryDetail = _t["pricing.line.delivery.free"];
        }
        else
        {
            deliveryDetail = (b.ZoneName ?? _t["pricing.zone.default"]) + " · " + Fmt.Km(b.DistanceKm);
            if (b.ScheduledFeeDiscount > 0)
                deliveryDetail += " · " + _t.Format("pricing.line.delivery.scheduled", b.ScheduledDiscountPercent);
        }

        b.Lines.Add(new PriceLine
        {
            Label = _t["pricing.line.delivery"],
            Detail = deliveryDetail,
            Amount = b.DeliveryFee,
            Kind = "fee"
        });

        if (b.EmergencySurcharge > 0)
        {
            b.Lines.Add(new PriceLine
            {
                Label = _t["pricing.line.emergency"],
                Detail = _t["pricing.line.emergency.detail"],
                Amount = b.EmergencySurcharge,
                Kind = "surcharge"
            });
        }

        if (b.PromotionDiscount > 0)
        {
            b.Lines.Add(new PriceLine
            {
                Label = _t["pricing.line.promotion"],
                Amount = -b.PromotionDiscount,
                Kind = "discount"
            });
        }

        // --- what only ReFuel sees: where the money actually comes from and goes ---

        b.InternalLines.Add(new PriceLine
        {
            Label = _t["pricing.internal.procurement"],
            Detail = _t.Format("pricing.internal.procurement.detail",
                Fmt.N(b.PumpPricePerLitre, 2), Fmt.N(b.ProcurementSpreadPerLitre, 2), Fmt.N(b.BaseCostPerLitre, 2)),
            Amount = -b.FuelCost,
            Kind = "cost",
            IsInternal = true
        });

        b.InternalLines.Add(new PriceLine
        {
            Label = _t["pricing.internal.grossfuelmargin"],
            Detail = _t["pricing.internal.grossfuelmargin.detail"] + " · " + Fmt.N(b.MarginPerLitre - b.VolumeDiscountPerLitre - b.ContractDiscountPerLitre, 2) + " ден/л",
            Amount = b.GrossFuelMargin,
            Kind = "margin",
            IsInternal = true
        });

        var costShape = b.DeliveryGrouped
            ? _t.Format("pricing.internal.deliverycost.grouped", Math.Max(1, settings.AverageStopsPerRoute), Fmt.N(b.DeliveryCostKm, 1), Fmt.N(b.DeliveryCostHours, 2))
            : _t.Format("pricing.internal.deliverycost.dedicated", Fmt.N(b.DeliveryCostKm, 1), Fmt.N(b.DeliveryCostHours, 2));

        b.InternalLines.Add(new PriceLine
        {
            Label = _t["pricing.internal.deliverycost"],
            Detail = costShape + " · " +
                     Fmt.N(settings.TankerCostPerKm, 2) + " ден/km · " +
                     Fmt.N(settings.DriverCostPerHour, 0) + " ден/h · " +
                     Fmt.N(settings.FixedDispatchCost, 0) + " ден " + _t["pricing.internal.fixed"],
            Amount = -b.DeliveryCost,
            Kind = "cost",
            IsInternal = true
        });

        b.InternalLines.Add(new PriceLine
        {
            Label = _t["pricing.internal.grosscontribution"],
            Detail = _t["pricing.internal.grosscontribution.detail"],
            Amount = b.GrossContribution,
            Kind = "total",
            IsInternal = true
        });

        if (b.GrossContribution < 0)
            b.Warnings.Add(_t["pricing.warn.negativecontribution"]);
    }

    public async Task<PricingSettings> GetSettingsAsync()
    {
        var settings = await _db.PricingSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();

        return settings ?? new PricingSettings
        {
            VatRate = _options.VatRate,
            UpdatedBy = "default"
        };
    }

    public async Task SaveSettingsAsync(PricingSettings settings, string updatedBy)
    {
        var current = await _db.PricingSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();

        if (current is null)
        {
            current = new PricingSettings { UpdatedBy = updatedBy };
            _db.PricingSettings.Add(current);
        }

        current.TankerCostPerKm = Math.Max(0, settings.TankerCostPerKm);
        current.DriverCostPerHour = Math.Max(0, settings.DriverCostPerHour);
        current.FixedDispatchCost = Math.Max(0, settings.FixedDispatchCost);
        current.MonthlyFixedCosts = Math.Max(0, settings.MonthlyFixedCosts);
        current.VatRate = Math.Clamp(settings.VatRate, 0m, 30m);
        current.AverageSpeedKph = Math.Clamp(settings.AverageSpeedKph, 10, 110);
        current.ChargeReturnLeg = settings.ChargeReturnLeg;
        current.ProcurementSpreadPerLitre = Math.Clamp(settings.ProcurementSpreadPerLitre, 0m, 30m);
        current.AverageStopsPerRoute = Math.Clamp(settings.AverageStopsPerRoute, 1, 20);
        current.InterStopKm = Math.Clamp(settings.InterStopKm, 0, 50);
        current.ServiceMinutesPerStop = Math.Clamp(settings.ServiceMinutesPerStop, 0, 120);
        current.ContractMinDropLitres = Math.Clamp(settings.ContractMinDropLitres, 0, 40000);
        current.ShowPriceExplanation = settings.ShowPriceExplanation;
        current.ShowMarginToCustomer = settings.ShowMarginToCustomer;
        current.UpdatedBy = updatedBy;
        current.UpdatedAtUtc = DateTime.UtcNow;

        if (current.PricingModelVersion <= 0) current.PricingModelVersion = PricingModelVersions.Current;

        await _db.SaveChangesAsync();
    }

    public async Task<List<PricingTier>> GetTiersAsync(bool onlyActive = true)
    {
        var query = _db.PricingTiers.Include(t => t.Bands).AsQueryable();
        if (onlyActive) query = query.Where(t => t.IsActive);

        return await query.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
    }

    public Task<PricingTier?> GetTierAsync(int id) =>
        _db.PricingTiers.Include(t => t.Bands).FirstOrDefaultAsync(t => t.Id == id);

    public async Task<PricingTier> ResolveTierAsync(CustomerType customerType, int? pricingTierId = null)
    {
        if (pricingTierId is > 0)
        {
            var explicitTier = await _db.PricingTiers.FirstOrDefaultAsync(t => t.Id == pricingTierId);
            if (explicitTier is not null) return explicitTier;
        }

        var tier = await _db.PricingTiers
            .Where(t => t.IsActive && t.CustomerType == customerType)
            .OrderBy(t => t.SortOrder)
            .FirstOrDefaultAsync();

        tier ??= await _db.PricingTiers
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .FirstOrDefaultAsync();

        return tier ?? FallbackTier(customerType);
    }

    private PricingTier FallbackTier(CustomerType customerType) => new()
    {
        Id = 0,
        Code = "FALLBACK",
        NameMk = "Стандардна цена",
        NameEn = "Standard price",
        NameSq = "Çmim standard",
        CustomerType = customerType,
        MarginPerLitre = 0m,
        MinDeliveryFee = _options.BaseDeliveryFee,
        DeliveryFeePerKm = _options.DeliveryFeePerKm,
        FreeDeliveryThreshold = _options.FreeDeliveryThreshold,
        EmergencySurcharge = _options.EmergencySurcharge,
        MinOrderLitres = _options.MinLitres,
        MaxOrderLitres = _options.MaxLitres
    };

    public async Task SaveTierAsync(PricingTier tier)
    {
        var current = await _db.PricingTiers.FirstOrDefaultAsync(t => t.Id == tier.Id);
        if (current is null) return;

        current.MarginPerLitre = Math.Max(0, tier.MarginPerLitre);
        current.MinMarginPerLitre = Math.Clamp(tier.MinMarginPerLitre, 0, current.MarginPerLitre);
        current.ScheduledDeliveryDiscountPercent = Math.Clamp(tier.ScheduledDeliveryDiscountPercent, 0, 90);
        current.FreeDeliveryScheduledOnly = tier.FreeDeliveryScheduledOnly;
        current.MinDeliveryFee = Math.Max(0, tier.MinDeliveryFee);
        current.DeliveryFeePerKm = Math.Max(0, tier.DeliveryFeePerKm);
        current.FreeDeliveryThreshold = Math.Max(0, tier.FreeDeliveryThreshold);
        current.EmergencySurcharge = Math.Max(0, tier.EmergencySurcharge);
        current.MinOrderLitres = Math.Clamp(tier.MinOrderLitres, 1, 5000);
        current.MaxOrderLitres = Math.Clamp(tier.MaxOrderLitres, current.MinOrderLitres, 40000);
        current.IsNegotiated = tier.IsNegotiated;
        current.IsActive = tier.IsActive;
        current.MarketingMessageMk = tier.MarketingMessageMk;
        current.MarketingMessageEn = tier.MarketingMessageEn;
        current.MarketingMessageSq = tier.MarketingMessageSq;
        current.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    public async Task<List<VolumeDiscountBand>> GetBandsAsync(int? pricingTierId = null)
    {
        var bands = await _db.VolumeDiscountBands
            .Where(b => b.IsActive)
            .OrderBy(b => b.FromLitresPerMonth)
            .ToListAsync();

        if (pricingTierId is null) return bands;

        return bands
            .Where(b => b.PricingTierId == null || b.PricingTierId == pricingTierId)
            .ToList();
    }

    private async Task<VolumeDiscountBand?> FindBandAsync(int pricingTierId, double monthlyLitres)
    {
        if (monthlyLitres <= 0) return null;

        var bands = await GetBandsAsync(pricingTierId);

        return bands
            .Where(b => b.Covers(monthlyLitres))
            .OrderByDescending(b => b.PricingTierId.HasValue)
            .ThenByDescending(b => b.DiscountPerLitre)
            .FirstOrDefault();
    }

    public async Task SaveBandAsync(VolumeDiscountBand band)
    {
        var tierId = band.PricingTierId is > 0 ? band.PricingTierId : null;
        var discount = Math.Max(0, band.DiscountPerLitre);

        // A band that belongs to one tier can never take more than that tier has room to give.
        if (tierId is int id)
        {
            var tier = await _db.PricingTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (tier is not null)
                discount = Math.Min(discount, Math.Max(0, tier.MarginPerLitre - tier.MinMarginPerLitre));
        }

        if (band.Id > 0)
        {
            var current = await _db.VolumeDiscountBands.FirstOrDefaultAsync(b => b.Id == band.Id);
            if (current is null) return;

            current.PricingTierId = tierId;
            current.FromLitresPerMonth = Math.Max(0, band.FromLitresPerMonth);
            current.ToLitresPerMonth = band.ToLitresPerMonth is > 0 ? band.ToLitresPerMonth : null;
            current.DiscountPerLitre = discount;
            if (!string.IsNullOrWhiteSpace(band.LabelMk)) current.LabelMk = band.LabelMk;
            if (!string.IsNullOrWhiteSpace(band.LabelEn)) current.LabelEn = band.LabelEn;
            if (!string.IsNullOrWhiteSpace(band.LabelSq)) current.LabelSq = band.LabelSq;
            current.IsActive = band.IsActive;
        }
        else
        {
            band.PricingTierId = tierId;
            band.ToLitresPerMonth = band.ToLitresPerMonth is > 0 ? band.ToLitresPerMonth : null;
            band.DiscountPerLitre = discount;
            if (string.IsNullOrWhiteSpace(band.LabelEn)) band.LabelEn = band.LabelMk;
            if (string.IsNullOrWhiteSpace(band.LabelSq)) band.LabelSq = band.LabelMk;
            _db.VolumeDiscountBands.Add(band);
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteBandAsync(int id)
    {
        var band = await _db.VolumeDiscountBands.FirstOrDefaultAsync(b => b.Id == id);
        if (band is null) return;

        _db.VolumeDiscountBands.Remove(band);
        await _db.SaveChangesAsync();
    }

    public Task<List<DeliveryZone>> GetZonesAsync(bool onlyActive = true)
    {
        var query = _db.DeliveryZones.AsQueryable();
        if (onlyActive) query = query.Where(z => z.IsActive);

        return query.OrderBy(z => z.SortOrder).ThenBy(z => z.Name).ToListAsync();
    }

    public async Task<DeliveryZone?> FindZoneAsync(double latitude, double longitude, string? city = null)
    {
        var zones = await GetZonesAsync();
        if (zones.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(city))
        {
            var byCity = zones.FirstOrDefault(z =>
                z.CityList.Any(c => string.Equals(c, city, StringComparison.OrdinalIgnoreCase)));

            if (byCity is not null) return byCity;
        }

        DeliveryZone? best = null;
        var bestDistance = double.MaxValue;

        foreach (var zone in zones)
        {
            var distance = GeoUtils.HaversineKm(latitude, longitude, zone.CenterLatitude, zone.CenterLongitude);
            if (distance > zone.RadiusKm) continue;
            if (distance >= bestDistance) continue;

            best = zone;
            bestDistance = distance;
        }

        return best;
    }

    public async Task SaveZoneAsync(DeliveryZone zone)
    {
        if (zone.Id > 0)
        {
            var current = await _db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == zone.Id);
            if (current is null) return;

            current.Name = zone.Name;
            current.Cities = zone.Cities ?? string.Empty;
            current.CenterLatitude = zone.CenterLatitude;
            current.CenterLongitude = zone.CenterLongitude;
            current.RadiusKm = Math.Clamp(zone.RadiusKm, 1, 200);
            current.BaseDeliveryFee = Math.Max(0, zone.BaseDeliveryFee);
            current.FeePerKm = Math.Max(0, zone.FeePerKm);
            current.MinOrderLitres = Math.Max(0, zone.MinOrderLitres);
            current.AverageSpeedKph = Math.Clamp(zone.AverageSpeedKph, 10, 110);
            current.ColorHex = zone.ColorHex;
            current.IsActive = zone.IsActive;
        }
        else
        {
            _db.DeliveryZones.Add(zone);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<double> GetMonthlyLitresAsync(string? userId, int? businessAccountId)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var total = 0d;

        if (!string.IsNullOrEmpty(userId))
        {
            total += await _db.Orders
                .Where(o => o.UserId == userId && o.CreatedAtUtc >= since && o.Status != OrderStatus.Cancelled)
                .SumAsync(o => (double?)o.Litres) ?? 0;
        }

        if (businessAccountId is > 0)
        {
            total += await _db.ContractDeliveries
                .Where(d => d.SupplyContract != null &&
                            d.SupplyContract.BusinessAccountId == businessAccountId &&
                            d.ScheduledForUtc >= since)
                .SumAsync(d => (double?)d.PlannedLitres) ?? 0;

            var committed = await _db.SupplyContracts
                .Where(c => c.BusinessAccountId == businessAccountId && c.Status == ContractStatus.Active)
                .SumAsync(c => (double?)c.CommittedLitresPerMonth) ?? 0;

            if (committed > total) total = committed;
        }

        return Math.Round(total, 0);
    }

    public EconomicsResultVm CalculateEconomics(EconomicsInputVm input, PricingSettings settings)
    {
        var result = new EconomicsResultVm { Input = input };

        var deliveriesPerDay = Math.Max(0, input.DeliveriesPerDay);
        var days = Math.Clamp(input.WorkingDaysPerMonth, 1, 31);

        result.MonthlyDeliveries = deliveriesPerDay * days;
        result.MonthlyLitres = Math.Round(result.MonthlyDeliveries * Math.Max(0, input.AverageLitresPerDelivery), 0);

        var litres = (decimal)result.MonthlyLitres;
        var deliveries = (decimal)result.MonthlyDeliveries;

        result.MonthlyGrossFuelMargin = Math.Round(litres * Math.Max(0, input.AverageMarginPerLitre), 0);
        result.MonthlyDeliveryRevenue = Math.Round(deliveries * Math.Max(0, input.AverageDeliveryFee), 0);
        result.MonthlyFuelRevenue = result.MonthlyGrossFuelMargin;
        result.MonthlyRevenue = result.MonthlyGrossFuelMargin + result.MonthlyDeliveryRevenue;

        // Most deliveries ride on a shared route; the rest (emergencies) get a tanker of their own.
        var groupedShare = Math.Clamp(input.GroupedSharePercent, 0, 100) / 100m;
        var grouped = DeliveryCosting.GroupedDrop(settings, input.AverageDistanceKm).Total;
        var dedicated = DeliveryCosting.DedicatedTrip(settings, input.AverageDistanceKm).Total;
        var costPerDelivery = Math.Round(grouped * groupedShare + dedicated * (1 - groupedShare), 2);

        result.CostPerDelivery = costPerDelivery;
        result.MonthlyDeliveryCost = Math.Round(deliveries * costPerDelivery, 0);
        result.MonthlyGrossContribution = result.MonthlyGrossFuelMargin + result.MonthlyDeliveryRevenue - result.MonthlyDeliveryCost;

        result.MonthlyFixedCosts = Math.Max(0, input.MonthlyFixedCosts);
        result.OperatingResult = result.MonthlyGrossContribution - result.MonthlyFixedCosts;

        result.ContributionPerDelivery = deliveries <= 0
            ? 0
            : Math.Round(result.MonthlyGrossContribution / deliveries, 2);

        result.BreakEvenDeliveriesPerMonth = result.ContributionPerDelivery <= 0
            ? 0
            : Math.Round((double)(result.MonthlyFixedCosts / result.ContributionPerDelivery), 0);

        result.BreakEvenDeliveriesPerDay = result.BreakEvenDeliveriesPerMonth <= 0
            ? 0
            : Math.Round(result.BreakEvenDeliveriesPerMonth / days, 1);

        result.DeliveriesPerTankerPerDay = input.Tankers <= 0
            ? 0
            : Math.Round((double)deliveriesPerDay / input.Tankers, 1);

        result.Assumptions.Add(_t.Format("economics.assumption.spread", Fmt.N(settings.ProcurementSpreadPerLitre, 2)));
        result.Assumptions.Add(_t.Format("economics.assumption.margin", Fmt.N(input.AverageMarginPerLitre, 2)));
        result.Assumptions.Add(_t.Format("economics.assumption.distance", Fmt.N(input.AverageDistanceKm, 1)));
        result.Assumptions.Add(_t.Format("economics.assumption.cost",
            Fmt.N(settings.TankerCostPerKm, 2), Fmt.N(settings.DriverCostPerHour, 0), Fmt.N(settings.FixedDispatchCost, 0)));
        result.Assumptions.Add(_t.Format("economics.assumption.route",
            Math.Max(1, settings.AverageStopsPerRoute), Fmt.N(settings.InterStopKm, 1), settings.ServiceMinutesPerStop, input.GroupedSharePercent));
        result.Assumptions.Add(_t["economics.assumption.excluded"]);
        result.Assumptions.Add(_t["economics.assumption.estimate"]);

        return result;
    }

    public SavingsResultVm CalculateSavings(SavingsInputVm input)
    {
        var result = new SavingsResultVm { Input = input };

        var litres = (decimal)Math.Max(0, input.LitresPerMonth);
        var trips = Math.Max(0, input.StationTripsPerMonth);

        result.HoursSavedPerMonth = Math.Round(trips * Math.Max(0, input.MinutesPerTrip) / 60.0, 1);
        result.KilometresSavedPerMonth = Math.Round(trips * Math.Max(0, input.DetourKmPerTrip), 0);

        result.StationFuelCost = Math.Round(litres * Math.Max(0, input.StationPricePerLitre), 0);
        result.StationLabourCost = Math.Round((decimal)result.HoursSavedPerMonth * Math.Max(0, input.DriverCostPerHour), 0);
        result.StationDrivingCost = Math.Round((decimal)result.KilometresSavedPerMonth * Math.Max(0, input.VehicleCostPerKm), 0);
        result.StationTotalCost = result.StationFuelCost + result.StationLabourCost + result.StationDrivingCost;

        result.ReFuelFuelCost = Math.Round(litres * Math.Max(0, input.ReFuelPricePerLitre), 0);
        result.ReFuelDeliveryCost = Math.Round(Math.Max(0, input.ReFuelDeliveryFeePerMonth), 0);
        result.ReFuelTotalCost = result.ReFuelFuelCost + result.ReFuelDeliveryCost;

        result.MonthlyDifference = result.StationTotalCost - result.ReFuelTotalCost;
        result.YearlyDifference = result.MonthlyDifference * 12;

        result.CheaperPerLitre = input.ReFuelPricePerLitre <= input.StationPricePerLitre;
        result.CheaperOverall = result.MonthlyDifference > 0;

        result.Assumptions.Add(_t.Format("savings.assumption.trips", trips, Fmt.N(input.MinutesPerTrip, 0)));
        result.Assumptions.Add(_t.Format("savings.assumption.labour", Fmt.N(input.DriverCostPerHour, 0), Fmt.N(input.VehicleCostPerKm, 0)));
        result.Assumptions.Add(_t["savings.assumption.total"]);
        result.Assumptions.Add(_t["savings.assumption.estimate"]);

        return result;
    }
}
