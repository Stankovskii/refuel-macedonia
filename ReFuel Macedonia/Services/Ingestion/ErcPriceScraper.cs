using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Services.Ingestion;

public record ScrapeResult(bool Success, int Imported, DateTime? EffectiveFrom, string Message, Dictionary<string, decimal>? Prices = null);

public interface IErcPriceScraper
{
    Task<ScrapeResult> RefreshAsync(CancellationToken ct = default);
    Task<ScrapeResult> ApplyManualAsync(Dictionary<string, decimal> prices, DateTime effectiveFrom);
}

public class ErcPriceScraper : IErcPriceScraper
{
    private readonly HttpClient _http;
    private readonly ApplicationDbContext _db;
    private readonly IPriceService _prices;
    private readonly Services.INotificationService _notifications;
    private readonly Infrastructure.ReFuelOptions _options;
    private readonly ILogger<ErcPriceScraper> _logger;

    public ErcPriceScraper(
        HttpClient http,
        ApplicationDbContext db,
        IPriceService prices,
        Services.INotificationService notifications,
        IOptions<Infrastructure.ReFuelOptions> options,
        ILogger<ErcPriceScraper> logger)
    {
        _http = http;
        _db = db;
        _prices = prices;
        _notifications = notifications;
        _options = options.Value;
        _logger = logger;
    }

    private static readonly (string Code, string Pattern)[] Patterns =
    {
        ("EUROSUPER_95", @"(?:ЕУРОСУПЕР|EUROSUPER)\s*(?:БС|BS)?\s*-?\s*95"),
        ("EUROSUPER_98", @"(?:ЕУРОСУПЕР|EUROSUPER)\s*(?:БС|BS)?\s*-?\s*98"),
        ("EURODIESEL",   @"(?:ЕУРОДИЗЕЛ|EURODIESEL|EURODIZEL)"),
        ("EL_1",         @"(?:ЕКСТРА\s+ЛЕСНО|EL\s*-?\s*1|ЕЛ\s*-?\s*1)"),
        ("MAZUT_M1",     @"(?:МАЗУТ|MAZUT)\s*(?:М|M)\s*-?\s*1")
    };

    public async Task<ScrapeResult> RefreshAsync(CancellationToken ct = default)
    {
        if (!_options.EnablePriceScraper)
            return new ScrapeResult(false, 0, null, "scraper.disabled");

        string html;
        try
        {
            using var response = await _http.GetAsync(_options.ErcPricesUrl, ct);
            if (!response.IsSuccessStatusCode)
                return new ScrapeResult(false, 0, null, "scraper.error.http");

            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The ERC page could not be downloaded.");
            return new ScrapeResult(false, 0, null, "scraper.error.network");
        }

        var text = StripHtml(html);
        var parsed = ExtractPrices(text);

        if (parsed.Count == 0)
            return new ScrapeResult(false, 0, null, "scraper.error.parse");

        var effectiveFrom = ExtractDate(text) ?? DateTime.UtcNow.Date;
        var imported = await StoreAsync(parsed, effectiveFrom, PriceSource.RegulatoryCommission);

        return new ScrapeResult(true, imported, effectiveFrom,
            imported > 0 ? "scraper.ok" : "scraper.nochange", parsed);
    }

    public async Task<ScrapeResult> ApplyManualAsync(Dictionary<string, decimal> prices, DateTime effectiveFrom)
    {
        var imported = await StoreAsync(prices, effectiveFrom.Date, PriceSource.Manual);
        return new ScrapeResult(true, imported, effectiveFrom, imported > 0 ? "scraper.ok" : "scraper.nochange", prices);
    }

    private async Task<int> StoreAsync(Dictionary<string, decimal> prices, DateTime effectiveFrom, PriceSource source)
    {
        var fuels = await _db.FuelTypes.ToListAsync();
        var imported = 0;

        foreach (var (code, price) in prices)
        {
            var fuel = fuels.FirstOrDefault(f => f.Code == code);
            if (fuel is null) continue;
            if (price <= 0 || price > 500) continue;

            var previous = await _db.NationalPrices
                .Where(p => p.FuelTypeId == fuel.Id)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync();

            if (previous is not null && previous.EffectiveFrom.Date == effectiveFrom.Date && previous.Price == price)
                continue;

            var existing = await _db.NationalPrices
                .FirstOrDefaultAsync(p => p.FuelTypeId == fuel.Id && p.EffectiveFrom == effectiveFrom);

            if (existing is not null)
            {
                existing.Price = price;
                existing.IsOfficial = true;
                existing.Source = source;
                existing.Change = previous is null ? 0 : Math.Round(price - previous.Price, 3);
            }
            else
            {
                _db.NationalPrices.Add(new NationalPrice
                {
                    FuelTypeId = fuel.Id,
                    EffectiveFrom = effectiveFrom,
                    Price = price,
                    Change = previous is null ? 0 : Math.Round(price - previous.Price, 3),
                    IsOfficial = true,
                    Source = source,
                    DecisionReference = $"РКЕ {effectiveFrom:dd.MM.yyyy}"
                });
            }

            imported++;

            if (previous is not null && price < previous.Price)
                await _notifications.BroadcastPriceDropAsync(fuel.Id, previous.Price, price);
        }

        if (imported > 0)
        {
            await _db.SaveChangesAsync();
            await _prices.ApplyNationalPricesToStationsAsync();
        }

        return imported;
    }

    internal static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        var text = Regex.Replace(html, @"<script[^>]*>.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<style[^>]*>.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    internal static Dictionary<string, decimal> ExtractPrices(string text)
    {
        var result = new Dictionary<string, decimal>();

        foreach (var (code, pattern) in Patterns)
        {
            var match = Regex.Match(text, pattern + @"[^0-9]{0,80}?(\d{1,3}[.,]\d{1,3})", RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            var raw = match.Groups[1].Value.Replace(',', '.');
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) && value is > 5 and < 500)
                result[code] = Math.Round(value, 3);
        }

        return result;
    }

    internal static DateTime? ExtractDate(string text)
    {
        var match = Regex.Match(text, @"(\d{1,2})\.(\d{1,2})\.(\d{4})");
        if (!match.Success) return null;

        var day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var year = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        try
        {
            var date = new DateTime(year, month, day);
            if (date < DateTime.UtcNow.AddYears(-1) || date > DateTime.UtcNow.AddMonths(1)) return null;
            return date;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
