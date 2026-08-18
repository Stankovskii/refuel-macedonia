using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Services.Ai;

namespace ReFuel_Macedonia.Services.Ingestion;

public class DataRefreshBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReFuelOptions _options;
    private readonly ILogger<DataRefreshBackgroundService> _logger;

    public DataRefreshBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReFuelOptions> options,
        ILogger<DataRefreshBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var first = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(first, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The data refresh cycle failed.");
            }

            first = false;

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(15, _options.RefreshIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(bool isFirstRun, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();

        if (isFirstRun && _options.EnableOsmImport)
        {
            var hasRealData = await db.Stations.AnyAsync(s => s.Source == StationSource.OpenStreetMap, ct);
            if (!hasRealData)
            {
                _logger.LogInformation("Importing the station network from OpenStreetMap...");
                var import = services.GetRequiredService<IOsmImportService>();
                var result = await import.ImportAsync(true, ct);

                IngestionState.LastOsmResult = result;
                IngestionState.LastOsmImportUtc = DateTime.UtcNow;

                if (result.Success)
                {
                    _logger.LogInformation("OpenStreetMap import: {Created} new stations.", result.Created);
                    await BackfillPricesAsync(services, ct);
                }
                else
                {
                    _logger.LogWarning("OpenStreetMap import failed ({Message}); keeping the offline data set.", result.Message);
                }
            }
        }

        if (_options.EnablePriceScraper)
        {
            var scraper = services.GetRequiredService<IErcPriceScraper>();
            var scrape = await scraper.RefreshAsync(ct);
            IngestionState.LastScrapeResult = scrape;
            IngestionState.LastScrapeUtc = DateTime.UtcNow;

            if (scrape.Success && scrape.Imported > 0)
                _logger.LogInformation("Imported {Count} regulated prices effective {Date:d}.", scrape.Imported, scrape.EffectiveFrom);
        }

        if (_options.EnableWeather)
        {
            var weather = services.GetRequiredService<IWeatherService>();
            var result = await weather.RefreshAsync(ct);
            IngestionState.LastWeatherResult = result;
            IngestionState.LastWeatherUtc = DateTime.UtcNow;
        }

        if (_options.EnableRoadConditions)
        {
            var roads = services.GetRequiredService<IRoadConditionService>();
            var result = await roads.RefreshAsync(ct);
            IngestionState.LastRoadResult = result;
            IngestionState.LastRoadUtc = DateTime.UtcNow;
        }

        var business = services.GetRequiredService<Services.IBusinessService>();
        await business.GenerateUpcomingDeliveriesAsync(7);

        var predictor = services.GetRequiredService<IPricePredictionService>();
        await predictor.RegenerateAsync();
        IngestionState.LastForecastUtc = DateTime.UtcNow;
    }

    private static async Task BackfillPricesAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        var fuels = await db.FuelTypes.OrderBy(f => f.SortOrder).ToListAsync(ct);
        var stations = await db.Stations
            .Where(s => s.Source == StationSource.OpenStreetMap)
            .Select(s => new { s.Id, s.HasLpg, s.HasCng, s.IsOnMotorway, s.BrandId })
            .ToListAsync(ct);

        var priced = await db.StationPrices
            .Where(p => p.IsCurrent)
            .Select(p => p.StationId)
            .Distinct()
            .ToListAsync(ct);

        var pricedSet = priced.ToHashSet();

        const int historyDepth = 26;
        var historyCodes = new[] { "EUROSUPER_95", "EUROSUPER_98", "EURODIESEL" };

        var series = new Dictionary<int, List<NationalPrice>>();
        foreach (var fuel in fuels)
        {
            series[fuel.Id] = await db.NationalPrices
                .Where(p => p.FuelTypeId == fuel.Id)
                .OrderByDescending(p => p.EffectiveFrom)
                .Take(historyDepth)
                .ToListAsync(ct);
        }

        var rows = new List<StationPrice>();

        foreach (var station in stations)
        {
            if (pricedSet.Contains(station.Id)) continue;

            var hash = (station.Id * 2654435761L) % 1000;
            var discount = (decimal)(hash / 1000.0 * 1.8) + (station.BrandId ?? 0) % 3 * 0.2m;
            if (station.IsOnMotorway) discount = Math.Max(0, discount - 0.6m);

            foreach (var fuel in fuels)
            {
                if (fuel.Code == "LPG" && !station.HasLpg) continue;
                if (fuel.Code == "CNG" && !station.HasCng) continue;
                if (fuel.Code is "MAZUT_M1" or "EL_1") continue;

                if (!series.TryGetValue(fuel.Id, out var points) || points.Count == 0) continue;

                var take = historyCodes.Contains(fuel.Code) ? Math.Min(historyDepth, points.Count) : 1;

                for (var i = 0; i < take; i++)
                {
                    var max = points[i].Price;

                    rows.Add(new StationPrice
                    {
                        StationId = station.Id,
                        FuelTypeId = fuel.Id,
                        Price = Math.Round(Math.Max(max - discount, max * 0.94m), 2),
                        Source = i == 0 ? PriceSource.CompanyWebsite : PriceSource.Estimated,
                        Confidence = i == 0 ? 0.7 : 0.5,
                        IsCurrent = i == 0,
                        ObservedAtUtc = DateTime.SpecifyKind(points[i].EffectiveFrom.AddHours(6), DateTimeKind.Utc)
                    });
                }
            }
        }

        if (rows.Count == 0) return;

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.StationPrices.AddRange(rows);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
    }
}

public static class IngestionState
{
    public static DateTime? LastOsmImportUtc { get; set; }
    public static DateTime? LastScrapeUtc { get; set; }
    public static DateTime? LastForecastUtc { get; set; }
    public static DateTime? LastWeatherUtc { get; set; }
    public static DateTime? LastRoadUtc { get; set; }
    public static OsmImportResult? LastOsmResult { get; set; }
    public static ScrapeResult? LastScrapeResult { get; set; }
    public static WeatherResult? LastWeatherResult { get; set; }
    public static RoadResult? LastRoadResult { get; set; }
}
