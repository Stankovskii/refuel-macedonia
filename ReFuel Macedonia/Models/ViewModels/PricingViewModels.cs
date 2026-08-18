using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Models.ViewModels;

public class PriceLine
{
    public string Label { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public decimal Amount { get; set; }
    public string Kind { get; set; } = "line";
    public bool IsInternal { get; set; }
}

public class PricingRequest
{
    public CustomerType CustomerType { get; set; } = CustomerType.Individual;

    public int? PricingTierId { get; set; }

    /// <summary>
    /// The pump (ERC retail) price the customer would pay at a station. The quote is built on this:
    /// ReFuel buys at pump − procurement spread and sells at or below the pump.
    /// </summary>
    public decimal PumpPricePerLitre { get; set; }

    /// <summary>Legacy input: treated as the pump price when PumpPricePerLitre is not supplied.</summary>
    public decimal BaseCostPerLitre { get; set; }

    public double Litres { get; set; }
    public double DistanceKm { get; set; }

    /// <summary>Standard = today, Scheduled = chosen window (grouped, cheaper delivery), Emergency = dedicated tanker.</summary>
    public OrderType OrderType { get; set; } = OrderType.Standard;

    public bool IsEmergency { get; set; }

    /// <summary>Contract drops: one tanker, one depot, full round trip — never shared with other stops.</summary>
    public bool DedicatedTrip { get; set; }

    public double MonthlyLitres { get; set; }

    public decimal ContractDiscountPerLitre { get; set; }
    public decimal? ContractFixedDeliveryFee { get; set; }
    public decimal? NegotiatedMarginPerLitre { get; set; }

    public decimal PromotionDiscount { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? City { get; set; }

    public bool IncludeVat { get; set; }
}

public class PriceBreakdown
{
    public int? PricingTierId { get; set; }
    public string TierName { get; set; } = string.Empty;
    public CustomerType CustomerType { get; set; }

    public int? DeliveryZoneId { get; set; }
    public string? ZoneName { get; set; }

    public double Litres { get; set; }
    public double DistanceKm { get; set; }
    public double MonthlyLitres { get; set; }

    /// <summary>What the customer would pay per litre at a station — the reference the quote is built on.</summary>
    public decimal PumpPricePerLitre { get; set; }

    /// <summary>How far below the pump price ReFuel buys (from settings).</summary>
    public decimal ProcurementSpreadPerLitre { get; set; }

    /// <summary>ReFuel's purchase price: pump − spread.</summary>
    public decimal BaseCostPerLitre { get; set; }
    public decimal MarginPerLitre { get; set; }
    public decimal MinMarginPerLitre { get; set; }
    public decimal VolumeDiscountPerLitre { get; set; }
    public decimal ContractDiscountPerLitre { get; set; }
    public decimal EffectivePricePerLitre { get; set; }

    /// <summary>pump − effective price. Positive = the customer pays less than at the station.</summary>
    public decimal CustomerDiscountPerLitre { get; set; }

    /// <summary>The tier's built-in discount below the pump price (spread − margin), before bands and contracts.</summary>
    public decimal TierDiscountPerLitre { get; set; }

    public decimal FuelCost { get; set; }
    public decimal FuelRevenue { get; set; }
    public decimal GrossFuelMargin { get; set; }

    public decimal DeliveryFee { get; set; }
    public decimal DeliveryFeeBeforeDiscount { get; set; }
    public decimal ScheduledFeeDiscount { get; set; }
    public int ScheduledDiscountPercent { get; set; }
    public decimal TierEmergencySurcharge { get; set; }
    public bool FreeDeliveryScheduledOnly { get; set; }

    public OrderType OrderType { get; set; } = OrderType.Standard;
    public bool IsScheduled => OrderType == OrderType.Scheduled;
    public bool IsEmergency => OrderType == OrderType.Emergency;

    public decimal DeliveryCost { get; set; }
    public decimal DeliveryCostDriver { get; set; }
    public decimal DeliveryCostTanker { get; set; }
    public decimal DeliveryCostDispatch { get; set; }
    public double DeliveryCostKm { get; set; }
    public double DeliveryCostHours { get; set; }
    public bool DeliveryGrouped { get; set; }

    public decimal EmergencySurcharge { get; set; }
    public decimal PromotionDiscount { get; set; }

    public decimal Subtotal { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }

    public decimal GrossContribution { get; set; }

    public double MinOrderLitres { get; set; }
    public decimal MinDeliveryFee { get; set; }

    public string? VolumeBandLabel { get; set; }
    public string? MarketingMessage { get; set; }

    public bool ShowExplanation { get; set; } = true;
    public bool ShowMargin { get; set; }

    public List<PriceLine> Lines { get; set; } = new();
    public List<PriceLine> InternalLines { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public decimal ContributionPerLitre => Litres <= 0 ? 0 : Math.Round(GrossContribution / (decimal)Litres, 2);

    public double GrossContributionPercent => Total <= 0 ? 0 : Math.Round((double)(GrossContribution / Total) * 100, 1);
}

public class EconomicsInputVm
{
    public int DeliveriesPerDay { get; set; } = 60;
    public double AverageLitresPerDelivery { get; set; } = 45;
    public double AverageDistanceKm { get; set; } = 12;
    public decimal AverageMarginPerLitre { get; set; } = 4m;
    public decimal AverageDeliveryFee { get; set; } = 250m;
    /// <summary>Share of deliveries that ride on a grouped route (standard + scheduled) rather than a dedicated trip.</summary>
    public int GroupedSharePercent { get; set; } = 85;
    public int WorkingDaysPerMonth { get; set; } = 26;
    public decimal MonthlyFixedCosts { get; set; } = 420000m;
    public int Tankers { get; set; } = 8;
}

public class EconomicsResultVm
{
    public EconomicsInputVm Input { get; set; } = new();

    public double MonthlyDeliveries { get; set; }
    public double MonthlyLitres { get; set; }

    public decimal MonthlyFuelRevenue { get; set; }
    public decimal MonthlyDeliveryRevenue { get; set; }
    public decimal MonthlyRevenue { get; set; }

    public decimal MonthlyGrossFuelMargin { get; set; }
    public decimal MonthlyDeliveryCost { get; set; }
    public decimal MonthlyGrossContribution { get; set; }

    public decimal MonthlyFixedCosts { get; set; }
    public decimal OperatingResult { get; set; }

    public decimal CostPerDelivery { get; set; }
    public decimal ContributionPerDelivery { get; set; }
    public double BreakEvenDeliveriesPerMonth { get; set; }
    public double BreakEvenDeliveriesPerDay { get; set; }
    public double DeliveriesPerTankerPerDay { get; set; }

    public List<string> Assumptions { get; set; } = new();
}

public class SavingsInputVm
{
    public double LitresPerMonth { get; set; } = 6000;
    public int VehicleCount { get; set; } = 12;
    public int StationTripsPerMonth { get; set; } = 60;
    public double MinutesPerTrip { get; set; } = 35;
    public double DetourKmPerTrip { get; set; } = 6;
    public decimal DriverCostPerHour { get; set; } = 380m;
    public decimal VehicleCostPerKm { get; set; } = 14m;
    public decimal ReFuelPricePerLitre { get; set; } = 77m;
    public decimal StationPricePerLitre { get; set; } = 78.5m;
    public decimal ReFuelDeliveryFeePerMonth { get; set; } = 2400m;
}

public class SavingsResultVm
{
    public SavingsInputVm Input { get; set; } = new();

    public decimal StationFuelCost { get; set; }
    public decimal StationLabourCost { get; set; }
    public decimal StationDrivingCost { get; set; }
    public decimal StationTotalCost { get; set; }

    public decimal ReFuelFuelCost { get; set; }
    public decimal ReFuelDeliveryCost { get; set; }
    public decimal ReFuelTotalCost { get; set; }

    public decimal MonthlyDifference { get; set; }
    public decimal YearlyDifference { get; set; }

    public double HoursSavedPerMonth { get; set; }
    public double KilometresSavedPerMonth { get; set; }

    public bool CheaperPerLitre { get; set; }
    public bool CheaperOverall { get; set; }

    public List<string> Assumptions { get; set; } = new();
}

public class PricingAdminVm
{
    public PricingSettings Settings { get; set; } = new();
    public List<PricingTier> Tiers { get; set; } = new();
    public List<VolumeDiscountBand> Bands { get; set; } = new();
    public List<DeliveryZone> Zones { get; set; } = new();
    public PriceBreakdown? Preview { get; set; }
    public PricingRequest PreviewRequest { get; set; } = new();
    public List<FuelType> Fuels { get; set; } = new();
    public int SelectedFuelTypeId { get; set; }
    public decimal SampleBaseCost { get; set; }
}

public class CompanyProfitVm
{
    public int? BusinessAccountId { get; set; }

    public string Company { get; set; } = string.Empty;
    public string? City { get; set; }
    public CustomerType CustomerType { get; set; }
    public string? TierName { get; set; }

    public double Litres { get; set; }
    public int Deliveries { get; set; }

    public decimal Revenue { get; set; }
    public decimal FuelCost { get; set; }
    public decimal GrossFuelMargin { get; set; }

    public decimal DeliveryRevenue { get; set; }
    public decimal DeliveryCost { get; set; }
    public decimal Discounts { get; set; }

    public decimal DriverCost { get; set; }
    public decimal TankerCost { get; set; }
    public decimal DispatchCost { get; set; }

    public decimal GrossContribution { get; set; }

    public double AverageDistanceKm { get; set; }

    public decimal MarginPerLitre => Litres <= 0 ? 0 : Math.Round(GrossFuelMargin / (decimal)Litres, 2);

    public decimal ContributionPerDelivery => Deliveries <= 0 ? 0 : Math.Round(GrossContribution / Deliveries, 0);

    public decimal ContributionPerLitre => Litres <= 0 ? 0 : Math.Round(GrossContribution / (decimal)Litres, 2);

    public double ContributionPercent => Revenue <= 0 ? 0 : Math.Round((double)(GrossContribution / Revenue) * 100, 1);

    public bool IsProfitable => GrossContribution > 0;
}

public class ProfitabilityVm
{
    public int Days { get; set; } = 90;

    public PricingSettings Settings { get; set; } = new();

    public List<CompanyProfitVm> Companies { get; set; } = new();
    public CompanyProfitVm Individuals { get; set; } = new();
    public CompanyProfitVm Total { get; set; } = new();

    public decimal PeriodFixedCosts { get; set; }
    public decimal OperatingResult { get; set; }

    public List<string> Assumptions { get; set; } = new();

    public IEnumerable<CompanyProfitVm> AllRows =>
        Companies.Concat(Individuals.Deliveries > 0 ? new[] { Individuals } : Array.Empty<CompanyProfitVm>());
}
