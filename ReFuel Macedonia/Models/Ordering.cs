using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReFuel_Macedonia.Models;

public class Order
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Reference { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? StationId { get; set; }
    public Station? Station { get; set; }

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public int? DeliveryVehicleId { get; set; }
    public DeliveryVehicle? DeliveryVehicle { get; set; }

    [Range(5, 400)]
    public double Litres { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal FuelSubtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal EmergencySurcharge { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }

    public OrderType Type { get; set; } = OrderType.Standard;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    [Required, MaxLength(250)]
    public string DeliveryAddress { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DeliveryCity { get; set; } = string.Empty;

    public double DeliveryLatitude { get; set; }
    public double DeliveryLongitude { get; set; }

    [MaxLength(50)]
    public string? ContactPhone { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime? ScheduledForUtc { get; set; }
    public DateTime? EtaUtc { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }

    public double DistanceKm { get; set; }

    public int? PromotionId { get; set; }
    public Promotion? Promotion { get; set; }

    public int PointsEarned { get; set; }

    public int? PricingTierId { get; set; }
    public PricingTier? PricingTier { get; set; }

    public int? DeliveryZoneId { get; set; }
    public DeliveryZone? DeliveryZone { get; set; }

    /// <summary>The pump (ERC retail) price the customer would have paid at a station — the reference the quote is built on.</summary>
    public decimal PumpPricePerLitre { get; set; }

    public decimal BaseCostPerLitre { get; set; }
    public decimal MarginPerLitre { get; set; }
    public decimal VolumeDiscountPerLitre { get; set; }
    public decimal ContractDiscountPerLitre { get; set; }
    public decimal DeliveryCost { get; set; }
    public decimal GrossFuelMargin { get; set; }
    public decimal GrossContribution { get; set; }

    public DeliveryPriority Priority { get; set; } = DeliveryPriority.Standard;

    public int? BusinessAccountId { get; set; }
    public BusinessAccount? BusinessAccount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();

    [NotMapped]
    public bool IsOpen => Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Assigned
                                  or OrderStatus.OnTheWay or OrderStatus.Arrived;

    [NotMapped]
    public int ProgressPercent => Status switch
    {
        OrderStatus.Pending => 10,
        OrderStatus.Confirmed => 30,
        OrderStatus.Assigned => 50,
        OrderStatus.OnTheWay => 75,
        OrderStatus.Arrived => 92,
        OrderStatus.Delivered => 100,
        _ => 0
    };
}

public class OrderStatusHistory
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public OrderStatus Status { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DeliveryVehicle
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DriverName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(300)]
    public string? DriverPhotoUrl { get; set; }

    [MaxLength(200)]
    public string? DriverBio { get; set; }

    public double DriverRating { get; set; } = 4.8;
    public int CompletedDeliveries { get; set; }
    public int YearsOfExperience { get; set; } = 3;

    [MaxLength(120)]
    public string TruckModel { get; set; } = "MAN TGM 18.290";

    [MaxLength(20)]
    public string PlateNumber { get; set; } = string.Empty;

    public double CapacityLitres { get; set; } = 1000;

    public double BaseLatitude { get; set; }
    public double BaseLongitude { get; set; }

    [MaxLength(120)]
    public string BaseCity { get; set; } = "Скопје";

    public bool IsAvailable { get; set; } = true;

    public bool HasHazmatLicence { get; set; } = true;
    public bool CanServeBoats { get; set; }
    public bool CanServeConstruction { get; set; } = true;

    public int? DeliveryZoneId { get; set; }
    public DeliveryZone? DeliveryZone { get; set; }

    public double CurrentLatitude { get; set; }
    public double CurrentLongitude { get; set; }
    public double OdometerKm { get; set; }
    public double SpeedKph { get; set; }
    public bool PumpActive { get; set; }
    public bool IsInMaintenance { get; set; }

    public DateTime? LastTelemetryUtc { get; set; }
    public DateTime? MaintenanceDueOn { get; set; }
    public DateTime? InsuranceValidUntil { get; set; }

    public ICollection<TankerCompartment> Compartments { get; set; } = new List<TankerCompartment>();

    [NotMapped]
    public double LoadedLitres => Compartments.Count == 0 ? 0 : Math.Round(Compartments.Sum(c => c.CurrentLitres), 1);

    [NotMapped]
    public double CompartmentCapacityLitres => Compartments.Count == 0 ? CapacityLitres : Math.Round(Compartments.Sum(c => c.CapacityLitres), 1);

    [NotMapped]
    public double LoadPercent
    {
        get
        {
            var capacity = CompartmentCapacityLitres;
            return capacity <= 0 ? 0 : Math.Round(LoadedLitres / capacity * 100, 0);
        }
    }

    [NotMapped]
    public bool TelemetryIsFresh => LastTelemetryUtc.HasValue && (DateTime.UtcNow - LastTelemetryUtc.Value).TotalMinutes <= 20;

    public double AvailableLitres(int fuelTypeId) =>
        Compartments.Where(c => c.FuelTypeId == fuelTypeId).Sum(c => c.CurrentLitres);

    public string Initials
    {
        get
        {
            var parts = DriverName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "RF";
            return parts.Length == 1
                ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
                : string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
        }
    }
}

public class RoutePlan
{
    public int Id { get; set; }

    public int DeliveryVehicleId { get; set; }
    public DeliveryVehicle? DeliveryVehicle { get; set; }

    public DateTime PlannedForUtc { get; set; }

    public double TotalDistanceKm { get; set; }
    public double EstimatedDurationMinutes { get; set; }
    public double TotalLitres { get; set; }
    public double ImprovementPercent { get; set; }

    [MaxLength(120)]
    public string Algorithm { get; set; } = "Nearest neighbour + 2-opt";

    public int? DeliveryZoneId { get; set; }
    public DeliveryZone? DeliveryZone { get; set; }

    [MaxLength(120)]
    public string? ZoneName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<RouteStop> Stops { get; set; } = new List<RouteStop>();
}

public class RouteStop
{
    public int Id { get; set; }

    public int RoutePlanId { get; set; }
    public RoutePlan? RoutePlan { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int Sequence { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public double LegDistanceKm { get; set; }

    public DateTime? EtaUtc { get; set; }

    public DeliveryPriority Priority { get; set; } = DeliveryPriority.Standard;

    public double Litres { get; set; }

    public int? FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    [MaxLength(250)]
    public string Label { get; set; } = string.Empty;
}
