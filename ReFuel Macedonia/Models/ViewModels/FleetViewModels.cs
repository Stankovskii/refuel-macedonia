using System.ComponentModel.DataAnnotations;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Models.ViewModels;

public class TelemetryInput
{
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    [Range(0, 200)]
    public double SpeedKph { get; set; }

    [Range(0, 360)]
    public double HeadingDegrees { get; set; }

    [Range(0, 60000)]
    public double LitresOnBoard { get; set; }

    [Range(0, 5000000)]
    public double OdometerKm { get; set; }

    public bool PumpActive { get; set; }
    public bool EngineOn { get; set; } = true;

    public TelemetrySource Source { get; set; } = TelemetrySource.Device;

    [MaxLength(400)]
    public string? Note { get; set; }

    public DateTime? RecordedAtUtc { get; set; }
}

public class TankerVm
{
    public DeliveryVehicle Tanker { get; set; } = null!;
    public List<TankerCompartment> Compartments { get; set; } = new();
    public TankerTelemetry? Latest { get; set; }
    public int OpenOrders { get; set; }
    public double AssignedLitres { get; set; }
    public string? ZoneName { get; set; }
    public string? ZoneColor { get; set; }
}

public class DispatchVm
{
    public List<DispatchItemVm> Queue { get; set; } = new();
    public List<TankerVm> Tankers { get; set; } = new();
    public List<DeliveryZone> Zones { get; set; } = new();
    public List<ZoneRouteVm> Routes { get; set; } = new();

    public int EmergencyCount { get; set; }
    public int ScheduledCount { get; set; }
    public int ContractCount { get; set; }
    public int StandardCount { get; set; }

    public double TotalLitres { get; set; }
    public int OpenAnomalies { get; set; }
}

public class DispatchItemVm
{
    public Order Order { get; set; } = null!;
    public DeliveryPriority Priority { get; set; }
    public string? ZoneName { get; set; }
    public string? ZoneColor { get; set; }
    public int? ZoneId { get; set; }
    public bool StockAvailable { get; set; } = true;
    public int WaitingMinutes { get; set; }
}

public class ZoneRouteVm
{
    public int? ZoneId { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public string ZoneColor { get; set; } = "#FF6A00";

    public DeliveryVehicle? Tanker { get; set; }

    public List<DispatchItemVm> Stops { get; set; } = new();

    public double TotalDistanceKm { get; set; }
    public double NaiveDistanceKm { get; set; }
    public double ImprovementPercent { get; set; }
    public double TotalLitres { get; set; }
    public double EstimatedMinutes { get; set; }

    public bool ExceedsCapacity { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class FleetDashboardVm
{
    public BusinessAccount Account { get; set; } = null!;

    public List<SupplyContract> Contracts { get; set; } = new();
    public List<ContractAsset> Assets { get; set; } = new();
    public List<ConstructionSite> Sites { get; set; } = new();
    public List<DeliverySchedule> Schedules { get; set; } = new();
    public List<ContractDelivery> UpcomingDeliveries { get; set; } = new();
    public List<FuelAnomaly> Alerts { get; set; } = new();

    public double LitresThisMonth { get; set; }
    public double LitresLastMonth { get; set; }
    public decimal SpendThisMonth { get; set; }
    public decimal AveragePricePerLitre { get; set; }
    public decimal DiscountPerLitre { get; set; }
    public decimal SavedByDiscount { get; set; }

    public int ActiveAssets { get; set; }
    public int ActiveSites { get; set; }
    public int OpenInvoices { get; set; }
    public decimal OpenInvoiceTotal { get; set; }

    public PricingTier? Tier { get; set; }
    public string? VolumeBandLabel { get; set; }

    public Dictionary<string, double> LitresByMonth { get; set; } = new();
    public Dictionary<string, double> LitresByAssetKind { get; set; } = new();
}

public class SiteFormVm
{
    public int Id { get; set; }
    public int BusinessAccountId { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(120)]
    public string? City { get; set; } = "Скопје";

    public double Latitude { get; set; } = 41.9981;
    public double Longitude { get; set; } = 21.4254;

    public int? PrimaryFuelTypeId { get; set; }
    public double WeeklyConsumptionLitres { get; set; } = 1000;

    [MaxLength(150)]
    public string? ContactPerson { get; set; }

    [MaxLength(50)]
    public string? ContactPhone { get; set; }

    public int AccessFromHour { get; set; } = 6;
    public int AccessToHour { get; set; } = 18;

    [MaxLength(600)]
    public string? Notes { get; set; }
}

public class ScheduleFormVm
{
    public int Id { get; set; }
    public int BusinessAccountId { get; set; }

    [Required, MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    public ScheduleKind Kind { get; set; } = ScheduleKind.Recurring;
    public DeliveryFrequency Frequency { get; set; } = DeliveryFrequency.Weekly;

    public int FuelTypeId { get; set; }
    public int? ConstructionSiteId { get; set; }

    public List<int> Days { get; set; } = new();

    public int WindowStartHour { get; set; } = 22;
    public int WindowEndHour { get; set; } = 2;

    public double LitresPerVisit { get; set; } = 500;

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(120)]
    public string? City { get; set; } = "Скопје";

    public double Latitude { get; set; } = 41.9981;
    public double Longitude { get; set; } = 21.4254;

    public double? TriggerBelowPercent { get; set; }
}

/// <summary>What the public verification page shows for a scanned proof of delivery or invoice.</summary>
public class VerifyDocumentVm
{
    public string? Kind { get; set; }
    public string? Number { get; set; }
    public string? Hash { get; set; }
    public bool Found { get; set; }
    public bool IsValid { get; set; }
    public DateTime? IssuedOn { get; set; }
    public double? Litres { get; set; }
    public decimal? Total { get; set; }
    public string? Company { get; set; }
    public string? Reference { get; set; }
    public string? ExpectedHash { get; set; }
}
