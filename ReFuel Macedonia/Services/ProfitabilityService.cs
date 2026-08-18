using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services;

public interface IProfitabilityService
{
    Task<ProfitabilityVm> GetAsync(int days);
}

public class ProfitabilityService : IProfitabilityService
{
    private readonly ApplicationDbContext _db;
    private readonly IPricingService _pricing;
    private readonly ITranslator _t;

    public ProfitabilityService(ApplicationDbContext db, IPricingService pricing, ITranslator translator)
    {
        _db = db;
        _pricing = pricing;
        _t = translator;
    }

    public async Task<ProfitabilityVm> GetAsync(int days)
    {
        days = Math.Clamp(days, 7, 3650);

        var since = DateTime.UtcNow.Date.AddDays(-days);
        var settings = await _pricing.GetSettingsAsync();
        var tiers = await _pricing.GetTiersAsync(onlyActive: false);
        var spread = Math.Max(0, settings.ProcurementSpreadPerLitre);

        var vm = new ProfitabilityVm
        {
            Days = days,
            Settings = settings
        };

        var accounts = await _db.BusinessAccounts
            .OrderBy(a => a.CompanyName)
            .ToListAsync();

        var rows = new Dictionary<int, CompanyProfitVm>();

        foreach (var account in accounts)
        {
            var tier = tiers.FirstOrDefault(t => t.Id == account.PricingTierId)
                       ?? tiers.FirstOrDefault(t => t.CustomerType == account.CustomerType && t.IsActive)
                       ?? tiers.FirstOrDefault(t => t.CustomerType == account.CustomerType);

            rows[account.Id] = new CompanyProfitVm
            {
                BusinessAccountId = account.Id,
                Company = account.CompanyName,
                City = account.City,
                CustomerType = account.CustomerType,
                TierName = tier?.LocalizedName(_t.Culture)
            };
        }

        var individuals = new CompanyProfitVm
        {
            Company = _t["admin.profit.individuals"],
            CustomerType = CustomerType.Individual,
            TierName = tiers.FirstOrDefault(t => t.CustomerType == CustomerType.Individual)?.LocalizedName(_t.Culture)
        };

        var distanceSum = new Dictionary<int, double>();
        var individualDistance = 0d;

        var orders = await _db.Orders
            .Where(o => o.CreatedAtUtc >= since &&
                        o.Status != OrderStatus.Cancelled &&
                        o.Status != OrderStatus.Failed)
            .Select(o => new
            {
                o.BusinessAccountId,
                o.Type,
                o.Litres,
                o.DistanceKm,
                o.FuelSubtotal,
                o.DeliveryFee,
                o.EmergencySurcharge,
                o.DiscountAmount,
                o.GrossFuelMargin,
                o.MarginPerLitre
            })
            .ToListAsync();

        foreach (var order in orders)
        {
            var row = order.BusinessAccountId is int id && rows.TryGetValue(id, out var found) ? found : individuals;

            var grossMargin = order.GrossFuelMargin > 0
                ? order.GrossFuelMargin
                : Math.Round(order.MarginPerLitre * (decimal)order.Litres, 2);

            // Standard and scheduled orders share a route; an emergency sends a tanker on its own.
            var cost = DeliveryCosting.For(settings, order.DistanceKm, DeliveryCosting.IsGrouped(order.Type));

            row.Litres += order.Litres;
            row.Deliveries += 1;
            row.Revenue += order.FuelSubtotal + order.DeliveryFee + order.EmergencySurcharge - order.DiscountAmount;
            row.GrossFuelMargin += grossMargin;
            row.FuelCost += Math.Round(order.FuelSubtotal - grossMargin, 2);
            row.DeliveryRevenue += order.DeliveryFee + order.EmergencySurcharge;
            row.Discounts += order.DiscountAmount;
            row.DriverCost += cost.Driver;
            row.TankerCost += cost.Tanker;
            row.DispatchCost += cost.Dispatch;

            if (order.BusinessAccountId is int accountId && rows.ContainsKey(accountId))
                distanceSum[accountId] = distanceSum.GetValueOrDefault(accountId) + order.DistanceKm;
            else
                individualDistance += order.DistanceKm;
        }

        var deliveries = await _db.ContractDeliveries
            .Include(d => d.SupplyContract)
            .Where(d => d.Status == OrderStatus.Delivered &&
                        d.ScheduledForUtc >= since &&
                        d.SupplyContract != null)
            .ToListAsync();

        var bases = await _db.DeliveryVehicles
            .Select(v => new { v.Id, v.BaseLatitude, v.BaseLongitude })
            .ToListAsync();

        foreach (var delivery in deliveries)
        {
            var contract = delivery.SupplyContract!;
            if (!rows.TryGetValue(contract.BusinessAccountId, out var row)) continue;

            // A contract sells at pump − discount; ReFuel bought at pump − spread. When the drop
            // recorded its reference pump price the margin is exact, otherwise it is estimated from
            // the contract's current discount. Not clamped: a discount larger than the spread is a
            // real loss and the report should say so.
            var marginPerLitre = delivery.ReferencePricePerLitre > 0
                ? delivery.UnitPrice - (delivery.ReferencePricePerLitre - spread)
                : spread - contract.DiscountPerLitre;

            var litres = delivery.DeliveredLitres;
            var grossMargin = Math.Round(marginPerLitre * (decimal)litres, 2);

            var home = bases.FirstOrDefault(b => b.Id == delivery.DeliveryVehicleId)
                       ?? bases.OrderBy(b => GeoUtils.HaversineKm(
                              b.BaseLatitude, b.BaseLongitude, contract.DefaultLatitude, contract.DefaultLongitude))
                          .FirstOrDefault();

            var distance = home is null
                ? 12
                : GeoUtils.RoadDistanceKm(home.BaseLatitude, home.BaseLongitude,
                      contract.DefaultLatitude, contract.DefaultLongitude);

            var cost = DeliveryCosting.DedicatedTrip(settings, distance);

            row.Litres += litres;
            row.Deliveries += 1;
            row.Revenue += delivery.LineTotal + contract.FixedDeliveryFee;
            row.GrossFuelMargin += grossMargin;
            row.FuelCost += Math.Round(delivery.LineTotal - grossMargin, 2);
            row.DeliveryRevenue += contract.FixedDeliveryFee;
            row.DriverCost += cost.Driver;
            row.TankerCost += cost.Tanker;
            row.DispatchCost += cost.Dispatch;

            distanceSum[contract.BusinessAccountId] = distanceSum.GetValueOrDefault(contract.BusinessAccountId) + distance;
        }

        void Finalise(CompanyProfitVm row, double totalDistance)
        {
            row.Litres = Math.Round(row.Litres, 1);
            row.Revenue = Math.Round(row.Revenue, 0);
            row.FuelCost = Math.Round(row.FuelCost, 0);
            row.GrossFuelMargin = Math.Round(row.GrossFuelMargin, 0);
            row.DeliveryRevenue = Math.Round(row.DeliveryRevenue, 0);
            row.Discounts = Math.Round(row.Discounts, 0);
            row.DriverCost = Math.Round(row.DriverCost, 0);
            row.TankerCost = Math.Round(row.TankerCost, 0);
            row.DispatchCost = Math.Round(row.DispatchCost, 0);
            row.DeliveryCost = row.DriverCost + row.TankerCost + row.DispatchCost;
            row.GrossContribution = row.GrossFuelMargin + row.DeliveryRevenue - row.Discounts - row.DeliveryCost;
            row.AverageDistanceKm = row.Deliveries <= 0 ? 0 : Math.Round(totalDistance / row.Deliveries, 1);
        }

        foreach (var (id, row) in rows)
            Finalise(row, distanceSum.GetValueOrDefault(id));

        Finalise(individuals, individualDistance);

        vm.Companies = rows.Values
            .Where(r => r.Deliveries > 0)
            .OrderByDescending(r => r.GrossContribution)
            .ToList();

        vm.Individuals = individuals;

        var all = vm.AllRows.ToList();

        vm.Total = new CompanyProfitVm
        {
            Company = _t["admin.profit.total"],
            Litres = Math.Round(all.Sum(r => r.Litres), 1),
            Deliveries = all.Sum(r => r.Deliveries),
            Revenue = all.Sum(r => r.Revenue),
            FuelCost = all.Sum(r => r.FuelCost),
            GrossFuelMargin = all.Sum(r => r.GrossFuelMargin),
            DeliveryRevenue = all.Sum(r => r.DeliveryRevenue),
            Discounts = all.Sum(r => r.Discounts),
            DriverCost = all.Sum(r => r.DriverCost),
            TankerCost = all.Sum(r => r.TankerCost),
            DispatchCost = all.Sum(r => r.DispatchCost),
            AverageDistanceKm = all.Sum(r => r.Deliveries) <= 0
                ? 0
                : Math.Round(all.Sum(r => r.AverageDistanceKm * r.Deliveries) / all.Sum(r => r.Deliveries), 1)
        };

        vm.Total.DeliveryCost = vm.Total.DriverCost + vm.Total.TankerCost + vm.Total.DispatchCost;
        vm.Total.GrossContribution =
            vm.Total.GrossFuelMargin + vm.Total.DeliveryRevenue - vm.Total.Discounts - vm.Total.DeliveryCost;

        vm.PeriodFixedCosts = Math.Round(settings.MonthlyFixedCosts / 30m * days, 0);
        vm.OperatingResult = vm.Total.GrossContribution - vm.PeriodFixedCosts;

        vm.Assumptions.Add(_t.Format("economics.assumption.spread", Fmt.N(spread, 2)));
        vm.Assumptions.Add(_t.Format("admin.profit.assumption.margin", Fmt.N(vm.Total.MarginPerLitre, 2)));
        vm.Assumptions.Add(_t.Format("economics.assumption.cost",
            Fmt.N(settings.TankerCostPerKm, 2), Fmt.N(settings.DriverCostPerHour, 0), Fmt.N(settings.FixedDispatchCost, 0)));
        vm.Assumptions.Add(_t.Format("admin.profit.assumption.route",
            Math.Max(1, settings.AverageStopsPerRoute), Fmt.N(settings.InterStopKm, 1), settings.ServiceMinutesPerStop));
        vm.Assumptions.Add(_t.Format("admin.profit.assumption.fixed",
            Fmt.N(settings.MonthlyFixedCosts, 0), days, Fmt.N(vm.PeriodFixedCosts, 0)));
        vm.Assumptions.Add(_t["economics.assumption.excluded"]);
        vm.Assumptions.Add(_t["economics.assumption.estimate"]);

        return vm;
    }
}
