using System.ComponentModel.DataAnnotations;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Models.ViewModels;

public class TrackingStepVm
{
    public OrderStatus Status { get; set; }
    public bool Done { get; set; }
    public bool Active { get; set; }
    public DateTime? AtUtc { get; set; }
}

public class TrackingVm
{
    public Order Order { get; set; } = new();
    public DeliveryVehicle? Driver { get; set; }

    public double OriginLat { get; set; }
    public double OriginLng { get; set; }
    public double CurrentLat { get; set; }
    public double CurrentLng { get; set; }
    public double DestinationLat { get; set; }
    public double DestinationLng { get; set; }

    public int ProgressPercent { get; set; }
    public double RemainingKm { get; set; }
    public double RemainingMinutes { get; set; }
    public DateTime? EtaUtc { get; set; }

    public List<TrackingStepVm> Steps { get; set; } = new();
}

public class BusinessOverviewVm
{
    public BusinessAccount Account { get; set; } = new();
    public List<SupplyContract> Contracts { get; set; } = new();
    public List<ContractDelivery> Upcoming { get; set; } = new();
    public List<ContractDelivery> Recent { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();

    public int TotalAssets { get; set; }
    public double LitresThisMonth { get; set; }
    public double LitresTotal { get; set; }
    public decimal SpendThisMonth { get; set; }
    public decimal SavedByContract { get; set; }
    public decimal OutstandingAmount { get; set; }
    public int OverdueCount { get; set; }
    public int DeliveriesCompleted { get; set; }
}

public class ContractDetailsVm
{
    public SupplyContract Contract { get; set; } = new();
    public List<ContractDelivery> Deliveries { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();

    public double LitresThisMonth { get; set; }
    public double CommitmentPercent { get; set; }
    public decimal SavedByContract { get; set; }
    public ContractDelivery? NextDelivery { get; set; }
}

public class ConditionsVm
{
    public List<WeatherSnapshot> Weather { get; set; } = new();
    public List<RoadCondition> Roads { get; set; } = new();
    public List<BorderCrossing> Borders { get; set; } = new();
    public WeatherSnapshot? Local { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
    public int HazardCount { get; set; }
}

public class QuickOrderVm
{
    public int FuelTypeId { get; set; }

    [Range(10, 400)]
    public double Litres { get; set; } = 40;

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(120)]
    public string City { get; set; } = "Скопје";

    public OrderType Type { get; set; } = OrderType.Standard;

    public List<FuelType> Fuels { get; set; } = new();
    public decimal EstimatedUnitPrice { get; set; }
    public decimal EstimatedTotal { get; set; }
    public double EtaMinutes { get; set; }
    public int AvailableDrivers { get; set; }
}
