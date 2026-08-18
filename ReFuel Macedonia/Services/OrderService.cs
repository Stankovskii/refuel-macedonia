using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;
using ReFuel_Macedonia.Services.Ai;

namespace ReFuel_Macedonia.Services;

public interface IOrderService
{
    Task<OrderQuoteVm> QuoteAsync(CreateOrderVm model, string? userId);
    Task<(bool Ok, string MessageKey, Order? Order)> PlaceAsync(CreateOrderVm model, string userId);
    Task<Order?> GetAsync(int id, string? userId, bool isAdmin = false);
    Task<OrderListVm> ListAsync(string userId, OrderStatus? status, int page = 1, int pageSize = 10);
    Task<bool> CancelAsync(int id, string userId);
    Task<bool> AdvanceStatusAsync(int id, OrderStatus status, string? note = null);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IPriceService _prices;
    private readonly IPromotionService _promotions;
    private readonly IRecommendationService _recommendations;
    private readonly INotificationService _notifications;
    private readonly ITrackingService _tracking;
    private readonly IPricingService _pricing;
    private readonly ReFuelOptions _options;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        ApplicationDbContext db,
        IPriceService prices,
        IPromotionService promotions,
        IRecommendationService recommendations,
        INotificationService notifications,
        ITrackingService tracking,
        IPricingService pricing,
        IOptions<ReFuelOptions> options,
        ILogger<OrderService> logger)
    {
        _db = db;
        _prices = prices;
        _promotions = promotions;
        _recommendations = recommendations;
        _notifications = notifications;
        _tracking = tracking;
        _pricing = pricing;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderQuoteVm> QuoteAsync(CreateOrderVm model, string? userId)
    {
        var quote = new OrderQuoteVm();

        var litres = Math.Clamp(model.Litres, _options.MinLitres, _options.MaxLitres);
        if (Math.Abs(litres - model.Litres) > 0.001)
            quote.Warnings.Add("order.warn.litresclamped");
        model.Litres = litres;

        Station? station = null;

        if (model.StationId is > 0)
            station = await _db.Stations.Include(s => s.Brand).FirstOrDefaultAsync(s => s.Id == model.StationId);

        if (station is null)
        {
            var reco = await _recommendations.RecommendAsync(
                userId, model.DeliveryLatitude, model.DeliveryLongitude, model.FuelTypeId, litres, null, 1);

            var bestId = reco.Items.FirstOrDefault()?.Station.Id;
            if (bestId is > 0)
                station = await _db.Stations.Include(s => s.Brand).FirstOrDefaultAsync(s => s.Id == bestId);
        }

        decimal baseCost = 0;
        if (station is not null)
            baseCost = await _prices.GetStationPriceAsync(station.Id, model.FuelTypeId) ?? 0;

        if (baseCost <= 0)
        {
            baseCost = await _prices.GetCurrentNationalPriceAsync(model.FuelTypeId) ?? 0;
            if (baseCost > 0) quote.Warnings.Add("order.warn.nationalprice");
        }

        if (baseCost <= 0)
        {
            quote.Warnings.Add("order.warn.noprice");
            return quote;
        }

        quote.StationId = station?.Id;
        quote.StationName = station?.DisplayName;

        var distance = station is null
            ? 8
            : GeoUtils.RoadDistanceKm(station.Latitude, station.Longitude, model.DeliveryLatitude, model.DeliveryLongitude);

        quote.DistanceKm = Math.Round(distance, 2);

        var user = string.IsNullOrEmpty(userId)
            ? null
            : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        var monthlyLitres = await _pricing.GetMonthlyLitresAsync(userId, user?.BusinessAccountId);

        var request = new PricingRequest
        {
            CustomerType = user?.CustomerType ?? CustomerType.Individual,
            PumpPricePerLitre = baseCost,
            BaseCostPerLitre = baseCost,
            Litres = litres,
            DistanceKm = distance,
            OrderType = model.Type,
            IsEmergency = model.Type == OrderType.Emergency,
            DedicatedTrip = model.Type == OrderType.Contract,
            MonthlyLitres = monthlyLitres,
            Latitude = model.DeliveryLatitude,
            Longitude = model.DeliveryLongitude,
            City = model.DeliveryCity
        };

        if (user?.BusinessAccountId is int accountId)
        {
            var account = await _db.BusinessAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
            if (account is not null)
            {
                request.PricingTierId = account.PricingTierId;
                request.NegotiatedMarginPerLitre = account.NegotiatedMarginPerLitre;
            }
        }

        var breakdown = await _pricing.QuoteAsync(request);

        var travel = GeoUtils.TravelMinutes(distance);
        quote.EtaMinutes = Math.Round(model.Type == OrderType.Emergency ? travel + 15 : travel + 40, 0);

        var when = model.Type == OrderType.Scheduled && model.ScheduledFor.HasValue
            ? Fmt.ToUtc(model.ScheduledFor.Value)
            : DateTime.UtcNow;

        // The most a promotion may give away on this order: the delivery charges plus whatever
        // margin sits above the tier's floor. The fuel itself is never discounted below that.
        var promotionCeiling = Math.Max(0,
            breakdown.DeliveryFee + breakdown.EmergencySurcharge
            + Math.Max(0, breakdown.MarginPerLitre - breakdown.MinMarginPerLitre - breakdown.VolumeDiscountPerLitre - breakdown.ContractDiscountPerLitre) * (decimal)breakdown.Litres);

        var evaluation = string.IsNullOrWhiteSpace(model.PromotionCode)
            ? await _promotions.FindBestAsync(userId, model.FuelTypeId, litres, breakdown.FuelRevenue, breakdown.DeliveryFee, model.Type, model.DeliveryCity, when, promotionCeiling)
            : await _promotions.EvaluateAsync(model.PromotionCode!, userId, model.FuelTypeId, litres, breakdown.FuelRevenue, breakdown.DeliveryFee, model.Type, model.DeliveryCity, when, promotionCeiling);

        quote.PromotionApplied = evaluation.Eligible;
        quote.PromotionMessage = evaluation.ReasonKey;

        if (evaluation.Eligible && evaluation.Promotion is not null && evaluation.DiscountAmount > 0)
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            quote.PromotionId = evaluation.Promotion.Id;
            quote.PromotionTitle = evaluation.Promotion.Title(culture switch { "sq" => "sq", "en" => "en", _ => "mk" });

            request.PromotionDiscount = evaluation.DiscountAmount;
            breakdown = await _pricing.QuoteAsync(request);
        }

        quote.Breakdown = breakdown;
        quote.UnitPrice = breakdown.EffectivePricePerLitre;
        quote.FuelSubtotal = breakdown.FuelRevenue;
        quote.DeliveryFee = breakdown.DeliveryFee;
        quote.EmergencySurcharge = breakdown.EmergencySurcharge;
        quote.DiscountAmount = breakdown.PromotionDiscount;
        quote.Total = breakdown.Total;

        foreach (var warning in breakdown.Warnings)
            quote.Warnings.Add(warning);

        quote.PointsEarned = (int)(quote.Total / 100m) * _options.PointsPer100Mkd;

        return quote;
    }

    public async Task<(bool Ok, string MessageKey, Order? Order)> PlaceAsync(CreateOrderVm model, string userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return (false, "order.error.user", null);

        var fuel = await _db.FuelTypes.FirstOrDefaultAsync(f => f.Id == model.FuelTypeId);
        if (fuel is null || !fuel.IsDeliverable) return (false, "order.error.fuel", null);

        if (!GeoUtils.IsInsideCountry(model.DeliveryLatitude, model.DeliveryLongitude))
            return (false, "order.error.outside", null);

        var quote = await QuoteAsync(model, userId);
        if (quote.UnitPrice <= 0) return (false, "order.error.price", null);

        // The scheduled discount exists because a window lets the dispatcher group the drop; no window, no discount.
        if (model.Type == OrderType.Scheduled && (!model.ScheduledFor.HasValue || Fmt.ToUtc(model.ScheduledFor.Value) <= DateTime.UtcNow))
            return (false, "order.error.scheduledfor", null);

        var open = await _db.Orders.CountAsync(o => o.UserId == userId &&
            (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Assigned));
        if (open >= 5) return (false, "order.error.toomany", null);

        var order = new Order
        {
            Reference = await NextReferenceAsync(),
            UserId = userId,
            StationId = quote.StationId,
            FuelTypeId = model.FuelTypeId,
            VehicleId = model.VehicleId,
            Litres = model.Litres,
            UnitPrice = quote.UnitPrice,
            FuelSubtotal = quote.FuelSubtotal,
            DeliveryFee = quote.DeliveryFee,
            EmergencySurcharge = quote.EmergencySurcharge,
            DiscountAmount = quote.DiscountAmount,
            Total = quote.Total,
            Type = model.Type,
            Status = OrderStatus.Pending,
            PaymentMethod = model.PaymentMethod,
            DeliveryAddress = model.DeliveryAddress,
            DeliveryCity = model.DeliveryCity,
            DeliveryLatitude = model.DeliveryLatitude,
            DeliveryLongitude = model.DeliveryLongitude,
            ContactPhone = model.ContactPhone ?? user.PhoneNumber,
            Notes = model.Notes,
            DistanceKm = quote.DistanceKm,
            EtaUtc = DateTime.UtcNow.AddMinutes(quote.EtaMinutes),
            ScheduledForUtc = model.Type == OrderType.Scheduled && model.ScheduledFor.HasValue ? Fmt.ToUtc(model.ScheduledFor.Value) : null,
            PointsEarned = quote.PointsEarned,
            BusinessAccountId = user.BusinessAccountId,
            Priority = model.Type switch
            {
                OrderType.Emergency => DeliveryPriority.Emergency,
                OrderType.Scheduled => DeliveryPriority.Scheduled,
                OrderType.Contract => DeliveryPriority.ContractFleet,
                _ => user.CustomerType == CustomerType.Individual ? DeliveryPriority.Standard : DeliveryPriority.ContractFleet
            }
        };

        if (quote.Breakdown is PriceBreakdown breakdown)
        {
            order.PricingTierId = breakdown.PricingTierId is > 0 ? breakdown.PricingTierId : null;
            order.DeliveryZoneId = breakdown.DeliveryZoneId;
            order.PumpPricePerLitre = breakdown.PumpPricePerLitre;
            order.BaseCostPerLitre = breakdown.BaseCostPerLitre;
            order.MarginPerLitre = breakdown.MarginPerLitre;
            order.VolumeDiscountPerLitre = breakdown.VolumeDiscountPerLitre;
            order.ContractDiscountPerLitre = breakdown.ContractDiscountPerLitre;
            order.DeliveryCost = breakdown.DeliveryCost;
            order.GrossFuelMargin = breakdown.GrossFuelMargin;
            order.GrossContribution = breakdown.GrossContribution;
        }

        var driver = await _tracking.AssignDriverAsync(order);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = OrderStatus.Pending,
            Note = "order.history.created"
        });

        if (driver is not null)
        {
            order.Status = OrderStatus.Confirmed;
            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                Status = OrderStatus.Confirmed,
                Note = "order.history.assigned"
            });
        }

        if (quote.DiscountAmount > 0 && quote.PromotionId is int promotionId)
        {
            order.PromotionId = promotionId;
            await _promotions.RedeemAsync(promotionId, userId, order.Id, quote.DiscountAmount);
        }

        user.LoyaltyPoints += quote.PointsEarned;
        user.Tier = TierFor(user.LoyaltyPoints);

        await _db.SaveChangesAsync();

        await _notifications.PushAsync(userId, "notif.order.created.title", "notif.order.created.body",
            NotificationKind.OrderUpdate, $"/Orders/Details/{order.Id}", order.Reference);

        _logger.LogInformation("Order {Reference} placed by {User} for {Litres} L.", order.Reference, userId, order.Litres);
        return (true, "order.ok", order);
    }

    private static LoyaltyTier TierFor(int points) => points switch
    {
        >= 4000 => LoyaltyTier.Platinum,
        >= 2000 => LoyaltyTier.Gold,
        >= 700 => LoyaltyTier.Silver,
        _ => LoyaltyTier.Basic
    };

    private async Task<string> NextReferenceAsync()
    {
        var year = DateTime.UtcNow.Year;
        var count = await _db.Orders.CountAsync();
        string reference;
        var attempt = 0;

        do
        {
            reference = $"RF-{year}-{count + 1 + attempt:D6}";
            attempt++;
        }
        while (await _db.Orders.AnyAsync(o => o.Reference == reference) && attempt < 50);

        return reference;
    }

    public async Task<Order?> GetAsync(int id, string? userId, bool isAdmin = false)
    {
        var order = await _db.Orders
            .Include(o => o.FuelType)
            .Include(o => o.Station).ThenInclude(s => s!.Brand)
            .Include(o => o.Vehicle)
            .Include(o => o.Promotion)
            .Include(o => o.User)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return null;
        if (!isAdmin && order.UserId != userId) return null;

        order.StatusHistory = order.StatusHistory.OrderBy(h => h.CreatedAtUtc).ToList();
        return order;
    }

    public async Task<OrderListVm> ListAsync(string userId, OrderStatus? status, int page = 1, int pageSize = 10)
    {
        var query = _db.Orders
            .Include(o => o.FuelType)
            .Include(o => o.Station)
            .Where(o => o.UserId == userId);

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);

        var total = await query.CountAsync();
        if (page < 1) page = 1;

        var orders = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var all = await _db.Orders
            .Where(o => o.UserId == userId && o.Status == OrderStatus.Delivered)
            .Select(o => new { o.Total, o.Litres })
            .ToListAsync();

        return new OrderListVm
        {
            Orders = orders,
            Status = status,
            Page = page,
            TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)),
            TotalSpent = all.Count == 0 ? 0 : all.Sum(o => o.Total),
            TotalLitres = all.Count == 0 ? 0 : Math.Round(all.Sum(o => o.Litres), 1),
            DeliveredCount = all.Count
        };
    }

    public async Task<bool> CancelAsync(int id, string userId)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null) return false;
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled) return false;
        if (order.Status == OrderStatus.OnTheWay) return false;

        order.Status = OrderStatus.Cancelled;
        order.CancelledAtUtc = DateTime.UtcNow;

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = OrderStatus.Cancelled,
            Note = "order.history.cancelled"
        });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AdvanceStatusAsync(int id, OrderStatus status, string? note = null)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return false;

        order.Status = status;
        if (status == OrderStatus.Delivered) order.DeliveredAtUtc = DateTime.UtcNow;
        if (status == OrderStatus.Cancelled) order.CancelledAtUtc = DateTime.UtcNow;

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = status,
            Note = note
        });

        await _db.SaveChangesAsync();

        await _notifications.PushAsync(order.UserId, "notif.order.status.title", "notif.order.status.body",
            NotificationKind.OrderUpdate, $"/Orders/Details/{order.Id}", order.Reference);

        return true;
    }
}
