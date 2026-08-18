using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Data;

public static class DbSeeder
{
    public const string RoleAdmin = "Administrator";
    public const string RoleCustomer = "Customer";
    public const string RoleDriver = "Driver";
    public const string RoleBusiness = "Business";

    private static Random NewRandom() => new(20260811);

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReFuelOptions>>().Value;

        await EnsureSchemaAsync(db, logger);

        await SeedRolesAsync(roleManager);
        await SeedBrandsAndFuelsAsync(db);
        await SeedPricingAsync(db, logger);
        await SeedNationalPricesAsync(db, logger);
        await SeedStationsAsync(db, logger);
        await SeedStationPricesAsync(db, logger);
        await SeedPromotionsAsync(db);
        await SeedFleetAsync(db);
        await SeedTankersAsync(db, logger);
        await SeedConditionsAsync(db);
        await SeedUsersAsync(db, userManager, options, logger);
        await SeedOrdersAsync(db, options, logger);
        await SeedBusinessAsync(db, userManager, options, logger);
        await SeedFleetOperationsAsync(db, logger);
        await UpgradePricingModelAsync(db, options, logger);
        await BackfillOrderEconomicsAsync(db, logger);
    }


    private static Task EnsureSchemaAsync(ApplicationDbContext db, ILogger logger)
        => SchemaGuard.EnsureUpToDateAsync(db, logger);

    private static List<VolumeDiscountBand> BuildBands(IReadOnlyDictionary<string, int> tierIdsByCode)
    {
        var sort = 1;
        var bands = new List<VolumeDiscountBand>();

        foreach (var b in SeedData.Bands)
        {
            int? tierId = null;
            if (b.TierCode is not null)
            {
                if (!tierIdsByCode.TryGetValue(b.TierCode, out var id)) continue;
                tierId = id;
            }

            bands.Add(new VolumeDiscountBand
            {
                PricingTierId = tierId,
                FromLitresPerMonth = b.From,
                ToLitresPerMonth = b.To,
                DiscountPerLitre = b.Discount,
                LabelMk = b.Mk,
                LabelEn = b.En,
                LabelSq = b.Sq,
                IsActive = true,
                SortOrder = sort++
            });
        }

        return bands;
    }

    private static async Task SeedPricingAsync(ApplicationDbContext db, ILogger logger)
    {
        if (!await db.PricingSettings.AnyAsync())
        {
            db.PricingSettings.Add(new PricingSettings
            {
                TankerCostPerKm = 11m,
                DriverCostPerHour = 320m,
                FixedDispatchCost = 60m,
                MonthlyFixedCosts = 420000m,
                VatRate = 18m,
                AverageSpeedKph = 46,
                ChargeReturnLeg = true,
                ProcurementSpreadPerLitre = SeedData.ProcurementSpreadPerLitre,
                AverageStopsPerRoute = SeedData.AverageStopsPerRoute,
                InterStopKm = SeedData.InterStopKm,
                ServiceMinutesPerStop = SeedData.ServiceMinutesPerStop,
                ContractMinDropLitres = SeedData.ContractMinDropLitres,
                PricingModelVersion = PricingModelVersions.Current,
                ShowPriceExplanation = true,
                ShowMarginToCustomer = false,
                UpdatedBy = "seed"
            });

            await db.SaveChangesAsync();
        }

        if (!await db.PricingTiers.AnyAsync())
        {
            db.PricingTiers.AddRange(SeedData.Tiers.Select(t => new PricingTier
            {
                Code = t.Code,
                NameMk = t.Mk,
                NameEn = t.En,
                NameSq = t.Sq,
                CustomerType = t.Type,
                MarginPerLitre = t.Margin,
                MinMarginPerLitre = t.MinMargin,
                MinDeliveryFee = t.MinFee,
                DeliveryFeePerKm = t.PerKm,
                FreeDeliveryThreshold = t.FreeThreshold,
                FreeDeliveryScheduledOnly = t.FreeScheduledOnly,
                ScheduledDeliveryDiscountPercent = t.ScheduledDiscount,
                EmergencySurcharge = t.Emergency,
                MinOrderLitres = t.MinLitres,
                MaxOrderLitres = t.MaxLitres,
                IsNegotiated = t.Negotiated,
                IsActive = true,
                SortOrder = t.Sort,
                MarketingMessageMk = t.MarketingMk,
                MarketingMessageEn = t.MarketingEn,
                MarketingMessageSq = t.MarketingSq
            }));

            await db.SaveChangesAsync();
        }

        if (!await db.VolumeDiscountBands.AnyAsync())
        {
            var tierIds = await db.PricingTiers.ToDictionaryAsync(t => t.Code, t => t.Id);
            db.VolumeDiscountBands.AddRange(BuildBands(tierIds));
            await db.SaveChangesAsync();
        }

        if (!await db.DeliveryZones.AnyAsync())
        {
            db.DeliveryZones.AddRange(SeedData.Zones.Select(z => new DeliveryZone
            {
                Name = z.Name,
                Cities = z.Cities,
                CenterLatitude = z.Lat,
                CenterLongitude = z.Lon,
                RadiusKm = z.Radius,
                BaseDeliveryFee = z.BaseFee,
                FeePerKm = z.PerKm,
                MinOrderLitres = z.MinLitres,
                AverageSpeedKph = z.Speed,
                ColorHex = z.Color,
                IsActive = true,
                SortOrder = z.Sort
            }));

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} delivery zones and the configurable pricing tiers.", SeedData.Zones.Length);
        }
    }

    private static async Task SeedTankersAsync(ApplicationDbContext db, ILogger logger)
    {
        var tankers = await db.DeliveryVehicles.ToListAsync();
        if (tankers.Count == 0) return;

        var zones = await db.DeliveryZones.ToListAsync();
        var fuels = await db.FuelTypes.ToDictionaryAsync(f => f.Code, f => f.Id);
        var rnd = NewRandom();

        foreach (var tanker in tankers)
        {
            if (tanker.DeliveryZoneId is null && zones.Count > 0)
            {
                var zone = zones.FirstOrDefault(z => z.CityList.Any(c =>
                              string.Equals(c, tanker.BaseCity, StringComparison.OrdinalIgnoreCase)))
                           ?? zones.OrderBy(z => GeoUtils.HaversineKm(
                                  tanker.BaseLatitude, tanker.BaseLongitude, z.CenterLatitude, z.CenterLongitude))
                              .First();

                tanker.DeliveryZoneId = zone.Id;
            }

            if (tanker.CurrentLatitude == 0 && tanker.CurrentLongitude == 0)
            {
                tanker.CurrentLatitude = Math.Round(tanker.BaseLatitude + (rnd.NextDouble() - 0.5) * 0.06, 6);
                tanker.CurrentLongitude = Math.Round(tanker.BaseLongitude + (rnd.NextDouble() - 0.5) * 0.06, 6);
            }

            if (tanker.OdometerKm <= 0)
                tanker.OdometerKm = Math.Round(90000 + rnd.NextDouble() * 180000, 0);

            tanker.MaintenanceDueOn ??= DateTime.UtcNow.Date.AddDays(rnd.Next(12, 160));
            tanker.InsuranceValidUntil ??= DateTime.UtcNow.Date.AddDays(rnd.Next(40, 330));
            tanker.LastTelemetryUtc ??= DateTime.UtcNow.AddMinutes(-rnd.Next(1, 14));
        }

        await db.SaveChangesAsync();

        if (!await db.TankerCompartments.AnyAsync())
        {
            var byCode = tankers.ToDictionary(t => t.Code, t => t);

            foreach (var seed in SeedData.Compartments)
            {
                if (!byCode.TryGetValue(seed.TruckCode, out var tanker)) continue;
                if (!fuels.TryGetValue(seed.FuelCode, out var fuelId)) continue;

                db.TankerCompartments.Add(new TankerCompartment
                {
                    DeliveryVehicleId = tanker.Id,
                    Number = seed.Number,
                    CapacityLitres = seed.Capacity,
                    CurrentLitres = Math.Round(seed.Capacity * seed.Fill, 0),
                    FuelTypeId = fuelId
                });
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} tanker compartments.", SeedData.Compartments.Length);
        }

        if (!await db.TankerTelemetries.AnyAsync())
        {
            var loads = await db.TankerCompartments
                .GroupBy(c => c.DeliveryVehicleId)
                .Select(g => new { Id = g.Key, Litres = g.Sum(c => c.CurrentLitres) })
                .ToListAsync();

            var loadById = loads.ToDictionary(l => l.Id, l => l.Litres);

            foreach (var tanker in tankers)
            {
                var litres = loadById.TryGetValue(tanker.Id, out var value) ? value : tanker.CapacityLitres * 0.6;
                var odometer = tanker.OdometerKm - 60;

                for (var step = 12; step >= 0; step--)
                {
                    var moment = DateTime.UtcNow.AddMinutes(-step * 25);
                    var drift = (rnd.NextDouble() - 0.5) * 0.05;

                    odometer += rnd.NextDouble() * 6;
                    litres = Math.Max(0, litres - (step % 4 == 0 ? rnd.Next(20, 90) : 0));

                    db.TankerTelemetries.Add(new TankerTelemetry
                    {
                        DeliveryVehicleId = tanker.Id,
                        RecordedAtUtc = moment,
                        Latitude = Math.Round(tanker.BaseLatitude + drift, 6),
                        Longitude = Math.Round(tanker.BaseLongitude + drift, 6),
                        SpeedKph = Math.Round(rnd.NextDouble() * 62, 1),
                        HeadingDegrees = Math.Round(rnd.NextDouble() * 360, 0),
                        LitresOnBoard = Math.Round(litres, 1),
                        OdometerKm = Math.Round(odometer, 1),
                        PumpActive = step % 4 == 0,
                        EngineOn = true,
                        Source = TelemetrySource.Simulator,
                        Note = "Seeded simulator sample."
                    });
                }
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded the tanker telemetry history.");
        }
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in new[] { RoleAdmin, RoleCustomer, RoleDriver, RoleBusiness })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private static async Task SeedBrandsAndFuelsAsync(ApplicationDbContext db)
    {
        if (!await db.Brands.AnyAsync())
        {
            db.Brands.AddRange(SeedData.Brands.Select(b => new Brand
            {
                Name = b.Name,
                NameMk = b.NameMk,
                Slug = b.Slug,
                ColorHex = b.Color,
                Website = b.Website,
                IsDeliveryPartner = b.DeliveryPartner
            }));
            await db.SaveChangesAsync();
        }

        if (!await db.FuelTypes.AnyAsync())
        {
            db.FuelTypes.AddRange(SeedData.Fuels.Select(f => new FuelType
            {
                Code = f.Code,
                NameMk = f.Mk,
                NameEn = f.En,
                NameSq = f.Sq,
                Unit = f.Unit,
                ColorHex = f.Color,
                IsRegulated = f.Regulated,
                IsDeliverable = f.Deliverable,
                SortOrder = f.Sort
            }));
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedNationalPricesAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.NationalPrices.AnyAsync()) return;

        var fuels = await db.FuelTypes.ToDictionaryAsync(f => f.Code, f => f.Id);
        var rnd = NewRandom();

        var anchors = SeedData.PriceAnchors.OrderBy(a => a.Date).ToArray();
        var last = anchors[^1];

        var start = last.Date.AddMonths(-SeedData.HistoryMonths);
        var dates = new List<DateTime>();
        for (var d = start; d < last.Date; d = d.AddDays(7)) dates.Add(d.Date);
        foreach (var a in anchors) if (!dates.Contains(a.Date.Date)) dates.Add(a.Date.Date);
        dates = dates.Distinct().OrderBy(d => d).ToList();

        var current = new Dictionary<string, decimal>
        {
            ["EUROSUPER_95"] = 78.50m,
            ["EUROSUPER_98"] = 80.50m,
            ["EURODIESEL"] = 82.00m,
            ["EL_1"] = 83.50m,
            ["MAZUT_M1"] = 38.500m,
            ["LPG"] = 42.00m,
            ["CNG"] = 74.00m
        };

        var rows = new List<NationalPrice>();

        foreach (var date in dates)
        {
            var anchor = anchors.FirstOrDefault(a => a.Date.Date == date);
            var nextAnchor = anchors.FirstOrDefault(a => a.Date.Date > date) ?? anchors[^1];
            var weeksToAnchor = Math.Max(1, (int)Math.Ceiling((nextAnchor.Date.Date - date).TotalDays / 7.0));

            var seasonal = (decimal)(Math.Sin((date.DayOfYear / 365.0) * 2 * Math.PI) * 0.55);

            foreach (var code in current.Keys.ToList())
            {
                decimal value;
                var official = false;

                if (anchor is not null && code is "EUROSUPER_95" or "EUROSUPER_98" or "EURODIESEL" or "EL_1" or "MAZUT_M1")
                {
                    value = code switch
                    {
                        "EUROSUPER_95" => anchor.Bs95,
                        "EUROSUPER_98" => anchor.Bs98,
                        "EURODIESEL" => anchor.Diesel,
                        "EL_1" => anchor.El1,
                        _ => anchor.Mazut
                    };
                    official = true;
                }
                else
                {
                    var target = code switch
                    {
                        "EUROSUPER_95" => nextAnchor.Bs95,
                        "EUROSUPER_98" => nextAnchor.Bs98,
                        "EURODIESEL" => nextAnchor.Diesel,
                        "EL_1" => nextAnchor.El1,
                        "MAZUT_M1" => nextAnchor.Mazut,
                        "LPG" => SeedData.LpgStartPrice,
                        _ => SeedData.CngStartPrice
                    };
                    value = Drift(current[code], target, weeksToAnchor, rnd, seasonal);
                }

                var change = Math.Round(value - current[code], 3);
                current[code] = value;

                if (!fuels.TryGetValue(code, out var fuelId)) continue;

                rows.Add(new NationalPrice
                {
                    FuelTypeId = fuelId,
                    EffectiveFrom = date,
                    Price = Math.Round(value, 3),
                    Change = change,
                    IsOfficial = official,
                    Source = official ? PriceSource.RegulatoryCommission : PriceSource.Estimated,
                    DecisionReference = official ? $"РКЕ одлука од {date:dd.MM.yyyy}" : null
                });
            }
        }

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.NationalPrices.AddRange(rows);
        await db.SaveChangesAsync();
        db.ChangeTracker.AutoDetectChangesEnabled = true;

        logger.LogInformation("Seeded {Count} national price rows from {From:d}.", rows.Count, dates[0]);
    }

    private static decimal Drift(decimal current, decimal target, int weeksLeft, Random rnd, decimal seasonal)
    {
        var pull = (target - current) / Math.Max(1, weeksLeft * 3);
        var noise = (decimal)((rnd.NextDouble() - 0.5) * 2.2);
        var next = current + pull + noise + seasonal * 0.25m;
        next = Math.Round(next * 2, MidpointRounding.AwayFromZero) / 2m;
        return Math.Clamp(next, 30m, 140m);
    }

    private static async Task SeedStationsAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.Stations.AnyAsync()) return;

        var brands = await db.Brands.ToListAsync();
        var rnd = NewRandom();
        var stations = new List<Station>();

        var pool = new List<Brand>();
        foreach (var b in brands)
        {
            var seed = SeedData.Brands.First(s => s.Slug == b.Slug);
            for (var i = 0; i < seed.Share; i++) pool.Add(b);
        }

        var counters = new Dictionary<string, int>();

        foreach (var city in SeedData.Cities)
        {
            for (var i = 0; i < city.Weight; i++)
            {
                var brand = pool[rnd.Next(pool.Count)];
                counters.TryGetValue(brand.Slug, out var n);
                counters[brand.Slug] = ++n;

                var radiusKm = city.Weight > 20 ? 7.0 : 3.0;
                var dLat = (rnd.NextDouble() - 0.5) * 2 * radiusKm / 111.32;
                var dLon = (rnd.NextDouble() - 0.5) * 2 * radiusKm / (111.32 * Math.Cos(city.Lat * Math.PI / 180));

                var onMotorway = city.Motorway && rnd.NextDouble() < 0.30;
                var street = SeedData.Streets[rnd.Next(SeedData.Streets.Length)];
                var name = $"{brand.NameMk ?? brand.Name} {city.Name} {n:D2}";
                var address = $"{street} бр. {rnd.Next(1, 220)}, {city.Name}";

                stations.Add(new Station
                {
                    Name = name,
                    SearchKey = Translit.BuildSearchKey(name, brand.Name, brand.NameMk, city.Name, city.Municipality, address),
                    BrandId = brand.Id,
                    City = city.Name,
                    Municipality = city.Municipality,
                    Address = address,
                    Latitude = Math.Round(city.Lat + dLat, 6),
                    Longitude = Math.Round(city.Lon + dLon, 6),
                    Phone = $"+389 {rnd.Next(70, 79)} {rnd.Next(100, 999)} {rnd.Next(100, 999)}",
                    IsOpen24h = rnd.NextDouble() < 0.55,
                    OpeningHours = rnd.NextDouble() < 0.55 ? "Mo-Su 00:00-24:00" : "Mo-Su 06:00-22:00",
                    HasLpg = rnd.NextDouble() < 0.45,
                    HasCng = rnd.NextDouble() < 0.08,
                    HasShop = rnd.NextDouble() < 0.80,
                    HasCarWash = rnd.NextDouble() < 0.35,
                    HasRestaurant = onMotorway || rnd.NextDouble() < 0.15,
                    HasToilets = rnd.NextDouble() < 0.85,
                    HasEvCharger = rnd.NextDouble() < 0.12,
                    HasTruckLane = onMotorway || rnd.NextDouble() < 0.25,
                    IsOnMotorway = onMotorway,
                    Source = StationSource.Seed,
                    IsVerified = false
                });
            }
        }

        db.Stations.AddRange(stations);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} fallback stations.", stations.Count);
    }

    private static async Task SeedStationPricesAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.StationPrices.AnyAsync()) return;

        var rnd = NewRandom();
        var fuels = await db.FuelTypes.OrderBy(f => f.SortOrder).ToListAsync();
        var stations = await db.Stations.AsNoTracking().ToListAsync();

        const int historyDepth = 26;

        var byFuel = new Dictionary<int, List<NationalPrice>>();
        foreach (var fuel in fuels)
        {
            byFuel[fuel.Id] = await db.NationalPrices
                .Where(p => p.FuelTypeId == fuel.Id)
                .OrderByDescending(p => p.EffectiveFrom)
                .Take(historyDepth)
                .ToListAsync();
        }

        var historyCodes = new[] { "EUROSUPER_95", "EUROSUPER_98", "EURODIESEL" };
        var historyFuelIds = fuels.Where(f => historyCodes.Contains(f.Code)).Select(f => f.Id).ToHashSet();

        var rows = new List<StationPrice>();

        foreach (var station in stations)
        {
            var brandFactor = (station.BrandId ?? 0) % 5 * 0.15;
            var motorwayPenalty = station.IsOnMotorway ? -0.6 : 0.0;
            var stationFactor = rnd.NextDouble() * 1.8;

            foreach (var fuel in fuels)
            {
                if (fuel.Code == "LPG" && !station.HasLpg) continue;
                if (fuel.Code == "CNG" && !station.HasCng) continue;
                if (fuel.Code is "MAZUT_M1" or "EL_1" && rnd.NextDouble() < 0.6) continue;

                if (!byFuel.TryGetValue(fuel.Id, out var series) || series.Count == 0) continue;

                var discount = (decimal)Math.Max(0, brandFactor + stationFactor + motorwayPenalty);
                var take = historyFuelIds.Contains(fuel.Id) ? Math.Min(historyDepth, series.Count) : 1;

                for (var i = 0; i < take; i++)
                {
                    var basePrice = series[i].Price;
                    var price = Math.Round(Math.Max(basePrice - discount, basePrice * 0.93m), 2);

                    rows.Add(new StationPrice
                    {
                        StationId = station.Id,
                        FuelTypeId = fuel.Id,
                        Price = price,
                        ObservedAtUtc = DateTime.SpecifyKind(series[i].EffectiveFrom.AddHours(6), DateTimeKind.Utc),
                        Source = i == 0 ? PriceSource.CompanyWebsite : PriceSource.Estimated,
                        Confidence = i == 0 ? 0.85 : 0.55,
                        IsCurrent = i == 0
                    });
                }
            }
        }

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.StationPrices.AddRange(rows);
        await db.SaveChangesAsync();
        db.ChangeTracker.AutoDetectChangesEnabled = true;

        logger.LogInformation("Seeded {Count} station price rows.", rows.Count);
    }

    /// <summary>Codes of the v1 promotions that discounted the litre itself. Retired: on a 4-denar margin, "5 % off fuel" is a loss.</summary>
    private static readonly string[] RetiredPromotionCodes =
        { "WELCOME10", "TUESDAY3", "FREEDELIVERY", "DIESEL150", "WEEKEND5", "SOS200", "GOLD7", "SKOPJE_WED" };

    private static async Task SeedPromotionsAsync(ApplicationDbContext db)
    {
        if (await db.Promotions.AnyAsync()) return;
        db.Promotions.AddRange(BuildPromotions(DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The promotions ReFuel can afford. Fuel is sold at a regulated maximum price on a thin, fixed
    /// margin, so every promotion here is on the delivery service or on loyalty — never on the
    /// litre. The engine also caps any promotion at the delivery fee plus the tier's margin room.
    /// </summary>
    private static List<Promotion> BuildPromotions(DateTime now)
    {
        const int everyDay = 127;

        return new List<Promotion>
        {
            new()
            {
                Code = "WELCOME",
                TitleMk = "Добредојде: бесплатна прва достава",
                TitleEn = "Welcome: your first delivery is free",
                TitleSq = "Mirë se vini: dërgesa e parë falas",
                DescriptionMk = "Важи еднаш, за првата нарачка на нов корисник, минимум 20 литри. Горивото е по цена на пумпа — доставата ја честиме ние.",
                DescriptionEn = "Valid once, on a new customer's first order, minimum 20 litres. Fuel at the pump price — the delivery is on us.",
                DescriptionSq = "Vlen një herë, për porosinë e parë të një klienti të ri, minimumi 20 litra. Karburanti me çmimin e pompës — dërgesën e paguajmë ne.",
                DiscountType = DiscountType.FreeDelivery,
                Value = 0m, MinLitres = 20,
                DayOfWeekMask = everyDay, CooldownDays = 0, MaxUsesPerUser = 1,
                NewCustomersOnly = true,
                StartsAtUtc = now.AddMonths(-2), EndsAtUtc = now.AddMonths(10),
                ColorHex = "#0F766E", Icon = "gift"
            },
            new()
            {
                Code = "GOLDDELIVERY",
                TitleMk = "Gold членови: бесплатна достава",
                TitleEn = "Gold members: free delivery",
                TitleSq = "Anëtarët Gold: dërgesë falas",
                DescriptionMk = "За корисници со Gold или Platinum статус, еднаш на 20 дена, за нарачки од најмалку 30 литри.",
                DescriptionEn = "For Gold and Platinum members, once every 20 days, on orders of at least 30 litres.",
                DescriptionSq = "Për anëtarët Gold dhe Platinum, një herë në 20 ditë, për porosi prej të paktën 30 litrash.",
                DiscountType = DiscountType.FreeDelivery,
                Value = 0m, MinLitres = 30,
                DayOfWeekMask = everyDay, CooldownDays = 20, MaxUsesPerUser = 0,
                RequiredTier = LoyaltyTier.Gold,
                StartsAtUtc = now.AddMonths(-5), EndsAtUtc = now.AddMonths(12),
                ColorHex = "#CA8A04", Icon = "star"
            },
            new()
            {
                Code = "FULLTANK",
                TitleMk = "Полн резервоар: -100 ден на доставата",
                TitleEn = "Full tank: 100 MKD off the delivery",
                TitleSq = "Rezervuar plot: 100 den zbritje në dërgesë",
                DescriptionMk = "За нарачки од 50 литри и повеќе, еднаш неделно по корисник. Попустот е на доставата, не на горивото.",
                DescriptionEn = "On orders of 50 litres or more, once a week per customer. The discount is on the delivery, not on the fuel.",
                DescriptionSq = "Për porosi prej 50 litrash e më shumë, një herë në javë për klient. Zbritja është në dërgesë, jo në karburant.",
                DiscountType = DiscountType.FixedAmount,
                Value = 100m, MinLitres = 50,
                DayOfWeekMask = everyDay, CooldownDays = 7, MaxUsesPerUser = 0,
                StartsAtUtc = now.AddMonths(-2), EndsAtUtc = now.AddMonths(8),
                ColorHex = "#2563EB", Icon = "fuel"
            },
            new()
            {
                Code = "SOS150",
                TitleMk = "Експрес достава: -150 ден на доплатата",
                TitleEn = "Express delivery: 150 MKD off the surcharge",
                TitleSq = "Dërgesë ekspres: 150 den zbritje në shtesën",
                DescriptionMk = "Попуст на доплатата за експрес достава на пат, еднаш месечно по корисник.",
                DescriptionEn = "Discount on the roadside express surcharge, once a month per customer.",
                DescriptionSq = "Zbritje në shtesën për dërgesë ekspres në rrugë, një herë në muaj për klient.",
                DiscountType = DiscountType.FixedAmount,
                Value = 150m,
                DayOfWeekMask = everyDay, CooldownDays = 30, MaxUsesPerUser = 0,
                EmergencyOnly = true,
                StartsAtUtc = now.AddMonths(-6), EndsAtUtc = now.AddMonths(12),
                ColorHex = "#DC2626", Icon = "alert"
            }
        };
    }

    private static async Task SeedFleetAsync(ApplicationDbContext db)
    {
        if (await db.DeliveryVehicles.AnyAsync()) return;

        db.DeliveryVehicles.AddRange(SeedData.Fleet.Select(f => new DeliveryVehicle
        {
            Code = f.Code,
            DriverName = f.Driver,
            Phone = f.Phone,
            DriverPhotoUrl = f.Photo,
            DriverRating = f.Rating,
            CompletedDeliveries = f.Deliveries,
            YearsOfExperience = f.Years,
            TruckModel = f.Truck,
            PlateNumber = f.Plate,
            CapacityLitres = f.Capacity,
            BaseLatitude = f.Lat,
            BaseLongitude = f.Lon,
            BaseCity = f.City,
            CanServeBoats = f.Boats,
            CanServeConstruction = f.Construction,
            IsAvailable = true
        }));
        await db.SaveChangesAsync();
    }

    private static async Task SeedConditionsAsync(ApplicationDbContext db)
    {
        if (!await db.BorderCrossings.AnyAsync())
        {
            db.BorderCrossings.AddRange(SeedData.Borders.Select(b => new BorderCrossing
            {
                Name = b.Name,
                Country = b.Country,
                Latitude = b.Lat,
                Longitude = b.Lon,
                EntryWaitMinutes = b.Entry,
                ExitWaitMinutes = b.Exit,
                ReportedAtUtc = DateTime.UtcNow
            }));
            await db.SaveChangesAsync();
        }

        if (!await db.RoadConditions.AnyAsync())
        {
            db.RoadConditions.AddRange(SeedData.Roads.Select(r => new RoadCondition
            {
                Road = r.Road,
                Region = r.Region,
                Status = r.Status,
                Description = r.Description,
                Latitude = r.Lat,
                Longitude = r.Lon,
                Source = "AMSM",
                ReportedAtUtc = DateTime.UtcNow
            }));
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedUsersAsync(
        ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        ReFuelOptions options, ILogger logger)
    {
        var petrol = await db.FuelTypes.FirstAsync(f => f.Code == "EUROSUPER_95");
        var diesel = await db.FuelTypes.FirstAsync(f => f.Code == "EURODIESEL");

        await EnsureUserAsync(userManager, options.AdminEmail, options.AdminPassword, "Администратор ReFuel",
            RoleAdmin, "Скопје", 41.9981, 21.4254, petrol.Id, LoyaltyTier.Platinum, logger);

        var demo = await EnsureUserAsync(userManager, options.DemoEmail, options.DemoPassword, "Петар Стојановски",
            RoleCustomer, "Скопје", 41.9981, 21.4254, diesel.Id, LoyaltyTier.Gold, logger);

        var extra = new (string Email, string Name, string City, double Lat, double Lon, LoyaltyTier Tier)[]
        {
            ("ana@refuel.mk",     "Ана Николовска",     "Битола", 41.0314, 21.3347, LoyaltyTier.Silver),
            ("besnik@refuel.mk",  "Бесник Ајдини",      "Тетово", 42.0106, 20.9714, LoyaltyTier.Basic),
            ("elena@refuel.mk",   "Елена Трајкова",     "Охрид",  41.1231, 20.8016, LoyaltyTier.Silver),
            ("marko@refuel.mk",   "Марко Илиевски",     "Скопје", 42.0043, 21.4097, LoyaltyTier.Basic),
            ("ivana@refuel.mk",   "Ивана Крстевска",    "Скопје", 41.9860, 21.4620, LoyaltyTier.Gold),
            ("arben@refuel.mk",   "Арбен Реџепи",       "Скопје", 42.0180, 21.3900, LoyaltyTier.Basic),
            ("dragan@refuel.mk",  "Драган Петковски",   "Битола", 41.0250, 21.3450, LoyaltyTier.Silver),
            ("teuta@refuel.mk",   "Теута Зибери",       "Тетово", 42.0050, 20.9800, LoyaltyTier.Basic),
            ("bojan@refuel.mk",   "Бојан Спасовски",    "Охрид",  41.1180, 20.8100, LoyaltyTier.Basic),
            ("simona@refuel.mk",  "Симона Ристовска",   "Скопје", 41.9950, 21.4300, LoyaltyTier.Silver),
            ("filip@refuel.mk",   "Филип Јованов",      "Скопје", 42.0100, 21.4500, LoyaltyTier.Basic)
        };

        foreach (var e in extra)
        {
            await EnsureUserAsync(userManager, e.Email, options.DemoPassword, e.Name, RoleCustomer,
                e.City, e.Lat, e.Lon, petrol.Id, e.Tier, logger);
        }

        if (demo is not null && !await db.Vehicles.AnyAsync(v => v.UserId == demo.Id))
        {
            db.Vehicles.AddRange(
                new Vehicle
                {
                    UserId = demo.Id, Make = "Volkswagen", Model = "Passat B8", Year = 2019,
                    PlateNumber = "SK-1234-AB", FuelTypeId = diesel.Id,
                    TankCapacityLitres = 66, AvgConsumption = 5.8, IsDefault = true
                },
                new Vehicle
                {
                    UserId = demo.Id, Make = "Renault", Model = "Clio IV", Year = 2016,
                    PlateNumber = "SK-5678-CD", FuelTypeId = petrol.Id,
                    TankCapacityLitres = 45, AvgConsumption = 6.4, IsDefault = false
                });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser?> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email, string password, string fullName, string role,
        string city, double lat, double lon, int fuelTypeId, LoyaltyTier tier,
        ILogger logger)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null) return user;

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            City = city,
            HomeLatitude = lat,
            HomeLongitude = lon,
            PreferredFuelTypeId = fuelTypeId,
            Tier = tier,
            PhoneNumber = "+389 7" + (email.Sum(c => c) * 7919 % 9000000 + 1000000),
            LoyaltyPoints = tier switch
            {
                LoyaltyTier.Platinum => 4200,
                LoyaltyTier.Gold => 2100,
                LoyaltyTier.Silver => 780,
                _ => 120
            },
            DefaultAddress = $"{city}, Северна Македонија",
            PreferredLanguage = "mk"
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogWarning("Could not create user {Email}: {Errors}", email,
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return null;
        }

        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    /// <summary>Roughly how many retail orders a day the demo history assumes — a first-year retail channel, not a fantasy.</summary>
    private const int DemoOrdersPerDay = 6;

    private static async Task SeedOrdersAsync(ApplicationDbContext db, ReFuelOptions options, ILogger logger)
    {
        if (await db.Orders.AnyAsync()) return;
        await GenerateDemoOrdersAsync(db, options, logger, DemoHistoryDays * DemoOrdersPerDay, DemoHistoryDays);
    }

    /// <summary>
    /// Creates `count` retail demo orders spread over the last `days` days for the seeded demo
    /// customers only (never for accounts a person registered). Priced at the pump — the retail
    /// price the customer sees — with the individual tier's delivery rules; the economics are
    /// filled in by ApplyPumpParityEconomics. Used by the fresh seed and by the upgrade top-up.
    /// </summary>
    private static async Task GenerateDemoOrdersAsync(ApplicationDbContext db, ReFuelOptions options, ILogger logger, int count, int days)
    {
        if (count <= 0) return;

        var rnd = new Random(20260811 + count);

        var users = await db.Users
            .Where(u => u.Email != null && u.Email.EndsWith("@refuel.mk")
                        && u.Email != options.AdminEmail && u.Email != options.BusinessEmail
                        && u.BusinessAccountId == null)
            .ToListAsync();
        if (users.Count == 0) return;

        var fleet = await db.DeliveryVehicles.ToListAsync();
        var fuels = await db.FuelTypes.Where(f => f.IsDeliverable).ToListAsync();

        var cities = users.Select(u => u.City).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var stations = await db.Stations
            .Where(s => s.IsActive && cities.Contains(s.City))
            .Take(160).ToListAsync();

        if (stations.Count == 0)
            stations = await db.Stations.Where(s => s.IsActive).Take(80).ToListAsync();

        if (stations.Count == 0 || fuels.Count == 0) return;

        var priceLookup = await db.StationPrices
            .Where(p => p.IsCurrent)
            .Select(p => new { p.StationId, p.FuelTypeId, p.Price })
            .ToListAsync();

        var settings = await db.PricingSettings.OrderBy(x => x.Id).FirstOrDefaultAsync() ?? new PricingSettings();
        var individual = await db.PricingTiers.FirstOrDefaultAsync(t => t.CustomerType == CustomerType.Individual);

        var seq = NextSequence(await db.Orders.Select(o => o.Reference).ToListAsync(), "RF-2026-");
        var orders = new List<Order>();

        for (var i = 0; i < count; i++)
        {
            var user = users[rnd.Next(users.Count)];

            // A customer orders near where they live; pick a station in their city when there is one.
            var local = stations.Where(s => s.City == user.City).ToList();
            var station = (local.Count > 0 ? local : stations)[rnd.Next(local.Count > 0 ? local.Count : stations.Count)];
            var fuel = fuels[rnd.Next(fuels.Count)];
            var litres = Math.Round(rnd.Next(20, 66) + rnd.NextDouble(), 1);

            var price = priceLookup.FirstOrDefault(p => p.StationId == station.Id && p.FuelTypeId == fuel.Id)?.Price
                        ?? priceLookup.FirstOrDefault(p => p.FuelTypeId == fuel.Id)?.Price
                        ?? 78m;

            var createdAt = DateTime.UtcNow.AddDays(-rnd.NextDouble() * days).AddHours(-rnd.Next(0, 24));
            var type = rnd.NextDouble() switch
            {
                < 0.10 => OrderType.Emergency,
                < 0.45 => OrderType.Scheduled,
                _ => OrderType.Standard
            };

            var distance = Math.Round(2 + rnd.NextDouble() * 14, 1);
            var subtotal = Math.Round(price * (decimal)litres, 2);

            var minFee = individual?.MinDeliveryFee ?? 150m;
            var perKm = individual?.DeliveryFeePerKm ?? 18m;
            var deliveryFee = Math.Round(minFee + perKm * (decimal)distance, 0);

            if (type == OrderType.Scheduled)
            {
                var pct = individual?.ScheduledDeliveryDiscountPercent ?? 40;
                deliveryFee = Math.Round(deliveryFee * (100 - pct) / 100m, 0);
                if (individual is not null && individual.FreeDeliveryThreshold > 0 && subtotal >= individual.FreeDeliveryThreshold)
                    deliveryFee = 0;
            }

            var surcharge = type == OrderType.Emergency ? (individual?.EmergencySurcharge ?? 350m) : 0m;

            // Promotions are on the delivery, never on the litre: now and then the fee is waived.
            var discount = rnd.NextDouble() < 0.12 ? deliveryFee : 0m;
            var total = subtotal + deliveryFee + surcharge - discount;

            var status = createdAt < DateTime.UtcNow.AddDays(-2)
                ? (rnd.NextDouble() < 0.06 ? OrderStatus.Cancelled : OrderStatus.Delivered)
                : (OrderStatus)rnd.Next(0, 4);

            var truck = fleet.Count == 0 ? null : NearestTanker(fleet, station.Latitude, station.Longitude);

            var order = new Order
            {
                Reference = $"RF-2026-{seq++:D6}",
                UserId = user.Id,
                StationId = station.Id,
                FuelTypeId = fuel.Id,
                DeliveryVehicleId = status is OrderStatus.Pending or OrderStatus.Confirmed ? null : truck?.Id,
                Litres = litres,
                UnitPrice = price,
                FuelSubtotal = subtotal,
                DeliveryFee = deliveryFee,
                EmergencySurcharge = surcharge,
                DiscountAmount = discount,
                Total = total,
                Type = type,
                Status = status,
                PaymentMethod = (PaymentMethod)rnd.Next(0, 3),
                DeliveryAddress = station.Address ?? station.City,
                DeliveryCity = station.City,
                DeliveryLatitude = Math.Round(station.Latitude + (rnd.NextDouble() - 0.5) * 0.05, 6),
                DeliveryLongitude = Math.Round(station.Longitude + (rnd.NextDouble() - 0.5) * 0.05, 6),
                ContactPhone = user.PhoneNumber ?? "+389 70 000 000",
                DistanceKm = distance,
                CreatedAtUtc = createdAt,
                DispatchedAtUtc = status is OrderStatus.OnTheWay or OrderStatus.Arrived or OrderStatus.Delivered
                    ? createdAt.AddMinutes(rnd.Next(10, 40)) : null,
                EtaUtc = createdAt.AddMinutes(rnd.Next(45, 150)),
                DeliveredAtUtc = status == OrderStatus.Delivered ? createdAt.AddMinutes(rnd.Next(35, 180)) : null,
                CancelledAtUtc = status == OrderStatus.Cancelled ? createdAt.AddMinutes(rnd.Next(5, 60)) : null,
                PointsEarned = status == OrderStatus.Delivered ? (int)(total / 100m) * 5 : 0,
                ScheduledForUtc = type == OrderType.Scheduled ? createdAt.AddDays(1) : null,
                PricingTierId = individual?.Id
            };

            ApplyPumpParityEconomics(order, settings, individual);
            NormalisePriority(order);
            orders.Add(order);
        }

        db.Orders.AddRange(orders);
        await db.SaveChangesAsync();

        var history = new List<OrderStatusHistory>();
        foreach (var o in orders)
        {
            history.Add(new OrderStatusHistory { OrderId = o.Id, Status = OrderStatus.Pending, CreatedAtUtc = o.CreatedAtUtc, Note = "order.history.created" });

            if (o.Status is OrderStatus.Delivered or OrderStatus.Arrived or OrderStatus.OnTheWay or OrderStatus.Assigned or OrderStatus.Confirmed)
                history.Add(new OrderStatusHistory { OrderId = o.Id, Status = OrderStatus.Confirmed, CreatedAtUtc = o.CreatedAtUtc.AddMinutes(4) });

            if (o.Status is OrderStatus.Delivered or OrderStatus.Arrived or OrderStatus.OnTheWay or OrderStatus.Assigned)
                history.Add(new OrderStatusHistory { OrderId = o.Id, Status = OrderStatus.Assigned, CreatedAtUtc = o.CreatedAtUtc.AddMinutes(11) });

            if (o.Status is OrderStatus.Delivered or OrderStatus.Arrived or OrderStatus.OnTheWay)
                history.Add(new OrderStatusHistory { OrderId = o.Id, Status = OrderStatus.OnTheWay, CreatedAtUtc = o.DispatchedAtUtc ?? o.CreatedAtUtc.AddMinutes(25) });

            if (o.Status is OrderStatus.Delivered)
                history.Add(new OrderStatusHistory { OrderId = o.Id, Status = OrderStatus.Delivered, CreatedAtUtc = o.DeliveredAtUtc ?? o.CreatedAtUtc.AddHours(1) });

            if (o.Status is OrderStatus.Cancelled)
                history.Add(new OrderStatusHistory { OrderId = o.Id, Status = OrderStatus.Cancelled, CreatedAtUtc = o.CancelledAtUtc ?? o.CreatedAtUtc.AddMinutes(20) });
        }

        db.OrderStatusHistories.AddRange(history);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} demo orders over the last {Days} days.", orders.Count, days);
    }

    private static async Task SeedBusinessAsync(
        ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        ReFuelOptions options, ILogger logger)
    {
        if (await db.BusinessAccounts.AnyAsync()) return;

        var rnd = NewRandom();
        var fuels = await db.FuelTypes.ToDictionaryAsync(f => f.Code, f => f);

        var businessUser = await EnsureUserAsync(userManager, options.BusinessEmail, options.BusinessPassword,
            "Горан Митревски", RoleBusiness, "Скопје", 41.9744, 21.5744,
            fuels["EURODIESEL"].Id, LoyaltyTier.Platinum, logger);

        var contractsCreated = new List<SupplyContract>();

        foreach (var seed in SeedData.Businesses)
        {
            var account = new BusinessAccount
            {
                CompanyName = seed.Company,
                TaxNumber = seed.Tax,
                RegistrationNumber = rnd.Next(6000000, 7999999).ToString(),
                Address = seed.Address,
                City = seed.City,
                ContactPerson = seed.Contact,
                ContactEmail = seed.Email,
                ContactPhone = seed.Phone,
                Industry = seed.Industry,
                OwnerUserId = seed.Contract == "RF-C-2026-001" ? businessUser?.Id : null,
                PaymentTermDays = 30,
                CreditLimit = (decimal)seed.MonthlyLitres * 100m
            };

            db.BusinessAccounts.Add(account);
            await db.SaveChangesAsync();

            if (account.OwnerUserId is not null && businessUser is not null && businessUser.BusinessAccountId != account.Id)
            {
                businessUser.BusinessAccountId = account.Id;
                businessUser.CustomerType = CustomerType.Fleet;
                await userManager.UpdateAsync(businessUser);
            }

            var contract = new SupplyContract
            {
                ContractNumber = seed.Contract,
                BusinessAccountId = account.Id,
                Title = seed.Title,
                FuelTypeId = fuels[seed.FuelCode].Id,
                StartsOn = new DateTime(2026, 1, 1),
                EndsOn = new DateTime(2026, 12, 31),
                Status = ContractStatus.Active,
                Frequency = seed.Frequency,
                CommittedLitresPerMonth = seed.MonthlyLitres,
                MinLitresPerDelivery = 200,
                DiscountPerLitre = seed.DiscountPerLitre,
                FixedDeliveryFee = 0m,
                PriceLinkedToErc = true,
                DefaultAddress = seed.Address,
                DefaultCity = seed.City,
                DefaultLatitude = seed.Lat,
                DefaultLongitude = seed.Lon,
                Notes = "Годишен договор за снабдување со гориво со фиксен попуст по литар врз основа на цената на РКЕ."
            };

            db.SupplyContracts.Add(contract);
            await db.SaveChangesAsync();
            contractsCreated.Add(contract);

            var assets = SeedData.Assets.Where(a => a.Contract == seed.Contract).Select(a => new ContractAsset
            {
                SupplyContractId = contract.Id,
                Name = a.Name,
                Kind = a.Kind,
                Identifier = a.Identifier,
                TankCapacityLitres = a.Capacity,
                ParkingLocation = seed.Address,
                Latitude = a.Lat,
                Longitude = a.Lon,
                IsActive = true
            }).ToList();

            db.ContractAssets.AddRange(assets);
            await db.SaveChangesAsync();
        }

        await SeedContractHistoryAsync(db, contractsCreated, DemoHistoryDays, logger);

        logger.LogInformation("Seeded {Contracts} supply contracts with deliveries, proofs and invoices.", contractsCreated.Count);
    }

    /// <summary>How far back the demo history reaches. Long enough for a 90-day profitability view.</summary>
    private const int DemoHistoryDays = 92;

    /// <summary>The tanker whose base is closest to a site — what a dispatcher would send.</summary>
    private static DeliveryVehicle? NearestTanker(IEnumerable<DeliveryVehicle> fleet, double lat, double lon, bool boats = false)
    {
        var candidates = fleet.Where(v => !boats || v.CanServeBoats).ToList();
        if (candidates.Count == 0) candidates = fleet.ToList();

        return candidates
            .OrderBy(v => GeoUtils.HaversineKm(v.BaseLatitude, v.BaseLongitude, lat, lon))
            .FirstOrDefault();
    }

    /// <summary>Next free number for references like RF-D-2026-00042, so re-seeding never collides with rows that stay.</summary>
    private static int NextSequence(IEnumerable<string> existing, string prefix)
    {
        var max = 0;
        foreach (var reference in existing)
        {
            if (!reference.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(reference[prefix.Length..], out var n) && n > max) max = n;
        }
        return max + 1;
    }

    /// <summary>
    /// Builds the delivery history of the given contracts: for every step of the contract's rhythm
    /// (nightly, weekly, ...) as many tanker visits as the committed monthly volume needs, sized to
    /// the tanker that is based nearest to the site, spread over the step so a weekly contract is
    /// served on several days rather than in one impossible night. Past visits are delivered with a
    /// small metering variance and get a proof of delivery; each calendar month gets an invoice.
    /// Used by the fresh seed and by the one-time upgrade that rebuilds the demo history.
    /// </summary>
    private static async Task SeedContractHistoryAsync(ApplicationDbContext db, List<SupplyContract> contracts, int days, ILogger logger)
    {
        if (contracts.Count == 0) return;

        var rnd = NewRandom();
        var fleet = await db.DeliveryVehicles.ToListAsync();

        // The whole national price series, so every past drop is priced at the pump price of its day.
        var fuelIds = contracts.Select(c => c.FuelTypeId).Distinct().ToList();
        var priceSeries = (await db.NationalPrices
                .Where(p => fuelIds.Contains(p.FuelTypeId))
                .OrderBy(p => p.EffectiveFrom)
                .Select(p => new { p.FuelTypeId, p.EffectiveFrom, p.Price })
                .ToListAsync())
            .GroupBy(p => p.FuelTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.EffectiveFrom, x.Price)).ToList());

        decimal PumpPriceOn(int fuelId, DateTime when)
        {
            if (!priceSeries.TryGetValue(fuelId, out var series) || series.Count == 0) return 78.5m;
            var hit = series.LastOrDefault(x => x.EffectiveFrom <= when);
            return hit == default ? series[0].Price : hit.Price;
        }

        var deliverySeq = NextSequence(await db.ContractDeliveries.Select(d => d.Reference).ToListAsync(), "RF-D-2026-");
        var proofSeq = NextSequence(await db.DeliveryProofs.Select(x => x.ProofNumber).ToListAsync(), "POD-2026-");
        var invoiceSeq = NextSequence(await db.Invoices.Select(i => i.InvoiceNumber).ToListAsync(), "RF-INV-2026-");

        var totalDeliveries = 0;

        foreach (var contract in contracts)
        {
            var assets = await db.ContractAssets.Where(a => a.SupplyContractId == contract.Id).ToListAsync();
            if (assets.Count == 0) continue;

            var stepDays = contract.Frequency switch
            {
                DeliveryFrequency.Nightly => 1,
                DeliveryFrequency.Weekly => 7,
                DeliveryFrequency.Biweekly => 14,
                DeliveryFrequency.Monthly => 30,
                _ => 10
            };

            var boats = assets.Any(a => a.Kind == AssetKind.Boat);
            var truck = NearestTanker(fleet, contract.DefaultLatitude, contract.DefaultLongitude, boats);
            var capacity = truck?.CapacityLitres is > 0 ? truck.CapacityLitres : 1200;

            // Litres the contract needs per step, delivered in as many tanker loads as it takes.
            var perStep = contract.CommittedLitresPerMonth / (30.0 / stepDays);
            var visits = Math.Max(1, (int)Math.Ceiling(perStep / capacity));
            var perVisit = Math.Round(perStep / visits, 0);
            perVisit = Math.Clamp(perVisit, contract.MinLitresPerDelivery, Math.Max(contract.MinLitresPerDelivery, capacity));

            var pastSteps = Math.Max(1, days / stepDays);
            var deliveries = new List<ContractDelivery>();

            for (var i = pastSteps; i >= -3; i--)
            {
                var stepDate = DateTime.UtcNow.Date.AddDays(-i * stepDays);

                for (var v = 0; v < visits; v++)
                {
                    // Nightly: the loads follow each other through the night. Weekly and slower:
                    // the loads are spread across the days of the step.
                    var scheduled = stepDays == 1
                        ? stepDate.Add(contract.DeliveryWindowStart).AddMinutes(v * 50)
                        : stepDate.AddDays(Math.Floor(v * stepDays / (double)visits)).Add(contract.DeliveryWindowStart);

                    var asset = assets[(i * visits + v + assets.Count * 1000) % assets.Count];
                    var isPast = scheduled < DateTime.UtcNow;
                    var delivered = isPast ? Math.Round(perVisit * (0.94 + rnd.NextDouble() * 0.1), 1) : 0;

                    var pumpPrice = PumpPriceOn(contract.FuelTypeId, scheduled);
                    var netPrice = Math.Round(Math.Max(1m, pumpPrice - contract.DiscountPerLitre), 2);

                    deliveries.Add(new ContractDelivery
                    {
                        Reference = $"RF-D-2026-{deliverySeq++:D5}",
                        SupplyContractId = contract.Id,
                        ContractAssetId = asset.Id,
                        DeliveryVehicleId = truck?.Id,
                        ScheduledForUtc = scheduled,
                        CompletedAtUtc = isPast ? scheduled.AddMinutes(rnd.Next(20, 60)) : null,
                        Status = isPast ? OrderStatus.Delivered : OrderStatus.Confirmed,
                        PlannedLitres = perVisit,
                        DeliveredLitres = delivered,
                        ReferencePricePerLitre = pumpPrice,
                        UnitPrice = netPrice,
                        LineTotal = Math.Round(netPrice * (decimal)delivered, 2)
                    });
                }
            }

            db.ContractDeliveries.AddRange(deliveries);
            await db.SaveChangesAsync();
            totalDeliveries += deliveries.Count;

            var meter = Math.Round(100000 + rnd.NextDouble() * 50000, 1);

            foreach (var delivery in deliveries.Where(d => d.Status == OrderStatus.Delivered).OrderBy(d => d.ScheduledForUtc))
            {
                var meterStart = meter;
                meter = Math.Round(meter + delivery.DeliveredLitres, 1);

                var proof = new DeliveryProof
                {
                    ProofNumber = $"POD-2026-{proofSeq++:D6}",
                    ContractDeliveryId = delivery.Id,
                    IssuedAtUtc = delivery.CompletedAtUtc ?? delivery.ScheduledForUtc,
                    LitresDelivered = delivery.DeliveredLitres,
                    MeterStart = meterStart,
                    MeterEnd = meter,
                    DriverName = truck?.DriverName ?? "ReFuel возач",
                    ReceivedBy = "Одговорно лице на локација",
                    SignatureData = "signed-on-device",
                    Latitude = contract.DefaultLatitude,
                    Longitude = contract.DefaultLongitude,
                    Remarks = "Испораката е извршена во договорениот термин."
                };

                proof.VerificationHash = BusinessHash.Compute(proof.ProofNumber, delivery.Reference,
                    proof.LitresDelivered, proof.IssuedAtUtc);

                db.DeliveryProofs.Add(proof);
            }

            await db.SaveChangesAsync();

            var months = deliveries.Where(d => d.Status == OrderStatus.Delivered)
                .GroupBy(d => new { d.ScheduledForUtc.Year, d.ScheduledForUtc.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .ToList();

            foreach (var month in months)
            {
                var periodStart = new DateTime(month.Key.Year, month.Key.Month, 1);
                var periodEnd = periodStart.AddMonths(1).AddDays(-1);
                var closed = periodEnd < DateTime.UtcNow.Date;

                var invoice = new Invoice
                {
                    InvoiceNumber = $"RF-INV-2026-{invoiceSeq++:D5}",
                    BusinessAccountId = contract.BusinessAccountId,
                    SupplyContractId = contract.Id,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    IssuedOn = closed ? periodEnd : DateTime.UtcNow.Date,
                    DueOn = (closed ? periodEnd : DateTime.UtcNow.Date).AddDays(30),
                    VatRate = 18m,
                    Status = periodEnd.AddDays(30) < DateTime.UtcNow.Date ? InvoiceStatus.Paid : InvoiceStatus.Issued,
                    PaidOn = periodEnd.AddDays(30) < DateTime.UtcNow.Date ? periodEnd.AddDays(rnd.Next(10, 29)) : null
                };

                db.Invoices.Add(invoice);
                await db.SaveChangesAsync();

                var deliveryIds = month.Select(m => m.Id).ToList();
                var proofs = await db.DeliveryProofs
                    .Where(x => deliveryIds.Contains(x.ContractDeliveryId))
                    .ToListAsync();

                foreach (var delivery in month)
                {
                    var proof = proofs.FirstOrDefault(x => x.ContractDeliveryId == delivery.Id);
                    db.InvoiceLines.Add(new InvoiceLine
                    {
                        InvoiceId = invoice.Id,
                        Description = $"{contract.Title} · {delivery.Reference}",
                        DeliveredOn = delivery.ScheduledForUtc.Date,
                        Litres = delivery.DeliveredLitres,
                        UnitPrice = delivery.UnitPrice,
                        Discount = Math.Round(delivery.DiscountPerLitre * (decimal)delivery.DeliveredLitres, 2),
                        LineTotal = delivery.LineTotal,
                        ProofNumber = proof?.ProofNumber
                    });

                    delivery.InvoiceId = invoice.Id;
                }

                await db.SaveChangesAsync();

                invoice.Subtotal = Math.Round(month.Sum(d => d.LineTotal), 2);
                invoice.VatAmount = Math.Round(invoice.Subtotal * invoice.VatRate / 100m, 2);
                invoice.Total = invoice.Subtotal + invoice.VatAmount;
                await db.SaveChangesAsync();
            }
        }

        logger.LogInformation("Seeded {Contracts} supply contracts with {Deliveries} deliveries, proofs and invoices over {Days} days.",
            contracts.Count, totalDeliveries, days);
    }

    private static async Task SeedFleetOperationsAsync(ApplicationDbContext db, ILogger logger)
    {
        var accounts = await db.BusinessAccounts.ToListAsync();
        if (accounts.Count == 0) return;

        var rnd = NewRandom();
        var fuels = await db.FuelTypes.ToDictionaryAsync(f => f.Code, f => f.Id);
        var tiers = await db.PricingTiers.ToListAsync();

        foreach (var account in accounts)
        {
            if (account.PricingTierId is not null) continue;

            account.CustomerType = account.Industry switch
            {
                "Градежништво" => CustomerType.Construction,
                "Земјоделство" => CustomerType.SmallBusiness,
                "Наутика и туризам" => CustomerType.SmallBusiness,
                _ => CustomerType.Fleet
            };

            var tier = tiers.FirstOrDefault(t => t.CustomerType == account.CustomerType);
            account.PricingTierId = tier?.Id;
        }

        await db.SaveChangesAsync();

        if (!await db.ConstructionSites.AnyAsync())
        {
            foreach (var seed in SeedData.Sites)
            {
                var account = accounts.FirstOrDefault(a => a.CompanyName == seed.Company);
                if (account is null) continue;

                db.ConstructionSites.Add(new ConstructionSite
                {
                    BusinessAccountId = account.Id,
                    Name = seed.Name,
                    Address = seed.Address,
                    City = seed.City,
                    Latitude = seed.Lat,
                    Longitude = seed.Lon,
                    PrimaryFuelTypeId = fuels.TryGetValue("EURODIESEL", out var diesel) ? diesel : null,
                    WeeklyConsumptionLitres = seed.WeeklyLitres,
                    ContactPerson = seed.Contact,
                    ContactPhone = seed.Phone,
                    AccessFrom = new TimeSpan(seed.AccessFrom, 0, 0),
                    AccessTo = new TimeSpan(seed.AccessTo, 0, 0),
                    Notes = seed.Note,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync();

            var sites = await db.ConstructionSites.ToListAsync();
            var machines = await db.ContractAssets
                .Where(a => a.Kind == AssetKind.ConstructionMachine || a.Kind == AssetKind.Generator)
                .ToListAsync();

            for (var i = 0; i < machines.Count && sites.Count > 0; i++)
            {
                machines[i].ConstructionSiteId = sites[i % sites.Count].Id;
                machines[i].AverageLitresPerWeek = Math.Round(220 + rnd.NextDouble() * 700, 0);
                machines[i].CurrentLevelPercent = Math.Round(18 + rnd.NextDouble() * 62, 0);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} construction sites.", SeedData.Sites.Length);
        }

        if (!await db.DeliverySchedules.AnyAsync())
        {
            var contracts = await db.SupplyContracts.ToListAsync();
            var sites = await db.ConstructionSites.ToListAsync();

            foreach (var seed in SeedData.Schedules)
            {
                var account = accounts.FirstOrDefault(a => a.CompanyName == seed.Company);
                if (account is null) continue;
                if (!fuels.TryGetValue(seed.FuelCode, out var fuelId)) continue;

                db.DeliverySchedules.Add(new DeliverySchedule
                {
                    Title = seed.Title,
                    Kind = seed.Kind,
                    Frequency = seed.Frequency,
                    BusinessAccountId = account.Id,
                    SupplyContractId = contracts.FirstOrDefault(c => c.BusinessAccountId == account.Id)?.Id,
                    ConstructionSiteId = sites.FirstOrDefault(s => s.BusinessAccountId == account.Id && s.City == seed.City)?.Id,
                    FuelTypeId = fuelId,
                    DaysOfWeek = seed.Days,
                    WindowStart = new TimeSpan(seed.WindowStart, 0, 0),
                    WindowEnd = new TimeSpan(seed.WindowEnd, 0, 0),
                    LitresPerVisit = seed.Litres,
                    Address = seed.Address,
                    City = seed.City,
                    Latitude = seed.Lat,
                    Longitude = seed.Lon,
                    StartsOn = new DateTime(2026, 1, 1),
                    EndsOn = new DateTime(2026, 12, 31),
                    TriggerBelowPercent = seed.TriggerPercent,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} delivery schedules.", SeedData.Schedules.Length);
        }

        if (!await db.FuelAnomalies.AnyAsync())
        {
            var deliveries = await db.ContractDeliveries
                .Include(d => d.SupplyContract)
                .Where(d => d.Status == OrderStatus.Delivered && d.DeliveredLitres > 0)
                .OrderByDescending(d => d.ScheduledForUtc)
                .Take(60)
                .ToListAsync();

            var candidates = deliveries
                .Where(d => Math.Abs(d.DeliveredLitres - d.PlannedLitres) / Math.Max(1, d.PlannedLitres) > 0.04)
                .Take(4)
                .ToList();

            foreach (var delivery in candidates)
            {
                var variance = delivery.DeliveredLitres - delivery.PlannedLitres;

                db.FuelAnomalies.Add(new FuelAnomaly
                {
                    DetectedAtUtc = (delivery.CompletedAtUtc ?? delivery.ScheduledForUtc).AddMinutes(6),
                    Kind = AnomalyKind.VolumeVariance,
                    Severity = Math.Abs(variance) > 60 ? AnomalySeverity.Attention : AnomalySeverity.Information,
                    Status = AnomalyStatus.Open,
                    DeliveryVehicleId = delivery.DeliveryVehicleId,
                    ContractDeliveryId = delivery.Id,
                    BusinessAccountId = delivery.SupplyContract?.BusinessAccountId,
                    ExpectedLitres = delivery.PlannedLitres,
                    ObservedLitres = delivery.DeliveredLitres,
                    Subject = "Отстапување помеѓу планирана и испорачана количина",
                    Detail = "Системот забележа отстапување во оваа испорака. Ова е известување за проверка, не обвинување — " +
                             "разликата најчесто произлегува од температурна корекција, делумно полн резервоар или ракување на терен."
                });
            }

            await db.SaveChangesAsync();
        }

        if (!await db.BusinessOffers.AnyAsync())
        {
            var settings = await db.PricingSettings.FirstOrDefaultAsync();
            var dieselId = fuels.TryGetValue("EURODIESEL", out var diesel) ? diesel : fuels.Values.First();

            var latestDiesel = await db.NationalPrices
                .Where(p => p.FuelTypeId == dieselId)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync();

            var pumpPrice = latestDiesel?.Price ?? 78.5m;
            var costing = settings ?? new PricingSettings();

            // The offer's cost basis is the wholesale price: pump minus the procurement spread.
            var basePrice = Math.Round(Math.Max(0, pumpPrice - costing.ProcurementSpreadPerLitre), 2);

            var drafts = new[]
            {
                new { Company = "Пекара Жито Караорман ДООЕЛ", Contact = "Марија Ристеска", Email = "m.risteska@zito.mk",
                      Phone = "+389 46 780 220", City = "Струга", Type = CustomerType.SmallBusiness,
                      Litres = 2600d, Deliveries = 8, Distance = 9d, Margin = 3.50m, Discount = 0.30m, Fee = 160m },
                new { Company = "Транспорт Вардар ДОО", Contact = "Зоран Ефтимов", Email = "z.eftimov@vardartrans.mk",
                      Phone = "+389 43 231 640", City = "Велес", Type = CustomerType.Fleet,
                      Litres = 38000d, Deliveries = 26, Distance = 16d, Margin = 2.50m, Discount = 0.20m, Fee = 0m },
                new { Company = "Бетон Инженеринг Прилеп АД", Contact = "Влатко Дамјановски", Email = "v.damjanovski@betonpp.mk",
                      Phone = "+389 48 412 900", City = "Прилеп", Type = CustomerType.Construction,
                      Litres = 14500d, Deliveries = 14, Distance = 22d, Margin = 3.00m, Discount = 0.20m, Fee = 250m }
            };

            var sequence = 1;

            foreach (var draft in drafts)
            {
                var litres = (decimal)draft.Litres;
                var deliveriesPerMonth = (decimal)draft.Deliveries;

                var effectiveMargin = Math.Max(0, draft.Margin - draft.Discount);
                var grossFuelMargin = Math.Round(litres * effectiveMargin, 0);
                var deliveryRevenue = Math.Round(deliveriesPerMonth * draft.Fee, 0);

                // A contract drop is a dedicated trip — the same costing every other screen uses.
                var costPerDelivery = DeliveryCosting.DedicatedTrip(costing, draft.Distance).Total;

                var deliveryCost = Math.Round(deliveriesPerMonth * costPerDelivery, 0);

                db.BusinessOffers.Add(new BusinessOffer
                {
                    OfferNumber = $"RF-OFF-2026-{sequence++:D4}",
                    CompanyName = draft.Company,
                    ContactPerson = draft.Contact,
                    ContactEmail = draft.Email,
                    ContactPhone = draft.Phone,
                    City = draft.City,
                    CustomerType = draft.Type,
                    FuelTypeId = dieselId,
                    EstimatedLitresPerMonth = draft.Litres,
                    DeliveriesPerMonth = draft.Deliveries,
                    AverageDistanceKm = draft.Distance,
                    BaseCostPerLitre = basePrice,
                    ProposedMarginPerLitre = draft.Margin,
                    VolumeDiscountPerLitre = draft.Discount,
                    ProposedDeliveryFee = draft.Fee,
                    EstimatedFuelCost = Math.Round(litres * basePrice, 0),
                    EstimatedMonthlyRevenue = Math.Round(litres * (basePrice + effectiveMargin) + deliveryRevenue, 0),
                    EstimatedGrossFuelMargin = grossFuelMargin,
                    EstimatedDeliveryCost = deliveryCost,
                    EstimatedGrossContribution = grossFuelMargin + deliveryRevenue - deliveryCost,
                    Status = OfferStatus.Draft,
                    Notes = "Проценка подготвена од продажниот тим. Бројките се проценка, не понуда со фиксна гаранција.",
                    ValidUntil = DateTime.UtcNow.Date.AddDays(30)
                });
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded the sample fleet proposals.");
        }
    }

    private static async Task BackfillOrderEconomicsAsync(ApplicationDbContext db, ILogger logger)
    {
        // Anything that never went through the v2 quote (no pump price recorded) gets its economics
        // filled in here; ApplyPumpParityEconomics is idempotent, so this is safe to run every start.
        var pending = await db.Orders
            .Where(o => o.UnitPrice > 0 && o.PumpPricePerLitre <= 0)
            .ToListAsync();

        if (pending.Count == 0) return;

        var settings = await db.PricingSettings.OrderBy(x => x.Id).FirstOrDefaultAsync() ?? new PricingSettings();
        var tiers = await db.PricingTiers.ToListAsync();
        var individual = tiers.FirstOrDefault(t => t.CustomerType == CustomerType.Individual);

        foreach (var order in pending)
        {
            ApplyPumpParityEconomics(order, settings, individual);
            NormalisePriority(order);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Filled in the pricing breakdown for {Count} historical orders.", pending.Count);
    }

    /// <summary>
    /// Historical demo orders were priced at the pump: the customer paid the station price. Under
    /// the pump-parity model that means ReFuel bought at pump − spread and kept the spread.
    /// </summary>
    private static void ApplyPumpParityEconomics(Order order, PricingSettings settings, PricingTier? tier)
    {
        var spread = Math.Max(0, settings.ProcurementSpreadPerLitre);
        var margin = tier is null ? spread : Math.Min(spread, Math.Max(0, tier.MarginPerLitre));

        order.PricingTierId ??= tier?.Id;
        order.PumpPricePerLitre = order.UnitPrice;
        order.BaseCostPerLitre = Math.Round(Math.Max(0, order.UnitPrice - spread), 2);
        order.MarginPerLitre = margin;
        order.VolumeDiscountPerLitre = 0;
        order.ContractDiscountPerLitre = 0;
        order.GrossFuelMargin = Math.Round(margin * (decimal)order.Litres, 2);
        order.DeliveryCost = DeliveryCosting.For(settings, order.DistanceKm, DeliveryCosting.IsGrouped(order.Type)).Total;
        order.GrossContribution = Math.Round(
            order.GrossFuelMargin + order.DeliveryFee + order.EmergencySurcharge - order.DiscountAmount - order.DeliveryCost, 2);
    }

    private static void NormalisePriority(Order order)
    {
        var expected = order.Type switch
        {
            OrderType.Emergency => DeliveryPriority.Emergency,
            OrderType.Scheduled => DeliveryPriority.Scheduled,
            OrderType.Contract => DeliveryPriority.ContractFleet,
            _ => DeliveryPriority.Standard
        };

        // Standard is the model default; Emergency (= 0) is what an added column defaults to.
        // Either one on a row whose type says otherwise means "never set".
        var unset = order.Priority == DeliveryPriority.Standard
                    || (order.Priority == DeliveryPriority.Emergency && order.Type != OrderType.Emergency);

        if (unset) order.Priority = expected;
    }

    /// <summary>
    /// One-time move of an existing database from pricing model v1 (markup above the pump price,
    /// global volume bands, taxi-style delivery cost) to v2 (wholesale spread, pump-price parity,
    /// per-tier bands, route-based cost). Runs once, stamps the version, and never touches
    /// accounts, users, stations or the order history itself — only how it is priced and reported.
    /// </summary>
    private static async Task UpgradePricingModelAsync(ApplicationDbContext db, ReFuelOptions options, ILogger logger)
    {
        var settings = await db.PricingSettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (settings is null || settings.PricingModelVersion >= PricingModelVersions.Current) return;

        var fromVersion = settings.PricingModelVersion;
        logger.LogInformation("Upgrading the pricing model from v{From} to v{To}.", fromVersion, PricingModelVersions.Current);

        if (fromVersion < 2) await UpgradeToV2Async(db, settings, logger);
        if (fromVersion < 3) await UpgradeToV3Async(db, options, logger);

        settings.PricingModelVersion = PricingModelVersions.Current;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedBy = "upgrade-v" + PricingModelVersions.Current;
        await db.SaveChangesAsync();
    }

    /// <summary>v1 → v2: wholesale spread, pump-price parity, per-tier bands, route-based cost.</summary>
    private static async Task UpgradeToV2Async(ApplicationDbContext db, PricingSettings settings, ILogger logger)
    {
        // 1. Settings — the new knobs arrive as 0 in an old database; give them the recommended values.
        if (settings.ProcurementSpreadPerLitre <= 0) settings.ProcurementSpreadPerLitre = SeedData.ProcurementSpreadPerLitre;
        if (settings.AverageStopsPerRoute <= 0) settings.AverageStopsPerRoute = SeedData.AverageStopsPerRoute;
        if (settings.InterStopKm <= 0) settings.InterStopKm = SeedData.InterStopKm;
        if (settings.ServiceMinutesPerStop <= 0) settings.ServiceMinutesPerStop = SeedData.ServiceMinutesPerStop;
        if (settings.ContractMinDropLitres <= 0) settings.ContractMinDropLitres = SeedData.ContractMinDropLitres;

        var spread = settings.ProcurementSpreadPerLitre;

        // 2. Tiers — margins are now measured above wholesale, so every seeded tier is retuned.
        var tiers = await db.PricingTiers.ToListAsync();
        foreach (var seed in SeedData.Tiers)
        {
            var tier = tiers.FirstOrDefault(t => t.Code == seed.Code);
            if (tier is null) continue;

            tier.MarginPerLitre = seed.Margin;
            tier.MinMarginPerLitre = seed.MinMargin;
            tier.FreeDeliveryThreshold = seed.FreeThreshold;
            tier.FreeDeliveryScheduledOnly = seed.FreeScheduledOnly;
            tier.ScheduledDeliveryDiscountPercent = seed.ScheduledDiscount;
            tier.MarketingMessageMk = seed.MarketingMk;
            tier.MarketingMessageEn = seed.MarketingEn;
            tier.MarketingMessageSq = seed.MarketingSq;
            tier.UpdatedAtUtc = DateTime.UtcNow;
        }

        // Tiers an administrator added by hand: give them a floor and keep them consistent.
        foreach (var tier in tiers.Where(t => SeedData.Tiers.All(seed => seed.Code != t.Code)))
        {
            if (tier.MarginPerLitre > spread) tier.MarginPerLitre = spread;
            if (tier.MinMarginPerLitre <= 0 || tier.MinMarginPerLitre > tier.MarginPerLitre)
                tier.MinMarginPerLitre = Math.Round(tier.MarginPerLitre * 0.6m, 2);
        }

        await db.SaveChangesAsync();

        // 3. Volume bands — the old global bands were sized for a 9-denar margin and wiped out
        //    the fleet tiers. Replace them with per-tier slices of the margin.
        var oldBands = await db.VolumeDiscountBands.ToListAsync();
        db.VolumeDiscountBands.RemoveRange(oldBands);
        db.VolumeDiscountBands.AddRange(BuildBands(tiers.ToDictionary(t => t.Code, t => t.Id)));
        await db.SaveChangesAsync();

        // 4. Contracts — DiscountPerLitre is the customer's total discount off the pump price and
        //    must leave the tier's minimum margin intact. Seeded contracts get their tuned values;
        //    anything else is capped.
        var contracts = await db.SupplyContracts.Include(c => c.BusinessAccount).ToListAsync();
        var oldDiscounts = contracts.ToDictionary(c => c.Id, c => c.DiscountPerLitre);
        var seededNumbers = new HashSet<string>(SeedData.Businesses.Select(b => b.Contract));

        foreach (var contract in contracts)
        {
            var seed = SeedData.Businesses.FirstOrDefault(b => b.Contract == contract.ContractNumber);
            var account = contract.BusinessAccount;

            var tier = tiers.FirstOrDefault(t => t.Id == account?.PricingTierId)
                       ?? tiers.FirstOrDefault(t => t.CustomerType == (account?.CustomerType ?? CustomerType.Fleet) && t.IsActive)
                       ?? tiers.FirstOrDefault(t => t.CustomerType == CustomerType.Fleet);

            var ceiling = Math.Max(0, spread - (tier?.MinMarginPerLitre ?? 0));

            if (seed is not null)
            {
                contract.DiscountPerLitre = Math.Min(seed.DiscountPerLitre, ceiling);
            }
            else if (contract.DiscountPerLitre > ceiling)
            {
                logger.LogWarning("Contract {Number}: discount {Old} ден/л capped to {New} ден/л by the {Tier} tier floor.",
                    contract.ContractNumber, contract.DiscountPerLitre, ceiling, tier?.Code ?? "-");
                contract.DiscountPerLitre = ceiling;
            }
        }

        await db.SaveChangesAsync();

        // 5. Contract deliveries — record the pump price each drop was priced against. The seeded
        //    demo deliveries (and their invoices) are re-priced at the new discount so the reports
        //    describe the model as it now stands; anything a user created keeps its price.
        var deliveries = await db.ContractDeliveries.Where(d => d.ReferencePricePerLitre <= 0).ToListAsync();
        var repricedContracts = new HashSet<int>();

        foreach (var delivery in deliveries)
        {
            var contract = contracts.FirstOrDefault(c => c.Id == delivery.SupplyContractId);
            if (contract is null) continue;

            var oldDiscount = oldDiscounts.GetValueOrDefault(contract.Id);
            delivery.ReferencePricePerLitre = Math.Round(delivery.UnitPrice + oldDiscount, 2);

            if (!seededNumbers.Contains(contract.ContractNumber)) continue;

            delivery.UnitPrice = Math.Round(Math.Max(1m, delivery.ReferencePricePerLitre - contract.DiscountPerLitre), 2);
            if (delivery.Status == OrderStatus.Delivered)
                delivery.LineTotal = Math.Round(delivery.UnitPrice * (decimal)delivery.DeliveredLitres, 2);

            repricedContracts.Add(contract.Id);
        }

        await db.SaveChangesAsync();

        if (repricedContracts.Count > 0)
        {
            var invoices = await db.Invoices
                .Include(i => i.Lines)
                .Where(i => i.SupplyContractId != null && repricedContracts.Contains(i.SupplyContractId.Value))
                .ToListAsync();

            var invoiceIds = invoices.Select(i => i.Id).ToList();
            var invoiced = await db.ContractDeliveries
                .Where(d => d.InvoiceId != null && invoiceIds.Contains(d.InvoiceId.Value))
                .ToListAsync();

            foreach (var invoice in invoices)
            {
                var lines = invoiced.Where(d => d.InvoiceId == invoice.Id).ToList();

                foreach (var line in invoice.Lines)
                {
                    var delivery = lines.FirstOrDefault(d => d.Reference == line.Description.Split(" · ").Last());
                    if (delivery is null) continue;

                    line.UnitPrice = delivery.UnitPrice;
                    line.Discount = Math.Round(delivery.DiscountPerLitre * (decimal)delivery.DeliveredLitres, 2);
                    line.LineTotal = delivery.LineTotal;
                }

                invoice.Subtotal = Math.Round(invoice.Lines.Sum(l => l.LineTotal), 2);
                invoice.VatAmount = Math.Round(invoice.Subtotal * invoice.VatRate / 100m, 2);
                invoice.Total = invoice.Subtotal + invoice.VatAmount;
            }

            await db.SaveChangesAsync();
        }

        // 6. Orders — the customer paid what they paid; what changes is how ReFuel's side is
        //    reported: bought at pump − spread, delivered as a share of a route.
        var orders = await db.Orders.Where(o => o.PumpPricePerLitre <= 0 && o.UnitPrice > 0).ToListAsync();
        var individual = tiers.FirstOrDefault(t => t.CustomerType == CustomerType.Individual);

        foreach (var order in orders)
        {
            var tier = tiers.FirstOrDefault(t => t.Id == order.PricingTierId) ?? individual;
            ApplyPumpParityEconomics(order, settings, tier);
        }

        await db.SaveChangesAsync();

        // 7. Sales offers — re-cost the drafts on the wholesale basis with the new tier margins.
        var offers = await db.BusinessOffers.ToListAsync();
        var bands = await db.VolumeDiscountBands.Where(b => b.IsActive).ToListAsync();

        foreach (var offer in offers)
        {
            var tier = tiers.FirstOrDefault(t => t.CustomerType == offer.CustomerType && t.IsActive);
            if (tier is null) continue;

            var pump = offer.BaseCostPerLitre;
            offer.BaseCostPerLitre = Math.Round(Math.Max(0, pump - spread), 2);
            offer.ProposedMarginPerLitre = tier.MarginPerLitre;
            offer.VolumeDiscountPerLitre = bands
                .Where(b => (b.PricingTierId == null || b.PricingTierId == tier.Id) && b.Covers(offer.EstimatedLitresPerMonth))
                .OrderByDescending(b => b.PricingTierId.HasValue)
                .Select(b => b.DiscountPerLitre)
                .FirstOrDefault();
            offer.Recalculate(settings);
        }

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Pricing model v2: {Tiers} tiers retuned, {Bands} bands rebuilt, {Contracts} contracts checked, {Deliveries} deliveries stamped, {Orders} orders re-costed, {Offers} offers re-costed.",
            tiers.Count, bands.Count, contracts.Count, deliveries.Count, orders.Count, offers.Count);
    }

    /// <summary>
    /// v2 → v3: the demo history is rebuilt so the reports describe the business as designed —
    /// contract drops by the nearest tanker at the committed volumes, a retail channel with a
    /// realistic number of orders, promotions on the delivery instead of the litre, and one spelling
    /// per city. Only synthetic rows are touched: the seeded contracts' deliveries/proofs/invoices,
    /// the seeded promotions, station city names. Registered accounts and their orders stay.
    /// </summary>
    private static async Task UpgradeToV3Async(ApplicationDbContext db, ReFuelOptions options, ILogger logger)
    {
        // 1. Promotions: retire the litre discounts, add the delivery/loyalty set.
        var now = DateTime.UtcNow;
        var retired = await db.Promotions.Where(p => RetiredPromotionCodes.Contains(p.Code)).ToListAsync();
        foreach (var promo in retired)
        {
            promo.IsActive = false;
            if (promo.EndsAtUtc > now) promo.EndsAtUtc = now.AddDays(-1);
        }

        var existingCodes = await db.Promotions.Select(p => p.Code).ToListAsync();
        db.Promotions.AddRange(BuildPromotions(now).Where(p => !existingCodes.Contains(p.Code)));
        await db.SaveChangesAsync();

        // 2. Station cities: one spelling each.
        var stations = await db.Stations.Include(x => x.Brand).ToListAsync();
        var renamed = 0;
        foreach (var station in stations)
        {
            var city = Translit.NormalizeCity(station.City);
            if (string.IsNullOrWhiteSpace(city) || city == station.City) continue;
            station.City = city;
            station.SearchKey = Translit.BuildSearchKey(station.Name, station.Brand?.Name, station.Brand?.NameMk, station.City, station.Address, station.OsmId);
            renamed++;
        }
        await db.SaveChangesAsync();

        // 3. Seeded contracts: rebuild deliveries, proofs and invoices from scratch.
        var seededNumbers = SeedData.Businesses.Select(b => b.Contract).ToList();
        var contracts = await db.SupplyContracts
            .Include(c => c.BusinessAccount)
            .Where(c => seededNumbers.Contains(c.ContractNumber))
            .ToListAsync();

        if (contracts.Count > 0)
        {
            var contractIds = contracts.Select(c => c.Id).ToList();

            var deliveries = await db.ContractDeliveries.Where(d => contractIds.Contains(d.SupplyContractId)).ToListAsync();
            var deliveryIds = deliveries.Select(d => d.Id).ToList();

            var anomalies = await db.FuelAnomalies
                .Where(a => a.ContractDeliveryId != null && deliveryIds.Contains(a.ContractDeliveryId.Value))
                .ToListAsync();
            foreach (var anomaly in anomalies) anomaly.ContractDeliveryId = null;

            var proofs = await db.DeliveryProofs.Where(x => deliveryIds.Contains(x.ContractDeliveryId)).ToListAsync();
            var invoices = await db.Invoices.Where(i => i.SupplyContractId != null && contractIds.Contains(i.SupplyContractId.Value)).ToListAsync();
            var invoiceIds = invoices.Select(i => i.Id).ToList();
            var lines = await db.InvoiceLines.Where(l => invoiceIds.Contains(l.InvoiceId)).ToListAsync();

            db.InvoiceLines.RemoveRange(lines);
            db.DeliveryProofs.RemoveRange(proofs);
            await db.SaveChangesAsync();

            db.ContractDeliveries.RemoveRange(deliveries);
            db.Invoices.RemoveRange(invoices);
            await db.SaveChangesAsync();

            await SeedContractHistoryAsync(db, contracts, DemoHistoryDays, logger);
        }

        // 4. Retail demo orders: top up to the assumed daily rate over the history window.
        var since = now.AddDays(-DemoHistoryDays);
        var demoUserIds = await db.Users
            .Where(u => u.Email != null && u.Email.EndsWith("@refuel.mk") && u.BusinessAccountId == null)
            .Select(u => u.Id)
            .ToListAsync();

        var recent = await db.Orders.CountAsync(o => o.CreatedAtUtc >= since && demoUserIds.Contains(o.UserId));
        var target = DemoHistoryDays * DemoOrdersPerDay;
        if (recent < target)
            await GenerateDemoOrdersAsync(db, options, logger, target - recent, DemoHistoryDays);

        logger.LogInformation("Pricing model v3: {Retired} promotions retired, {Cities} station cities normalised, {Contracts} contract histories rebuilt, demo orders topped up from {Recent} to {Target}.",
            retired.Count, renamed, contracts.Count, recent, target);
    }
}

public static class BusinessHash
{
    public static string Compute(params object[] parts)
    {
        // Timestamps are hashed without their Kind: a value written as UTC comes back from SQLite as
        // Unspecified, and the fingerprint must survive that round trip.
        var payload = string.Join("|", parts.Select(p => p switch
        {
            DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified).ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            decimal m => m.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            _ => p.ToString() ?? string.Empty
        }));

        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..32].ToUpperInvariant();
    }
}
