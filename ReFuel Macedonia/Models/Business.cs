using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReFuel_Macedonia.Models;

public class BusinessAccount
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string TaxNumber { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? RegistrationNumber { get; set; }

    [MaxLength(250)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(120)]
    public string City { get; set; } = string.Empty;

    [MaxLength(150)]
    public string ContactPerson { get; set; } = string.Empty;

    [MaxLength(150)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ContactPhone { get; set; } = string.Empty;

    [MaxLength(60)]
    public string? Industry { get; set; }

    public string? OwnerUserId { get; set; }
    public ApplicationUser? OwnerUser { get; set; }

    public int PaymentTermDays { get; set; } = 30;
    public decimal CreditLimit { get; set; } = 500000m;

    public CustomerType CustomerType { get; set; } = CustomerType.Fleet;

    public int? PricingTierId { get; set; }
    public PricingTier? PricingTier { get; set; }

    public decimal? NegotiatedMarginPerLitre { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<SupplyContract> Contracts { get; set; } = new List<SupplyContract>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<ConstructionSite> Sites { get; set; } = new List<ConstructionSite>();
}

public class SupplyContract
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string ContractNumber { get; set; } = string.Empty;

    public int BusinessAccountId { get; set; }
    public BusinessAccount? BusinessAccount { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    public DateTime StartsOn { get; set; }
    public DateTime EndsOn { get; set; }

    public ContractStatus Status { get; set; } = ContractStatus.Active;
    public DeliveryFrequency Frequency { get; set; } = DeliveryFrequency.Nightly;

    public TimeSpan DeliveryWindowStart { get; set; } = new(22, 0, 0);
    public TimeSpan DeliveryWindowEnd { get; set; } = new(5, 0, 0);

    public double CommittedLitresPerMonth { get; set; }
    public double MinLitresPerDelivery { get; set; } = 200;

    public decimal DiscountPerLitre { get; set; }
    public decimal FixedDeliveryFee { get; set; }
    public bool PriceLinkedToErc { get; set; } = true;

    [MaxLength(250)]
    public string DefaultAddress { get; set; } = string.Empty;
    [MaxLength(120)]
    public string DefaultCity { get; set; } = string.Empty;
    public double DefaultLatitude { get; set; }
    public double DefaultLongitude { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ContractAsset> Assets { get; set; } = new List<ContractAsset>();
    public ICollection<ContractDelivery> Deliveries { get; set; } = new List<ContractDelivery>();

    [NotMapped]
    public bool IsRunning => Status == ContractStatus.Active && DateTime.UtcNow.Date >= StartsOn.Date && DateTime.UtcNow.Date <= EndsOn.Date;

    [NotMapped]
    public int DaysRemaining => (int)Math.Max(0, (EndsOn.Date - DateTime.UtcNow.Date).TotalDays);
}

public class ContractAsset
{
    public int Id { get; set; }

    public int SupplyContractId { get; set; }
    public SupplyContract? SupplyContract { get; set; }

    public int? ConstructionSiteId { get; set; }
    public ConstructionSite? ConstructionSite { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public AssetKind Kind { get; set; } = AssetKind.Truck;

    [MaxLength(40)]
    public string? Identifier { get; set; }

    public double TankCapacityLitres { get; set; } = 400;

    public double AverageConsumptionPer100Km { get; set; }
    public double AverageLitresPerWeek { get; set; }
    public double CurrentLevelPercent { get; set; } = 55;

    [MaxLength(200)]
    public string? ParkingLocation { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public bool IsActive { get; set; } = true;

    public string KindIcon => Kind switch
    {
        AssetKind.Truck => "truck",
        AssetKind.Bus => "bus",
        AssetKind.ConstructionMachine => "excavator",
        AssetKind.Boat => "boat",
        AssetKind.Generator => "bolt",
        AssetKind.StorageTank => "tank",
        _ => "tractor"
    };
}

public class ContractDelivery
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string Reference { get; set; } = string.Empty;

    public int SupplyContractId { get; set; }
    public SupplyContract? SupplyContract { get; set; }

    public int? ContractAssetId { get; set; }
    public ContractAsset? ContractAsset { get; set; }

    public int? DeliveryVehicleId { get; set; }
    public DeliveryVehicle? DeliveryVehicle { get; set; }

    public DateTime ScheduledForUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public double PlannedLitres { get; set; }
    public double DeliveredLitres { get; set; }

    /// <summary>The pump (ERC retail) price of the fuel on the day the drop was scheduled. UnitPrice = this − contract discount.</summary>
    public decimal ReferencePricePerLitre { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public DeliveryProof? Proof { get; set; }

    [NotMapped]
    public decimal DiscountPerLitre => ReferencePricePerLitre > 0 ? Math.Max(0, ReferencePricePerLitre - UnitPrice) : 0m;
}

public class DeliveryProof
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string ProofNumber { get; set; } = string.Empty;

    public int ContractDeliveryId { get; set; }
    public ContractDelivery? ContractDelivery { get; set; }

    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

    public double LitresDelivered { get; set; }
    public double MeterStart { get; set; }
    public double MeterEnd { get; set; }

    [MaxLength(150)]
    public string DriverName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string ReceivedBy { get; set; } = string.Empty;

    /// <summary>
    /// The receiver's hand-drawn signature as a PNG data URL, captured on the driver's device.
    /// A simple electronic signature in the sense of the e-documents act — evidence of receipt,
    /// not a qualified signature. Older rows carry the placeholder "signed-on-device".
    /// </summary>
    public string SignatureData { get; set; } = string.Empty;

    [NotMapped]
    public bool HasSignatureImage => SignatureData.StartsWith("data:image/", StringComparison.Ordinal);

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    [MaxLength(64)]
    public string VerificationHash { get; set; } = string.Empty;

    [MaxLength(600)]
    public string? Remarks { get; set; }

    [NotMapped]
    public double MeterDifference => Math.Round(MeterEnd - MeterStart, 2);
}

public class Invoice
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public int BusinessAccountId { get; set; }
    public BusinessAccount? BusinessAccount { get; set; }

    public int? SupplyContractId { get; set; }
    public SupplyContract? SupplyContract { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public DateTime IssuedOn { get; set; } = DateTime.UtcNow.Date;
    public DateTime DueOn { get; set; } = DateTime.UtcNow.Date.AddDays(30);

    public decimal Subtotal { get; set; }
    public decimal VatRate { get; set; } = 18m;
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;

    public DateTime? PaidOn { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();

    [NotMapped]
    public bool IsOverdue => Status == InvoiceStatus.Issued && DueOn < DateTime.UtcNow.Date;
}

public class InvoiceLine
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    [MaxLength(250)]
    public string Description { get; set; } = string.Empty;

    public DateTime DeliveredOn { get; set; }

    public double Litres { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal { get; set; }

    [MaxLength(40)]
    public string? ProofNumber { get; set; }
}
