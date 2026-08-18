using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<FuelType> FuelTypes => Set<FuelType>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StationReview> StationReviews => Set<StationReview>();
    public DbSet<FavoriteStation> FavoriteStations => Set<FavoriteStation>();

    public DbSet<NationalPrice> NationalPrices => Set<NationalPrice>();
    public DbSet<StationPrice> StationPrices => Set<StationPrice>();
    public DbSet<PriceReport> PriceReports => Set<PriceReport>();
    public DbSet<PricePrediction> PricePredictions => Set<PricePrediction>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<DeliveryVehicle> DeliveryVehicles => Set<DeliveryVehicle>();
    public DbSet<RoutePlan> RoutePlans => Set<RoutePlan>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();

    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();

    public DbSet<BusinessAccount> BusinessAccounts => Set<BusinessAccount>();
    public DbSet<SupplyContract> SupplyContracts => Set<SupplyContract>();
    public DbSet<ContractAsset> ContractAssets => Set<ContractAsset>();
    public DbSet<ContractDelivery> ContractDeliveries => Set<ContractDelivery>();
    public DbSet<DeliveryProof> DeliveryProofs => Set<DeliveryProof>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<PricingSettings> PricingSettings => Set<PricingSettings>();
    public DbSet<PricingTier> PricingTiers => Set<PricingTier>();
    public DbSet<VolumeDiscountBand> VolumeDiscountBands => Set<VolumeDiscountBand>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<TankerCompartment> TankerCompartments => Set<TankerCompartment>();
    public DbSet<TankerTelemetry> TankerTelemetries => Set<TankerTelemetry>();
    public DbSet<ConstructionSite> ConstructionSites => Set<ConstructionSite>();
    public DbSet<DeliverySchedule> DeliverySchedules => Set<DeliverySchedule>();
    public DbSet<FuelAnomaly> FuelAnomalies => Set<FuelAnomaly>();
    public DbSet<BusinessOffer> BusinessOffers => Set<BusinessOffer>();

    public DbSet<WeatherSnapshot> WeatherSnapshots => Set<WeatherSnapshot>();
    public DbSet<RoadCondition> RoadConditions => Set<RoadCondition>();
    public DbSet<BorderCrossing> BorderCrossings => Set<BorderCrossing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                    property.SetProviderClrType(typeof(double));
            }
        }

        builder.Entity<Brand>(b => b.HasIndex(x => x.Slug).IsUnique());
        builder.Entity<FuelType>(b => b.HasIndex(x => x.Code).IsUnique());

        builder.Entity<Station>(b =>
        {
            b.HasIndex(x => x.OsmId);
            b.HasIndex(x => new { x.Latitude, x.Longitude });
            b.HasIndex(x => x.City);
            b.HasIndex(x => x.SearchKey);
            b.HasOne(x => x.Brand).WithMany(x => x.Stations)
             .HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<StationPrice>(b =>
        {
            b.HasIndex(x => new { x.StationId, x.FuelTypeId, x.IsCurrent });
            b.HasIndex(x => x.ObservedAtUtc);
            b.HasOne(x => x.Station).WithMany(x => x.Prices).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.FuelType).WithMany(x => x.Prices).HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.ReportedByUser).WithMany().HasForeignKey(x => x.ReportedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<NationalPrice>(b => b.HasIndex(x => new { x.FuelTypeId, x.EffectiveFrom }).IsUnique());
        builder.Entity<PricePrediction>(b => b.HasIndex(x => new { x.FuelTypeId, x.TargetDate }));

        builder.Entity<PriceReport>(b =>
        {
            b.HasIndex(x => x.Status);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Order>(b =>
        {
            b.HasIndex(x => x.Reference).IsUnique();
            b.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            b.HasIndex(x => x.Status);
            b.HasOne(x => x.User).WithMany(x => x.Orders).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.DeliveryVehicle).WithMany().HasForeignKey(x => x.DeliveryVehicleId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Promotion).WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.PricingTier).WithMany().HasForeignKey(x => x.PricingTierId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.DeliveryZone).WithMany().HasForeignKey(x => x.DeliveryZoneId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.BusinessAccount).WithMany().HasForeignKey(x => x.BusinessAccountId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<OrderStatusHistory>(b =>
            b.HasOne(x => x.Order).WithMany(x => x.StatusHistory).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<Vehicle>(b =>
        {
            b.HasOne(x => x.User).WithMany(x => x.Vehicles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FavoriteStation>(b =>
        {
            b.HasIndex(x => new { x.UserId, x.StationId }).IsUnique();
            b.HasOne(x => x.User).WithMany(x => x.Favorites).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StationReview>(b =>
        {
            b.HasIndex(x => new { x.UserId, x.StationId }).IsUnique();
            b.HasOne(x => x.Station).WithMany(x => x.Reviews).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Promotion>(b =>
        {
            b.HasIndex(x => x.Code).IsUnique();
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PromotionRedemption>(b =>
        {
            b.HasIndex(x => new { x.PromotionId, x.UserId, x.RedeemedAtUtc });
            b.HasOne(x => x.Promotion).WithMany(x => x.Redemptions).HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<RoutePlan>(b =>
            b.HasOne(x => x.DeliveryVehicle).WithMany().HasForeignKey(x => x.DeliveryVehicleId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<RouteStop>(b =>
        {
            b.HasOne(x => x.RoutePlan).WithMany(x => x.Stops).HasForeignKey(x => x.RoutePlanId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Notification>(b =>
        {
            b.HasIndex(x => new { x.UserId, x.IsRead });
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BusinessAccount>(b =>
        {
            b.HasIndex(x => x.TaxNumber).IsUnique();
            b.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SupplyContract>(b =>
        {
            b.HasIndex(x => x.ContractNumber).IsUnique();
            b.HasOne(x => x.BusinessAccount).WithMany(x => x.Contracts).HasForeignKey(x => x.BusinessAccountId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ContractAsset>(b =>
            b.HasOne(x => x.SupplyContract).WithMany(x => x.Assets).HasForeignKey(x => x.SupplyContractId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<ContractDelivery>(b =>
        {
            b.HasIndex(x => x.Reference).IsUnique();
            b.HasIndex(x => x.ScheduledForUtc);
            b.HasOne(x => x.SupplyContract).WithMany(x => x.Deliveries).HasForeignKey(x => x.SupplyContractId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.ContractAsset).WithMany().HasForeignKey(x => x.ContractAssetId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.DeliveryVehicle).WithMany().HasForeignKey(x => x.DeliveryVehicleId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DeliveryProof>(b =>
        {
            b.HasIndex(x => x.ProofNumber).IsUnique();
            b.HasOne(x => x.ContractDelivery).WithOne(x => x.Proof)
             .HasForeignKey<DeliveryProof>(x => x.ContractDeliveryId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Invoice>(b =>
        {
            b.HasIndex(x => x.InvoiceNumber).IsUnique();
            b.HasOne(x => x.BusinessAccount).WithMany(x => x.Invoices).HasForeignKey(x => x.BusinessAccountId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.SupplyContract).WithMany().HasForeignKey(x => x.SupplyContractId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InvoiceLine>(b =>
            b.HasOne(x => x.Invoice).WithMany(x => x.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<ApplicationUser>(b =>
            b.HasOne(x => x.BusinessAccount).WithMany().HasForeignKey(x => x.BusinessAccountId).OnDelete(DeleteBehavior.SetNull));

        builder.Entity<PricingTier>(b => b.HasIndex(x => x.Code).IsUnique());

        builder.Entity<VolumeDiscountBand>(b =>
        {
            b.HasIndex(x => x.FromLitresPerMonth);
            b.HasOne(x => x.PricingTier).WithMany(x => x.Bands).HasForeignKey(x => x.PricingTierId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DeliveryZone>(b => b.HasIndex(x => x.Name).IsUnique());

        builder.Entity<DeliveryVehicle>(b =>
            b.HasOne(x => x.DeliveryZone).WithMany(x => x.Tankers).HasForeignKey(x => x.DeliveryZoneId).OnDelete(DeleteBehavior.SetNull));

        builder.Entity<TankerCompartment>(b =>
        {
            b.HasIndex(x => new { x.DeliveryVehicleId, x.Number }).IsUnique();
            b.HasOne(x => x.DeliveryVehicle).WithMany(x => x.Compartments).HasForeignKey(x => x.DeliveryVehicleId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TankerTelemetry>(b =>
        {
            b.HasIndex(x => new { x.DeliveryVehicleId, x.RecordedAtUtc });
            b.HasOne(x => x.DeliveryVehicle).WithMany().HasForeignKey(x => x.DeliveryVehicleId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ConstructionSite>(b =>
        {
            b.HasIndex(x => new { x.BusinessAccountId, x.Name });
            b.HasOne(x => x.BusinessAccount).WithMany(x => x.Sites).HasForeignKey(x => x.BusinessAccountId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.PrimaryFuelType).WithMany().HasForeignKey(x => x.PrimaryFuelTypeId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ContractAsset>(b =>
            b.HasOne(x => x.ConstructionSite).WithMany(x => x.Machines).HasForeignKey(x => x.ConstructionSiteId).OnDelete(DeleteBehavior.SetNull));

        builder.Entity<DeliverySchedule>(b =>
        {
            b.HasIndex(x => new { x.IsActive, x.StartsOn });
            b.HasOne(x => x.BusinessAccount).WithMany().HasForeignKey(x => x.BusinessAccountId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.SupplyContract).WithMany().HasForeignKey(x => x.SupplyContractId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.ConstructionSite).WithMany().HasForeignKey(x => x.ConstructionSiteId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FuelAnomaly>(b =>
        {
            b.HasIndex(x => new { x.Status, x.DetectedAtUtc });
            b.HasOne(x => x.DeliveryVehicle).WithMany().HasForeignKey(x => x.DeliveryVehicleId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.ContractDelivery).WithMany().HasForeignKey(x => x.ContractDeliveryId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.BusinessAccount).WithMany().HasForeignKey(x => x.BusinessAccountId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<BusinessOffer>(b =>
        {
            b.HasIndex(x => x.OfferNumber).IsUnique();
            b.HasOne(x => x.BusinessAccount).WithMany().HasForeignKey(x => x.BusinessAccountId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BusinessAccount>(b =>
            b.HasOne(x => x.PricingTier).WithMany().HasForeignKey(x => x.PricingTierId).OnDelete(DeleteBehavior.SetNull));

        builder.Entity<RoutePlan>(b =>
            b.HasOne(x => x.DeliveryZone).WithMany().HasForeignKey(x => x.DeliveryZoneId).OnDelete(DeleteBehavior.SetNull));

        builder.Entity<RouteStop>(b =>
            b.HasOne(x => x.FuelType).WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.SetNull));

        builder.Entity<WeatherSnapshot>(b => b.HasIndex(x => new { x.City, x.ObservedAtUtc }));
        builder.Entity<RoadCondition>(b => b.HasIndex(x => x.ReportedAtUtc));
        builder.Entity<BorderCrossing>(b => b.HasIndex(x => x.Name).IsUnique());
    }
}
