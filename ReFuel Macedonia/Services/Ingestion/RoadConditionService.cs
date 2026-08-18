using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Services.Ingestion;

public record RoadResult(bool Success, int Roads, int Borders, string Message);

public interface IRoadConditionService
{
    Task<RoadResult> RefreshAsync(CancellationToken ct = default);
    Task<List<RoadCondition>> GetRoadsAsync();
    Task<List<BorderCrossing>> GetBordersAsync();
    Task<List<RoadCondition>> GetAlertsAsync();
}

public class RoadConditionService : IRoadConditionService
{
    private readonly HttpClient _http;
    private readonly ApplicationDbContext _db;
    private readonly ReFuelOptions _options;
    private readonly ILogger<RoadConditionService> _logger;

    public RoadConditionService(HttpClient http, ApplicationDbContext db, IOptions<ReFuelOptions> options, ILogger<RoadConditionService> logger)
    {
        _http = http;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RoadResult> RefreshAsync(CancellationToken ct = default)
    {
        if (!_options.EnableRoadConditions)
            return new RoadResult(false, 0, 0, "road.disabled");

        string html;
        try
        {
            using var response = await _http.GetAsync(_options.RoadConditionsUrl, ct);
            if (!response.IsSuccessStatusCode)
                return new RoadResult(false, 0, 0, "road.error.http");

            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Road condition source unreachable.");
            return new RoadResult(false, 0, 0, "road.error.network");
        }

        var text = ErcPriceScraper.StripHtml(html);
        if (text.Length < 120)
            return new RoadResult(false, 0, 0, "road.error.parse");

        var borders = ParseBorders(text);
        var roads = ParseRoads(text);

        var updatedBorders = 0;
        if (borders.Count > 0)
        {
            var stored = await _db.BorderCrossings.ToListAsync(ct);
            foreach (var parsed in borders)
            {
                var match = stored.FirstOrDefault(b => Translit.Normalize(b.Name) == Translit.Normalize(parsed.Name));
                if (match is null) continue;

                match.EntryWaitMinutes = parsed.Entry;
                match.ExitWaitMinutes = parsed.Exit;
                match.ReportedAtUtc = DateTime.UtcNow;
                updatedBorders++;
            }
            await _db.SaveChangesAsync(ct);
        }

        var updatedRoads = 0;
        if (roads.Count > 0)
        {
            var stored = await _db.RoadConditions.Where(r => r.Source == "AMSM").ToListAsync(ct);
            _db.RoadConditions.RemoveRange(stored);
            _db.RoadConditions.AddRange(roads);
            await _db.SaveChangesAsync(ct);
            updatedRoads = roads.Count;
        }

        return new RoadResult(true, updatedRoads, updatedBorders,
            updatedRoads + updatedBorders > 0 ? "road.ok" : "road.nochange");
    }

    private static List<(string Name, int Entry, int Exit)> ParseBorders(string text)
    {
        var result = new List<(string, int, int)>();

        var pattern = new Regex(
            @"(?:ГП|Г\.П\.)\s*([А-Яа-яЁёЀ-ӿ\s\-]{3,30}?)\s*(?:за\s+)?(влез|излез)[^0-9]{0,25}(?:(\d{1,3})\s*(час|мин)|(?:околу\s+)?(\d{1,3})\s*(час|мин))",
            RegexOptions.IgnoreCase);

        foreach (Match match in pattern.Matches(text))
        {
            var name = match.Groups[1].Value.Trim(' ', '-', '–');
            if (name.Length < 3) continue;

            var value = match.Groups[3].Success ? match.Groups[3].Value : match.Groups[5].Value;
            var unit = match.Groups[4].Success ? match.Groups[4].Value : match.Groups[6].Value;

            if (!int.TryParse(value, out var amount)) continue;
            var minutes = unit.StartsWith("час", StringComparison.OrdinalIgnoreCase) ? amount * 60 : amount;

            var isEntry = match.Groups[2].Value.StartsWith("влез", StringComparison.OrdinalIgnoreCase);

            var index = result.FindIndex(r => r.Item1 == name);
            if (index >= 0)
            {
                var existing = result[index];
                result[index] = isEntry ? (existing.Item1, minutes, existing.Item3) : (existing.Item1, existing.Item2, minutes);
            }
            else
            {
                result.Add(isEntry ? (name, minutes, 0) : (name, 0, minutes));
            }
        }

        return result;
    }

    private static List<RoadCondition> ParseRoads(string text)
    {
        var result = new List<RoadCondition>();

        var sentences = Regex.Split(text, @"(?<=[\.\!])\s+")
            .Select(s => s.Trim())
            .Where(s => s.Length is > 30 and < 500)
            .ToList();

        var keywords = new (string Word, RoadStatus Status)[]
        {
            ("затворен", RoadStatus.Closed),
            ("прекин", RoadStatus.Closed),
            ("забранет", RoadStatus.Closed),
            ("наизменич", RoadStatus.Restricted),
            ("ограничув", RoadStatus.Restricted),
            ("реконструкц", RoadStatus.Restricted),
            ("работи", RoadStatus.Restricted),
            ("одрон", RoadStatus.Caution),
            ("магла", RoadStatus.Caution),
            ("снег", RoadStatus.Caution),
            ("поледиц", RoadStatus.Caution),
            ("внимание", RoadStatus.Caution),
            ("зимска опрема", RoadStatus.Caution)
        };

        foreach (var sentence in sentences)
        {
            var lower = sentence.ToLowerInvariant();
            var hit = keywords.FirstOrDefault(k => lower.Contains(k.Word, StringComparison.Ordinal));
            if (hit.Word is null) continue;

            var roadMatch = Regex.Match(sentence, @"(?:А\d|Р\d{3,4}|автопат[а-я]*|магистрал[а-я]*|тунел[а-я]*|превој[а-я]*)[^,\.]{0,60}", RegexOptions.IgnoreCase);
            var road = roadMatch.Success ? roadMatch.Value.Trim() : "Патна мрежа";

            if (result.Any(r => r.Road == road && r.Status == hit.Status)) continue;

            result.Add(new RoadCondition
            {
                Road = road.Length > 200 ? road[..200] : road,
                Description = sentence.Length > 600 ? sentence[..600] : sentence,
                Status = hit.Status,
                Source = "AMSM",
                ReportedAtUtc = DateTime.UtcNow
            });

            if (result.Count >= 25) break;
        }

        return result;
    }

    public Task<List<RoadCondition>> GetRoadsAsync()
        => _db.RoadConditions.OrderBy(r => r.Status == RoadStatus.Normal).ThenByDescending(r => r.Status).ToListAsync();

    public Task<List<BorderCrossing>> GetBordersAsync()
        => _db.BorderCrossings.OrderByDescending(b => b.EntryWaitMinutes + b.ExitWaitMinutes).ToListAsync();

    public Task<List<RoadCondition>> GetAlertsAsync()
        => _db.RoadConditions.Where(r => r.Status != RoadStatus.Normal)
                             .OrderByDescending(r => r.Status)
                             .Take(5)
                             .ToListAsync();
}
