using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services;

public record PromotionEvaluation(
    bool Eligible,
    string ReasonKey,
    decimal DiscountAmount,
    Promotion? Promotion,
    DateTime? AvailableFrom = null);

public interface IPromotionService
{
    Task<PromotionsPageVm> GetPageAsync(string? userId);
    Task<List<Promotion>> GetLivePromotionsAsync(int take = 6);

    /// <param name="maxDiscount">Hard ceiling from the pricing engine: the delivery charges plus the margin room. A promotion can never take more.</param>
    Task<PromotionEvaluation> EvaluateAsync(
        string code, string? userId, int fuelTypeId, double litres,
        decimal fuelSubtotal, decimal deliveryFee, OrderType type, string city, DateTime whenUtc, decimal? maxDiscount = null);

    Task<PromotionEvaluation> FindBestAsync(
        string? userId, int fuelTypeId, double litres,
        decimal fuelSubtotal, decimal deliveryFee, OrderType type, string city, DateTime whenUtc, decimal? maxDiscount = null);

    Task RedeemAsync(int promotionId, string userId, int orderId, decimal amount);
}

public class PromotionService : IPromotionService
{
    private readonly ApplicationDbContext _db;

    public PromotionService(ApplicationDbContext db) => _db = db;

    public async Task<List<Promotion>> GetLivePromotionsAsync(int take = 6)
    {
        var now = DateTime.UtcNow;
        return await _db.Promotions
            .Where(p => p.IsActive && p.StartsAtUtc <= now && p.EndsAtUtc >= now)
            .OrderByDescending(p => p.Value)
            .Take(take)
            .ToListAsync();
    }

    public async Task<PromotionsPageVm> GetPageAsync(string? userId)
    {
        var now = DateTime.UtcNow;
        var vm = new PromotionsPageVm { IsAuthenticated = !string.IsNullOrEmpty(userId) };

        ApplicationUser? user = null;
        if (!string.IsNullOrEmpty(userId))
        {
            user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            vm.Tier = user?.Tier ?? LoyaltyTier.Basic;
            vm.Points = user?.LoyaltyPoints ?? 0;
        }

        var promotions = await _db.Promotions
            .Include(p => p.FuelType)
            .Where(p => p.IsActive && p.EndsAtUtc >= now)
            .OrderBy(p => p.EndsAtUtc)
            .ToListAsync();

        foreach (var promo in promotions)
        {
            var card = new PromotionCardVm
            {
                Promotion = promo,
                Days = promo.ActiveDays().ToList()
            };

            if (user is null)
            {
                card.IsEligible = false;
                card.ReasonKey = "promo.reason.login";
            }
            else
            {
                var redemptions = await _db.PromotionRedemptions
                    .Where(r => r.PromotionId == promo.Id && r.UserId == user.Id)
                    .OrderByDescending(r => r.RedeemedAtUtc)
                    .ToListAsync();

                var check = CheckAvailability(promo, user, redemptions, now, null, null);
                card.IsEligible = check.Eligible;
                card.ReasonKey = check.ReasonKey;
                card.AvailableFrom = check.AvailableFrom;
                card.UsesLeft = promo.MaxUsesPerUser == 0 ? int.MaxValue : Math.Max(0, promo.MaxUsesPerUser - redemptions.Count);
            }

            vm.Cards.Add(card);
        }

        vm.Cards = vm.Cards.OrderByDescending(c => c.IsEligible).ThenBy(c => c.Promotion.EndsAtUtc).ToList();
        return vm;
    }

    public async Task<PromotionEvaluation> EvaluateAsync(
        string code, string? userId, int fuelTypeId, double litres,
        decimal fuelSubtotal, decimal deliveryFee, OrderType type, string city, DateTime whenUtc, decimal? maxDiscount = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new PromotionEvaluation(false, "promo.reason.nocode", 0, null);

        var promo = await _db.Promotions
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == code.Trim().ToUpper());

        if (promo is null)
            return new PromotionEvaluation(false, "promo.reason.notfound", 0, null);

        return await EvaluateCoreAsync(promo, userId, fuelTypeId, litres, fuelSubtotal, deliveryFee, type, city, whenUtc, maxDiscount);
    }

    public async Task<PromotionEvaluation> FindBestAsync(
        string? userId, int fuelTypeId, double litres,
        decimal fuelSubtotal, decimal deliveryFee, OrderType type, string city, DateTime whenUtc, decimal? maxDiscount = null)
    {
        var promos = await _db.Promotions
            .Where(p => p.IsActive && p.StartsAtUtc <= whenUtc && p.EndsAtUtc >= whenUtc)
            .ToListAsync();

        PromotionEvaluation? best = null;

        foreach (var promo in promos)
        {
            var evaluation = await EvaluateCoreAsync(promo, userId, fuelTypeId, litres, fuelSubtotal, deliveryFee, type, city, whenUtc, maxDiscount);
            if (!evaluation.Eligible) continue;
            if (best is null || evaluation.DiscountAmount > best.DiscountAmount) best = evaluation;
        }

        return best ?? new PromotionEvaluation(false, "promo.reason.none", 0, null);
    }

    private async Task<PromotionEvaluation> EvaluateCoreAsync(
        Promotion promo, string? userId, int fuelTypeId, double litres,
        decimal fuelSubtotal, decimal deliveryFee, OrderType type, string city, DateTime whenUtc, decimal? maxDiscount)
    {
        if (string.IsNullOrEmpty(userId))
            return new PromotionEvaluation(false, "promo.reason.login", 0, promo);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return new PromotionEvaluation(false, "promo.reason.login", 0, promo);

        var redemptions = await _db.PromotionRedemptions
            .Where(r => r.PromotionId == promo.Id && r.UserId == userId)
            .OrderByDescending(r => r.RedeemedAtUtc)
            .ToListAsync();

        var availability = CheckAvailability(promo, user, redemptions, whenUtc, type, city);
        if (!availability.Eligible) return availability with { Promotion = promo };

        if (promo.FuelTypeId is > 0 && promo.FuelTypeId != fuelTypeId)
            return new PromotionEvaluation(false, "promo.reason.fuel", 0, promo);

        if (promo.MinLitres > 0 && litres < promo.MinLitres)
            return new PromotionEvaluation(false, "promo.reason.minlitres", 0, promo);

        if (promo.MinOrderTotal > 0 && fuelSubtotal + deliveryFee < promo.MinOrderTotal)
            return new PromotionEvaluation(false, "promo.reason.mintotal", 0, promo);

        if (promo.NewCustomersOnly)
        {
            var hasOrders = await _db.Orders.AnyAsync(o => o.UserId == userId && o.Status != OrderStatus.Cancelled);
            if (hasOrders) return new PromotionEvaluation(false, "promo.reason.newonly", 0, promo);
        }

        var discount = promo.DiscountType switch
        {
            DiscountType.Percentage => Math.Round(fuelSubtotal * promo.Value / 100m, 0),
            DiscountType.FixedAmount => promo.Value,
            DiscountType.PerLitre => Math.Round(promo.Value * (decimal)litres, 0),
            DiscountType.FreeDelivery => deliveryFee,
            _ => 0m
        };

        if (promo.MaxDiscountAmount > 0 && discount > promo.MaxDiscountAmount)
            discount = promo.MaxDiscountAmount;

        // A promotion may give away the delivery charges and the room above the tier's minimum
        // margin — never the fuel itself. The pricing engine passes that ceiling in.
        var ceiling = fuelSubtotal + deliveryFee;
        if (maxDiscount.HasValue && maxDiscount.Value < ceiling) ceiling = Math.Max(0, maxDiscount.Value);
        if (discount > ceiling) discount = ceiling;

        if (discount <= 0)
            return new PromotionEvaluation(false, "promo.reason.nodiscount", 0, promo);

        return new PromotionEvaluation(true, "promo.reason.ok", discount, promo);
    }

    private static PromotionEvaluation CheckAvailability(
        Promotion promo, ApplicationUser user, List<PromotionRedemption> redemptions,
        DateTime whenUtc, OrderType? type, string? city)
    {
        if (!promo.IsActive)
            return new PromotionEvaluation(false, "promo.reason.inactive", 0, promo);

        if (whenUtc < promo.StartsAtUtc)
            return new PromotionEvaluation(false, "promo.reason.notstarted", 0, promo, promo.StartsAtUtc);

        if (whenUtc > promo.EndsAtUtc)
            return new PromotionEvaluation(false, "promo.reason.expired", 0, promo);

        if (promo.MaxTotalUses > 0 && promo.TimesUsed >= promo.MaxTotalUses)
            return new PromotionEvaluation(false, "promo.reason.exhausted", 0, promo);

        if (user.Tier < promo.RequiredTier)
            return new PromotionEvaluation(false, "promo.reason.tier", 0, promo);

        var local = Infrastructure.Fmt.ToLocal(whenUtc);
        var dayBit = 1 << (int)local.DayOfWeek;
        if ((promo.DayOfWeekMask & dayBit) == 0)
        {
            var next = NextAvailableDay(promo, local);
            return new PromotionEvaluation(false, "promo.reason.day", 0, promo, next);
        }

        if (promo.MaxUsesPerUser > 0 && redemptions.Count >= promo.MaxUsesPerUser)
            return new PromotionEvaluation(false, "promo.reason.used", 0, promo);

        if (promo.CooldownDays > 0 && redemptions.Count > 0)
        {
            var last = redemptions[0].RedeemedAtUtc;
            var next = last.AddDays(promo.CooldownDays);
            if (whenUtc < next)
                return new PromotionEvaluation(false, "promo.reason.cooldown", 0, promo, next);
        }

        if (type is not null)
        {
            if (promo.EmergencyOnly && type != OrderType.Emergency)
                return new PromotionEvaluation(false, "promo.reason.emergencyonly", 0, promo);
        }

        if (!string.IsNullOrWhiteSpace(promo.City) && !string.IsNullOrWhiteSpace(city)
            && !string.Equals(promo.City, city, StringComparison.OrdinalIgnoreCase))
            return new PromotionEvaluation(false, "promo.reason.city", 0, promo);

        return new PromotionEvaluation(true, "promo.reason.ok", 0, promo);
    }

    private static DateTime NextAvailableDay(Promotion promo, DateTime localNow)
    {
        for (var i = 1; i <= 7; i++)
        {
            var candidate = localNow.Date.AddDays(i);
            if ((promo.DayOfWeekMask & (1 << (int)candidate.DayOfWeek)) != 0)
                return candidate;
        }
        return localNow.Date.AddDays(1);
    }

    public async Task RedeemAsync(int promotionId, string userId, int orderId, decimal amount)
    {
        var promo = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == promotionId);
        if (promo is null) return;

        promo.TimesUsed++;

        _db.PromotionRedemptions.Add(new PromotionRedemption
        {
            PromotionId = promotionId,
            UserId = userId,
            OrderId = orderId,
            DiscountAmount = amount,
            RedeemedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}
