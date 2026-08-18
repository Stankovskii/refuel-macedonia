using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReFuel_Macedonia.Models;

public class Promotion
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string TitleMk { get; set; } = string.Empty;
    [Required, MaxLength(160)]
    public string TitleEn { get; set; } = string.Empty;
    [Required, MaxLength(160)]
    public string TitleSq { get; set; } = string.Empty;

    [MaxLength(600)]
    public string DescriptionMk { get; set; } = string.Empty;
    [MaxLength(600)]
    public string DescriptionEn { get; set; } = string.Empty;
    [MaxLength(600)]
    public string DescriptionSq { get; set; } = string.Empty;

    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;

    public decimal Value { get; set; }

    public decimal MaxDiscountAmount { get; set; }

    public double MinLitres { get; set; }

    public decimal MinOrderTotal { get; set; }

    public DateTime StartsAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime EndsAtUtc { get; set; } = DateTime.UtcNow.AddMonths(3);

    public int DayOfWeekMask { get; set; } = 127;

    public int CooldownDays { get; set; } = 7;

    public int MaxUsesPerUser { get; set; } = 1;

    public int MaxTotalUses { get; set; }

    public int TimesUsed { get; set; }

    public LoyaltyTier RequiredTier { get; set; } = LoyaltyTier.Basic;

    public int? FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }

    [MaxLength(120)]
    public string? City { get; set; }

    public bool NewCustomersOnly { get; set; }

    public bool EmergencyOnly { get; set; }

    [MaxLength(9)]
    public string ColorHex { get; set; } = "#0F766E";

    [MaxLength(40)]
    public string Icon { get; set; } = "tag";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PromotionRedemption> Redemptions { get; set; } = new List<PromotionRedemption>();

    [NotMapped]
    public bool IsLive => IsActive && DateTime.UtcNow >= StartsAtUtc && DateTime.UtcNow <= EndsAtUtc;

    public string Title(string culture) => culture switch
    {
        "sq" => TitleSq,
        "en" => TitleEn,
        _ => TitleMk
    };

    public string Description(string culture) => culture switch
    {
        "sq" => DescriptionSq,
        "en" => DescriptionEn,
        _ => DescriptionMk
    };

    public IEnumerable<DayOfWeek> ActiveDays()
    {
        for (var i = 0; i < 7; i++)
        {
            if ((DayOfWeekMask & (1 << i)) != 0)
                yield return (DayOfWeek)i;
        }
    }
}

public class PromotionRedemption
{
    public int Id { get; set; }

    public int PromotionId { get; set; }
    public Promotion? Promotion { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public decimal DiscountAmount { get; set; }

    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;
}
