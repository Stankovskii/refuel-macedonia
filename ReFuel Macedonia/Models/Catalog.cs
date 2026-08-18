using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReFuel_Macedonia.Models;

public class Brand
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? NameMk { get; set; }

    [MaxLength(300)]
    public string? Website { get; set; }

    [MaxLength(9)]
    public string ColorHex { get; set; } = "#0F766E";

    [MaxLength(300)]
    public string? LogoUrl { get; set; }

    public bool IsDeliveryPartner { get; set; }

    public ICollection<Station> Stations { get; set; } = new List<Station>();

    public string DisplayName(string culture) => culture == "mk" && !string.IsNullOrWhiteSpace(NameMk) ? NameMk! : Name;
}

public class FuelType
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string NameMk { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string NameEn { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string NameSq { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Unit { get; set; } = "L";

    [MaxLength(9)]
    public string ColorHex { get; set; } = "#2563EB";

    public bool IsRegulated { get; set; } = true;
    public bool IsDeliverable { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<StationPrice> Prices { get; set; } = new List<StationPrice>();

    public string LocalizedName(string culture) => culture switch
    {
        "sq" => NameSq,
        "en" => NameEn,
        _ => NameMk
    };

    public string ShortName => Code switch
    {
        "EUROSUPER_95" => "БС-95",
        "EUROSUPER_98" => "БС-98",
        "EURODIESEL" => "Дизел",
        "LPG" => "ТНГ",
        "CNG" => "CNG",
        "EL_1" => "ЕЛ-1",
        _ => "Мазут"
    };
}

public class Station
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string? OsmId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(400)]
    public string SearchKey { get; set; } = string.Empty;

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    [MaxLength(120)]
    public string City { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Municipality { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(120)]
    public string? OpeningHours { get; set; }

    public bool IsOpen24h { get; set; }
    public bool HasLpg { get; set; }
    public bool HasCng { get; set; }
    public bool HasShop { get; set; }
    public bool HasCarWash { get; set; }
    public bool HasRestaurant { get; set; }
    public bool HasToilets { get; set; }
    public bool HasEvCharger { get; set; }
    public bool HasTruckLane { get; set; }

    public bool IsOnMotorway { get; set; }
    public bool IsVerified { get; set; }

    public StationSource Source { get; set; } = StationSource.Seed;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<StationPrice> Prices { get; set; } = new List<StationPrice>();
    public ICollection<StationReview> Reviews { get; set; } = new List<StationReview>();

    [NotMapped]
    public double AverageRating { get; set; }

    [NotMapped]
    public double DistanceKm { get; set; }

    [NotMapped]
    public string DisplayName => Brand is null || Name.Contains(Brand.Name, StringComparison.OrdinalIgnoreCase)
        ? Name
        : $"{Brand.Name} - {Name}";
}

public class StationReview
{
    public int Id { get; set; }

    public int StationId { get; set; }
    public Station? Station { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(800)]
    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class FavoriteStation
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int StationId { get; set; }
    public Station? Station { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
