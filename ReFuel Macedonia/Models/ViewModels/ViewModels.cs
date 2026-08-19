using System.ComponentModel.DataAnnotations;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Models.ViewModels;

public class NationalPriceVm
{
    public int FuelTypeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "L";
    public string Color { get; set; } = "#2563EB";
    public bool IsRegulated { get; set; }

    public decimal Price { get; set; }
    public decimal PreviousPrice { get; set; }
    public decimal Change { get; set; }
    public DateTime EffectiveFrom { get; set; }

    public decimal MarketMin { get; set; }
    public decimal MarketAvg { get; set; }
    public decimal MarketMax { get; set; }
    public int StationCount { get; set; }

    public string? CheapestStationName { get; set; }
    public int? CheapestStationId { get; set; }
    public string? CheapestStationCity { get; set; }

    public string? DecisionReference { get; set; }

    public decimal PredictedNext { get; set; }
    public string PredictedTrend { get; set; } = "flat";
    public double PredictionConfidence { get; set; }

    public string Trend => Change > 0.001m ? "up" : Change < -0.001m ? "down" : "flat";
    public double ChangePercent => PreviousPrice == 0 ? 0 : (double)((Change / PreviousPrice) * 100m);
}

public class PriceHistoryPointVm
{
    public DateTime Date { get; set; }
    public decimal Price { get; set; }
    public decimal Change { get; set; }
    public bool IsOfficial { get; set; }
    public string? Reference { get; set; }
}

public class PriceSeriesVm
{
    public int FuelTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#2563EB";
    public List<PriceHistoryPointVm> Points { get; set; } = new();
    public List<PriceHistoryPointVm> Forecast { get; set; } = new();

    public decimal Min => Points.Count == 0 ? 0 : Points.Min(p => p.Price);
    public decimal Max => Points.Count == 0 ? 0 : Points.Max(p => p.Price);
    public decimal Avg => Points.Count == 0 ? 0 : Math.Round(Points.Average(p => p.Price), 2);
}

public class PricesPageVm
{
    public List<NationalPriceVm> Board { get; set; } = new();
    public List<PriceSeriesVm> Series { get; set; } = new();
    public List<BrandPriceVm> ByBrand { get; set; } = new();
    public List<CityPriceVm> ByCity { get; set; } = new();
    public int SelectedFuelId { get; set; }
    public DateTime LastUpdateUtc { get; set; }
}

public class BrandPriceVm
{
    public int BrandId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Color { get; set; } = "#64748B";
    public decimal AveragePrice { get; set; }
    public decimal MinPrice { get; set; }
    public int StationCount { get; set; }
}

public class CityPriceVm
{
    public string City { get; set; } = string.Empty;
    public decimal AveragePrice { get; set; }
    public decimal MinPrice { get; set; }
    public int StationCount { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class StationListItemVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SearchKey { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string BrandColor { get; set; } = "#64748B";
    public string City { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsOpen24h { get; set; }
    public bool HasLpg { get; set; }
    public bool HasShop { get; set; }
    public bool HasCarWash { get; set; }
    public bool HasEvCharger { get; set; }
    public bool IsVerified { get; set; }
    public double DistanceKm { get; set; }
    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public decimal? Price { get; set; }
    public DateTime? PriceObservedAtUtc { get; set; }
    public string? PriceSource { get; set; }
}

public class StationSearchVm
{
    public string? Query { get; set; }
    public string? City { get; set; }
    public int? BrandId { get; set; }
    public int? FuelTypeId { get; set; }
    public bool Open24h { get; set; }
    public bool WithLpg { get; set; }
    public bool WithShop { get; set; }
    public bool WithEv { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double RadiusKm { get; set; } = 15;
    public string Sort { get; set; } = "price";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public List<StationListItemVm> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public List<Brand> Brands { get; set; } = new();
    public List<FuelType> Fuels { get; set; } = new();
    public List<string> Cities { get; set; } = new();
}

public class StationDetailsVm
{
    public Station Station { get; set; } = new();
    public List<StationFuelPriceVm> Prices { get; set; } = new();
    public List<PriceHistoryPointVm> History { get; set; } = new();
    public int HistoryFuelTypeId { get; set; }
    public List<StationReview> Reviews { get; set; } = new();
    public List<StationListItemVm> Nearby { get; set; } = new();
    public bool IsFavorite { get; set; }
    public double AverageRating { get; set; }
    public decimal CheapestNearbyPrice { get; set; }
    public string? CheapestNearbyName { get; set; }
    public decimal PotentialSavingPerTank { get; set; }
}

public class StationFuelPriceVm
{
    public int FuelTypeId { get; set; }
    public string Fuel { get; set; } = string.Empty;
    public string Unit { get; set; } = "L";
    public string Color { get; set; } = "#2563EB";
    public decimal Price { get; set; }
    public decimal NationalMax { get; set; }
    public decimal DiffToNational { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public PriceSource Source { get; set; }
    public double Confidence { get; set; }
}

public class MapStationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string Color { get; set; } = "#64748B";
    public string City { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public decimal? Price { get; set; }
    public string? Fuel { get; set; }
    public bool Open24h { get; set; }
    public bool Lpg { get; set; }
    public bool Verified { get; set; }
}

public class CreateOrderVm
{
    [Required]
    public int FuelTypeId { get; set; }

    [Range(10, 120, ErrorMessage = "order.error.litres")]
    public double Litres { get; set; } = 40;

    public int? StationId { get; set; }
    public int? VehicleId { get; set; }

    public OrderType Type { get; set; } = OrderType.Standard;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    [Required(ErrorMessage = "order.error.address"), MaxLength(250)]
    public string DeliveryAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "order.error.city"), MaxLength(120)]
    public string DeliveryCity { get; set; } = string.Empty;

    /// <summary>0 / 0 means "not picked yet" — the customer must place the pin or use their location.</summary>
    public double DeliveryLatitude { get; set; }
    public double DeliveryLongitude { get; set; }

    [Required(ErrorMessage = "order.error.phone"), MaxLength(50)]
    [RegularExpression(@"^\+?[0-9 ()\-/]{7,20}$", ErrorMessage = "order.error.phone")]
    public string? ContactPhone { get; set; }

    public bool HasLocation => DeliveryLatitude != 0 && DeliveryLongitude != 0;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime? ScheduledFor { get; set; }

    [MaxLength(40)]
    public string? PromotionCode { get; set; }

    public List<FuelType> Fuels { get; set; } = new();
    public List<Vehicle> Vehicles { get; set; } = new();
    public List<StationListItemVm> SuggestedStations { get; set; } = new();
    public List<Promotion> AvailablePromotions { get; set; } = new();
    public OrderQuoteVm? Quote { get; set; }
}

public class OrderQuoteVm
{
    public decimal UnitPrice { get; set; }
    public decimal FuelSubtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal EmergencySurcharge { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }
    public double DistanceKm { get; set; }
    public double EtaMinutes { get; set; }
    public int PointsEarned { get; set; }
    public int? StationId { get; set; }
    public string? StationName { get; set; }
    public int? PromotionId { get; set; }
    public string? PromotionTitle { get; set; }
    public string? PromotionMessage { get; set; }
    public bool PromotionApplied { get; set; }
    public List<string> Warnings { get; set; } = new();

    public PriceBreakdown? Breakdown { get; set; }
}

public class OrderListVm
{
    public List<Order> Orders { get; set; } = new();
    public OrderStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public decimal TotalSpent { get; set; }
    public double TotalLitres { get; set; }
    public int DeliveredCount { get; set; }
}

public class HomeVm
{
    public List<NationalPriceVm> Board { get; set; } = new();
    public List<PriceSeriesVm> Series { get; set; } = new();
    public List<StationListItemVm> Cheapest { get; set; } = new();
    public List<StationListItemVm> Nearby { get; set; } = new();
    public List<Promotion> Promotions { get; set; } = new();
    public int StationCount { get; set; }
    public int VerifiedStationCount { get; set; }
    public int CityCount { get; set; }
    public int BrandCount { get; set; }
    public DateTime LastPriceUpdateUtc { get; set; }
    public string? UserName { get; set; }
    public RecommendationResultVm? Recommendation { get; set; }

    public QuickOrderVm QuickOrder { get; set; } = new();
    public List<DeliveryVehicle> Drivers { get; set; } = new();
    public Order? ActiveOrder { get; set; }
    public TrackingVm? ActiveTracking { get; set; }
    public WeatherSnapshot? Weather { get; set; }
    public List<RoadCondition> RoadAlerts { get; set; } = new();
    public int DeliveriesCompleted { get; set; }
    public double AverageDeliveryMinutes { get; set; }
    public int BusinessContracts { get; set; }
    public double LitresDelivered { get; set; }

    // landing page: company chrome -------------------------------------
    public List<DeliveryZone> Zones { get; set; } = new();
    public List<HomeSegmentVm> Segments { get; set; } = new();
    public int TankerCount { get; set; }
    public int DeliveryCityCount { get; set; }
    public decimal MinDeliveryFee { get; set; }
    public int ScheduledDiscountPercent { get; set; }
    public string? PriceDecisionReference { get; set; }
    public DateTime? PriceEffectiveFrom { get; set; }
}

/// <summary>
/// One customer segment on the landing page (individuals, small business and
/// construction, fleets and contracts). Numbers are derived from the active
/// pricing tiers and the procurement spread, never typed into the view.
/// </summary>
public class HomeSegmentVm
{
    public string Key { get; set; } = string.Empty;
    public bool Featured { get; set; }

    /// <summary>Smallest discount below the pump price in the group (ден/л, 0 = pump price).</summary>
    public decimal DiscountFrom { get; set; }

    /// <summary>Largest discount below the pump price incl. the best volume band (ден/л).</summary>
    public decimal DiscountTo { get; set; }

    public decimal MinDeliveryFee { get; set; }
    public int ScheduledDiscountPercent { get; set; }
    public decimal EmergencySurcharge { get; set; }
    public double MinLitres { get; set; }
    public double MaxLitres { get; set; }
    public bool HasVolumeBands { get; set; }
}

public class ForecastPointVm
{
    public DateTime Date { get; set; }
    public decimal Price { get; set; }
    public decimal Lower { get; set; }
    public decimal Upper { get; set; }
}

public class ForecastVm
{
    public int FuelTypeId { get; set; }
    public string Fuel { get; set; } = string.Empty;
    public string Color { get; set; } = "#2563EB";
    public decimal CurrentPrice { get; set; }
    public List<ForecastPointVm> Points { get; set; } = new();
    public string Trend { get; set; } = "flat";
    public double Confidence { get; set; }
    public string Model { get; set; } = string.Empty;
    public double BacktestMae { get; set; }
    public double BacktestMape { get; set; }
    public string Advice { get; set; } = string.Empty;
    public string AdviceKey { get; set; } = "ai.advice.hold";
}

public class RecommendedStationVm
{
    public StationListItemVm Station { get; set; } = new();
    public double Score { get; set; }
    public decimal Price { get; set; }
    public double DistanceKm { get; set; }
    public double DetourCostMkd { get; set; }
    public decimal SavingPerTank { get; set; }
    public decimal NetSaving { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public class RecommendationResultVm
{
    public List<RecommendedStationVm> Items { get; set; } = new();
    public string FuelName { get; set; } = string.Empty;
    public int FuelTypeId { get; set; }
    public double TankLitres { get; set; }
    public double Consumption { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public class AnalyticsVm
{
    public List<ForecastVm> Forecasts { get; set; } = new();
    public List<PriceSeriesVm> Series { get; set; } = new();
    public RecommendationResultVm? Recommendation { get; set; }
    public List<BrandPriceVm> ByBrand { get; set; } = new();
    public List<CityPriceVm> ByCity { get; set; } = new();
    public SavingsCalculatorVm Calculator { get; set; } = new();
    public List<FuelType> Fuels { get; set; } = new();
    public int SelectedFuelId { get; set; }
}

public class SavingsCalculatorVm
{
    public double MonthlyKm { get; set; } = 1200;
    public double Consumption { get; set; } = 6.5;
    public int FuelTypeId { get; set; }
    public double LitresPerMonth { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal BestPrice { get; set; }
    public decimal MonthlyCostAverage { get; set; }
    public decimal MonthlyCostBest { get; set; }
    public decimal MonthlySaving { get; set; }
    public decimal YearlySaving { get; set; }
}

public class RoutePlanVm
{
    public int VehicleId { get; set; }
    public string VehicleCode { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public double TotalDistanceKm { get; set; }
    public double NaiveDistanceKm { get; set; }
    public double ImprovementPercent { get; set; }
    public double DurationMinutes { get; set; }
    public double TotalLitres { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public List<RouteStopVm> Stops { get; set; } = new();
}

public class RouteStopVm
{
    public int Sequence { get; set; }
    public int OrderId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double LegDistanceKm { get; set; }
    public double CumulativeKm { get; set; }
    public DateTime Eta { get; set; }
    public double Litres { get; set; }
}

public class LoginVm
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }
}

public class RegisterVm
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone, MaxLength(50)]
    public string? PhoneNumber { get; set; }

    [MaxLength(120)]
    public string? City { get; set; }

    [Required, DataType(DataType.Password), MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Лозинките не се совпаѓаат / Passwords do not match")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string PreferredLanguage { get; set; } = "mk";

    public CustomerType CustomerType { get; set; } = CustomerType.Individual;

    [MaxLength(200)]
    public string? CompanyName { get; set; }

    [MaxLength(20)]
    public string? TaxNumber { get; set; }

    public bool AcceptTerms { get; set; }
    public string? ReturnUrl { get; set; }
}

public class ProfileVm
{
    public ApplicationUser User { get; set; } = new();
    public List<Vehicle> Vehicles { get; set; } = new();
    public List<StationListItemVm> Favorites { get; set; } = new();
    public List<Notification> Notifications { get; set; } = new();
    public List<FuelType> Fuels { get; set; } = new();
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public double TotalLitres { get; set; }
    public decimal EstimatedSavings { get; set; }
    public int PointsToNextTier { get; set; }
    public LoyaltyTier NextTier { get; set; }
}

public class PriceReportVm
{
    [Required]
    public int StationId { get; set; }

    [Required]
    public int FuelTypeId { get; set; }

    [Required, Range(10, 400)]
    public decimal Price { get; set; }

    [MaxLength(400)]
    public string? Note { get; set; }

    public string? StationName { get; set; }
    public List<FuelType> Fuels { get; set; } = new();
}

public class PromotionCardVm
{
    public Promotion Promotion { get; set; } = new();
    public bool IsEligible { get; set; }
    public string ReasonKey { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime? AvailableFrom { get; set; }
    public int UsesLeft { get; set; }
    public List<DayOfWeek> Days { get; set; } = new();
}

public class PromotionsPageVm
{
    public List<PromotionCardVm> Cards { get; set; } = new();
    public LoyaltyTier Tier { get; set; }
    public int Points { get; set; }
    public bool IsAuthenticated { get; set; }
}

public class AdminDashboardVm
{
    public int Stations { get; set; }
    public int OsmStations { get; set; }
    public int VerifiedStations { get; set; }
    public int Users { get; set; }
    public int Orders { get; set; }
    public int OpenOrders { get; set; }
    public int PendingReports { get; set; }
    public decimal Revenue { get; set; }
    public double LitresDelivered { get; set; }
    public DateTime? LastOsmImportUtc { get; set; }
    public DateTime? LastPriceUpdateUtc { get; set; }
    public List<NationalPriceVm> Board { get; set; } = new();
    public List<Order> RecentOrders { get; set; } = new();
    public List<PriceReport> RecentReports { get; set; } = new();
    public Dictionary<string, int> OrdersByStatus { get; set; } = new();
    public Dictionary<string, int> StationsByBrand { get; set; } = new();
}
