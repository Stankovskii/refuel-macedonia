using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReFuel_Macedonia.Models;

public class PricingSettings
{
    public int Id { get; set; }

    public decimal TankerCostPerKm { get; set; } = 11m;
    public decimal DriverCostPerHour { get; set; } = 320m;
    public decimal FixedDispatchCost { get; set; } = 60m;

    public decimal MonthlyFixedCosts { get; set; } = 420000m;

    public decimal VatRate { get; set; } = 18m;

    public double AverageSpeedKph { get; set; } = 46;
    public bool ChargeReturnLeg { get; set; } = true;

    /// <summary>
    /// How far below the pump (ERC retail) price ReFuel buys fuel, ден/л. The regulator publishes a
    /// maximum wholesale and a maximum retail price for each product; the gap between them is the
    /// retail trading margin — the margin a distributor without a forecourt keeps for itself.
    /// Customers are quoted at or below the pump price, and this spread is where the fuel margin
    /// comes from. Verify the figure against the current ERC decision.
    /// </summary>
    public decimal ProcurementSpreadPerLitre { get; set; } = 4.00m;

    /// <summary>
    /// A tanker on a standard or scheduled round serves several customers per loop. These three
    /// values turn "one round trip per order" into "this order's share of a route".
    /// </summary>
    public int AverageStopsPerRoute { get; set; } = 6;
    public double InterStopKm { get; set; } = 3;
    public int ServiceMinutesPerStop { get; set; } = 10;

    /// <summary>
    /// A contract-priced drop below this size is charged the normal distance-based delivery fee
    /// instead of the contract's (often zero) fixed fee, so token top-ups never ride for free.
    /// Contract drops are per asset (a truck, a bus, a boat), typically 300–1 500 litres.
    /// </summary>
    public double ContractMinDropLitres { get; set; } = 300;

    /// <summary>
    /// Version of the pricing model this database has been tuned for. Bumped by the seeder's
    /// one-time upgrade so an existing database picks up new defaults without being recreated.
    /// </summary>
    public int PricingModelVersion { get; set; } = PricingModelVersions.Current;

    public bool ShowPriceExplanation { get; set; } = true;
    public bool ShowMarginToCustomer { get; set; }

    [MaxLength(120)]
    public string UpdatedBy { get; set; } = "seed";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class PricingModelVersions
{
    /// <summary>
    /// 1 = markup above the pump price. 2 = wholesale spread, pump-price parity, route-based
    /// delivery cost. 3 = demo history rebuilt at committed volumes with nearest-tanker dispatch,
    /// delivery-only promotions, normalised city names.
    /// </summary>
    public const int Current = 3;
}

public class PricingTier
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(120)]
    public string NameMk { get; set; } = string.Empty;

    [MaxLength(120)]
    public string NameEn { get; set; } = string.Empty;

    [MaxLength(120)]
    public string NameSq { get; set; } = string.Empty;

    public CustomerType CustomerType { get; set; } = CustomerType.Individual;

    /// <summary>ReFuel's margin above its wholesale purchase price, ден/л, before any discount.</summary>
    public decimal MarginPerLitre { get; set; }

    /// <summary>
    /// The part of the margin that volume bands and negotiated contract discounts may never eat into.
    /// Anything an administrator enters below this floor is clamped, and the quote shows a warning.
    /// </summary>
    public decimal MinMarginPerLitre { get; set; }

    public decimal MinDeliveryFee { get; set; } = 150m;
    public decimal DeliveryFeePerKm { get; set; } = 18m;
    public decimal FreeDeliveryThreshold { get; set; } = 4500m;
    public decimal EmergencySurcharge { get; set; } = 350m;

    /// <summary>
    /// Discount on the delivery fee when the customer picks a scheduled window instead of "today".
    /// A scheduled drop can be grouped into a route, which is what makes it cheaper to serve.
    /// </summary>
    public int ScheduledDeliveryDiscountPercent { get; set; }

    /// <summary>When true, the free-delivery threshold only applies to scheduled orders.</summary>
    public bool FreeDeliveryScheduledOnly { get; set; }

    public double MinOrderLitres { get; set; } = 10;
    public double MaxOrderLitres { get; set; } = 120;

    public bool IsNegotiated { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    [MaxLength(200)]
    public string? MarketingMessageMk { get; set; }

    [MaxLength(200)]
    public string? MarketingMessageEn { get; set; }

    [MaxLength(200)]
    public string? MarketingMessageSq { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<VolumeDiscountBand> Bands { get; set; } = new List<VolumeDiscountBand>();

    public string LocalizedName(string culture) => culture switch
    {
        "en" => string.IsNullOrWhiteSpace(NameEn) ? NameMk : NameEn,
        "sq" => string.IsNullOrWhiteSpace(NameSq) ? NameMk : NameSq,
        _ => string.IsNullOrWhiteSpace(NameMk) ? NameEn : NameMk
    };

    public string? MarketingMessage(string culture) => culture switch
    {
        "en" => MarketingMessageEn ?? MarketingMessageMk,
        "sq" => MarketingMessageSq ?? MarketingMessageMk,
        _ => MarketingMessageMk ?? MarketingMessageEn
    };
}

public class VolumeDiscountBand
{
    public int Id { get; set; }

    public int? PricingTierId { get; set; }
    public PricingTier? PricingTier { get; set; }

    public double FromLitresPerMonth { get; set; }
    public double? ToLitresPerMonth { get; set; }

    public decimal DiscountPerLitre { get; set; }

    [MaxLength(120)]
    public string LabelMk { get; set; } = string.Empty;

    [MaxLength(120)]
    public string LabelEn { get; set; } = string.Empty;

    [MaxLength(120)]
    public string LabelSq { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public string LocalizedLabel(string culture) => culture switch
    {
        "en" => string.IsNullOrWhiteSpace(LabelEn) ? LabelMk : LabelEn,
        "sq" => string.IsNullOrWhiteSpace(LabelSq) ? LabelMk : LabelSq,
        _ => string.IsNullOrWhiteSpace(LabelMk) ? LabelEn : LabelMk
    };

    [NotMapped]
    public string Range => ToLitresPerMonth.HasValue
        ? $"{FromLitresPerMonth:0} – {ToLitresPerMonth.Value:0} L"
        : $"{FromLitresPerMonth:0}+ L";

    public bool Covers(double litresPerMonth) =>
        litresPerMonth >= FromLitresPerMonth &&
        (!ToLitresPerMonth.HasValue || litresPerMonth < ToLitresPerMonth.Value);
}

public class DeliveryZone
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(400)]
    public string Cities { get; set; } = string.Empty;

    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public double RadiusKm { get; set; } = 25;

    public decimal BaseDeliveryFee { get; set; } = 150m;
    public decimal FeePerKm { get; set; } = 18m;
    public double MinOrderLitres { get; set; } = 10;

    public double AverageSpeedKph { get; set; } = 46;

    [MaxLength(9)]
    public string ColorHex { get; set; } = "#FF6A00";

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<DeliveryVehicle> Tankers { get; set; } = new List<DeliveryVehicle>();

    public IEnumerable<string> CityList =>
        Cities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class TankerCompartment
{
    public int Id { get; set; }

    public int DeliveryVehicleId { get; set; }
    public DeliveryVehicle? DeliveryVehicle { get; set; }

    public int Number { get; set; } = 1;

    public double CapacityLitres { get; set; } = 500;
    public double CurrentLitres { get; set; }

    public int? FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public double FillPercent => CapacityLitres <= 0 ? 0 : Math.Round(CurrentLitres / CapacityLitres * 100, 0);

    [NotMapped]
    public double FreeLitres => Math.Max(0, Math.Round(CapacityLitres - CurrentLitres, 1));
}

public class TankerTelemetry
{
    public int Id { get; set; }

    public int DeliveryVehicleId { get; set; }
    public DeliveryVehicle? DeliveryVehicle { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKph { get; set; }
    public double HeadingDegrees { get; set; }

    public double LitresOnBoard { get; set; }
    public double OdometerKm { get; set; }

    public bool PumpActive { get; set; }
    public bool EngineOn { get; set; }

    public TelemetrySource Source { get; set; } = TelemetrySource.Simulator;

    [MaxLength(400)]
    public string? Note { get; set; }
}

public class ConstructionSite
{
    public int Id { get; set; }

    public int BusinessAccountId { get; set; }
    public BusinessAccount? BusinessAccount { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(120)]
    public string City { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public int? PrimaryFuelTypeId { get; set; }
    public FuelType? PrimaryFuelType { get; set; }

    public double WeeklyConsumptionLitres { get; set; }

    [MaxLength(150)]
    public string ContactPerson { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ContactPhone { get; set; } = string.Empty;

    public TimeSpan AccessFrom { get; set; } = new(6, 0, 0);
    public TimeSpan AccessTo { get; set; } = new(18, 0, 0);

    [MaxLength(600)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ContractAsset> Machines { get; set; } = new List<ContractAsset>();

    [NotMapped]
    public double MonthlyConsumptionLitres => Math.Round(WeeklyConsumptionLitres * 4.33, 0);
}

public class DeliverySchedule
{
    public int Id { get; set; }

    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    public ScheduleKind Kind { get; set; } = ScheduleKind.Recurring;
    public DeliveryFrequency Frequency { get; set; } = DeliveryFrequency.Weekly;

    public int? BusinessAccountId { get; set; }
    public BusinessAccount? BusinessAccount { get; set; }

    public int? SupplyContractId { get; set; }
    public SupplyContract? SupplyContract { get; set; }

    public int? ConstructionSiteId { get; set; }
    public ConstructionSite? ConstructionSite { get; set; }

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    [MaxLength(24)]
    public string DaysOfWeek { get; set; } = "1,3,5";

    public TimeSpan WindowStart { get; set; } = new(22, 0, 0);
    public TimeSpan WindowEnd { get; set; } = new(2, 0, 0);

    public double LitresPerVisit { get; set; } = 500;

    [MaxLength(250)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(120)]
    public string City { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public DateTime StartsOn { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EndsOn { get; set; }

    public double? TriggerBelowPercent { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastGeneratedUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public IEnumerable<DayOfWeek> Days
    {
        get
        {
            foreach (var part in DaysOfWeek.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var value) && value is >= 0 and <= 6)
                    yield return (DayOfWeek)value;
            }
        }
    }

    [NotMapped]
    public double MonthlyLitres => Kind == ScheduleKind.OneTime
        ? LitresPerVisit
        : Math.Round(LitresPerVisit * VisitsPerMonth, 0);

    [NotMapped]
    public double VisitsPerMonth => Frequency switch
    {
        DeliveryFrequency.Nightly => 30,
        DeliveryFrequency.Weekly => Math.Max(1, Days.Count()) * 4.33,
        DeliveryFrequency.Biweekly => Math.Max(1, Days.Count()) * 2.17,
        DeliveryFrequency.Monthly => 1,
        _ => 0
    };
}

public class FuelAnomaly
{
    public int Id { get; set; }

    public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;

    public AnomalyKind Kind { get; set; }
    public AnomalySeverity Severity { get; set; } = AnomalySeverity.Attention;
    public AnomalyStatus Status { get; set; } = AnomalyStatus.Open;

    public int? DeliveryVehicleId { get; set; }
    public DeliveryVehicle? DeliveryVehicle { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public int? ContractDeliveryId { get; set; }
    public ContractDelivery? ContractDelivery { get; set; }

    public int? BusinessAccountId { get; set; }
    public BusinessAccount? BusinessAccount { get; set; }

    public double ExpectedLitres { get; set; }
    public double ObservedLitres { get; set; }

    [MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(600)]
    public string Detail { get; set; } = string.Empty;

    [MaxLength(600)]
    public string? Explanation { get; set; }

    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    [NotMapped]
    public double VarianceLitres => Math.Round(ObservedLitres - ExpectedLitres, 1);

    [NotMapped]
    public double VariancePercent => ExpectedLitres <= 0
        ? 0
        : Math.Round((ObservedLitres - ExpectedLitres) / ExpectedLitres * 100, 1);
}

public class BusinessOffer
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string OfferNumber { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string ContactPerson { get; set; } = string.Empty;

    [MaxLength(150)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ContactPhone { get; set; } = string.Empty;

    [MaxLength(120)]
    public string City { get; set; } = string.Empty;

    public CustomerType CustomerType { get; set; } = CustomerType.Fleet;

    public int? BusinessAccountId { get; set; }
    public BusinessAccount? BusinessAccount { get; set; }

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    public double EstimatedLitresPerMonth { get; set; }
    public int DeliveriesPerMonth { get; set; } = 20;
    public double AverageDistanceKm { get; set; } = 14;

    public decimal BaseCostPerLitre { get; set; }
    public decimal ProposedMarginPerLitre { get; set; }
    public decimal VolumeDiscountPerLitre { get; set; }
    public decimal ProposedDeliveryFee { get; set; }

    public decimal EstimatedMonthlyRevenue { get; set; }
    public decimal EstimatedFuelCost { get; set; }
    public decimal EstimatedGrossFuelMargin { get; set; }
    public decimal EstimatedDeliveryCost { get; set; }
    public decimal EstimatedGrossContribution { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Draft;

    public string? CreatedByUserId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ValidUntil { get; set; } = DateTime.UtcNow.Date.AddDays(30);

    [NotMapped]
    public bool IsExpired => Status == OfferStatus.Sent && ValidUntil < DateTime.UtcNow.Date;

    [NotMapped]
    public decimal EffectivePricePerLitre =>
        Math.Round(BaseCostPerLitre + ProposedMarginPerLitre - VolumeDiscountPerLitre, 2);

    /// <summary>The pump price the offer was built against: wholesale basis + procurement spread.</summary>
    public decimal PumpPricePerLitre(PricingSettings settings) =>
        Math.Round(BaseCostPerLitre + Math.Max(0, settings.ProcurementSpreadPerLitre), 2);

    /// <summary>
    /// Re-derives every estimate from the inputs. BaseCostPerLitre is ReFuel's purchase price
    /// (pump − spread); a contract drop is costed as a dedicated trip.
    /// </summary>
    public void Recalculate(PricingSettings settings)
    {
        var litres = (decimal)Math.Max(0, EstimatedLitresPerMonth);
        var deliveries = (decimal)Math.Max(0, DeliveriesPerMonth);

        var effectiveMargin = Math.Max(0, ProposedMarginPerLitre - VolumeDiscountPerLitre);
        var costPerDelivery = Infrastructure.DeliveryCosting.DedicatedTrip(settings, Math.Max(0, AverageDistanceKm)).Total;

        EstimatedFuelCost = Math.Round(litres * BaseCostPerLitre, 0);
        EstimatedGrossFuelMargin = Math.Round(litres * effectiveMargin, 0);
        EstimatedDeliveryCost = Math.Round(deliveries * costPerDelivery, 0);
        EstimatedMonthlyRevenue = Math.Round(litres * (BaseCostPerLitre + effectiveMargin) + deliveries * ProposedDeliveryFee, 0);
        EstimatedGrossContribution = EstimatedGrossFuelMargin + Math.Round(deliveries * ProposedDeliveryFee, 0) - EstimatedDeliveryCost;
    }
}
