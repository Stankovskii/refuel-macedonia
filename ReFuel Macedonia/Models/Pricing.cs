using System.ComponentModel.DataAnnotations;

namespace ReFuel_Macedonia.Models;

public class NationalPrice
{
    public int Id { get; set; }

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public decimal Price { get; set; }

    public decimal Change { get; set; }

    [MaxLength(200)]
    public string? DecisionReference { get; set; }

    public PriceSource Source { get; set; } = PriceSource.RegulatoryCommission;

    public bool IsOfficial { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class StationPrice
{
    public int Id { get; set; }

    public int StationId { get; set; }
    public Station? Station { get; set; }

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    public decimal Price { get; set; }

    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;

    public PriceSource Source { get; set; } = PriceSource.Estimated;

    public double Confidence { get; set; } = 0.5;

    public bool IsCurrent { get; set; } = true;

    public string? ReportedByUserId { get; set; }
    public ApplicationUser? ReportedByUser { get; set; }
}

public class PriceReport
{
    public int Id { get; set; }

    public int StationId { get; set; }
    public Station? Station { get; set; }

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Range(1, 500)]
    public decimal Price { get; set; }

    [MaxLength(400)]
    public string? Note { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Pending;

    public double PlausibilityScore { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByUserId { get; set; }
}

public class PricePrediction
{
    public int Id { get; set; }

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    public DateTime TargetDate { get; set; }

    public decimal PredictedPrice { get; set; }

    public decimal LowerBound { get; set; }
    public decimal UpperBound { get; set; }

    [MaxLength(120)]
    public string Model { get; set; } = string.Empty;

    public double Confidence { get; set; }

    [MaxLength(10)]
    public string Trend { get; set; } = "flat";

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
