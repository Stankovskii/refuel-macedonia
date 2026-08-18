using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ReFuel_Macedonia.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(5)]
    public string PreferredLanguage { get; set; } = "mk";

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(250)]
    public string? DefaultAddress { get; set; }

    public double? HomeLatitude { get; set; }
    public double? HomeLongitude { get; set; }

    public int? PreferredFuelTypeId { get; set; }
    public FuelType? PreferredFuelType { get; set; }

    public double MaxDetourKm { get; set; } = 10;

    public double DistanceWeight { get; set; } = 0.4;

    public int LoyaltyPoints { get; set; }
    public LoyaltyTier Tier { get; set; } = LoyaltyTier.Basic;

    public CustomerType CustomerType { get; set; } = CustomerType.Individual;

    public int? BusinessAccountId { get; set; }
    public BusinessAccount? BusinessAccount { get; set; }

    public bool NotifyPriceDrops { get; set; } = true;
    public bool NotifyPromotions { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }

    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<FavoriteStation> Favorites { get; set; } = new List<FavoriteStation>();
}

public class Vehicle
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(60)]
    public string Make { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string Model { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PlateNumber { get; set; }

    public int? Year { get; set; }

    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    [Range(10, 200)]
    public double TankCapacityLitres { get; set; } = 50;

    [Range(1, 40)]
    public double AvgConsumption { get; set; } = 7;

    public bool IsDefault { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string DisplayName => $"{Make} {Model}" + (string.IsNullOrWhiteSpace(PlateNumber) ? "" : $" ({PlateNumber})");
}

public class Notification
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(600)]
    public string Body { get; set; } = string.Empty;

    public NotificationKind Kind { get; set; } = NotificationKind.Info;

    [MaxLength(250)]
    public string? Url { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
