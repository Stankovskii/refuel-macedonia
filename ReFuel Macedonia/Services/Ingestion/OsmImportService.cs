using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Services.Ingestion;

public record OsmImportResult(bool Success, int Fetched, int Created, int Updated, string Message, string? Endpoint = null);

public interface IOsmImportService
{
    Task<OsmImportResult> ImportAsync(bool replaceSeedData = true, CancellationToken ct = default);
}

public class OsmImportService : IOsmImportService
{
    private readonly HttpClient _http;
    private readonly ApplicationDbContext _db;
    private readonly ReFuelOptions _options;
    private readonly ILogger<OsmImportService> _logger;

    public OsmImportService(HttpClient http, ApplicationDbContext db, IOptions<ReFuelOptions> options, ILogger<OsmImportService> logger)
    {
        _http = http;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    private const string OverpassQuery = """
        [out:json][timeout:120];
        area["ISO3166-1"="MK"][admin_level=2]->.mk;
        (
          node["amenity"="fuel"](area.mk);
          way["amenity"="fuel"](area.mk);
          relation["amenity"="fuel"](area.mk);
          node["shop"="gas"](area.mk);
          way["shop"="gas"](area.mk);
        );
        out center tags;
        """;

    private const string OverpassBboxQuery = """
        [out:json][timeout:120];
        (
          node["amenity"="fuel"](40.83,20.44,42.38,23.05);
          way["amenity"="fuel"](40.83,20.44,42.38,23.05);
          relation["amenity"="fuel"](40.83,20.44,42.38,23.05);
        );
        out center tags;
        """;

    public async Task<OsmImportResult> ImportAsync(bool replaceSeedData = true, CancellationToken ct = default)
    {
        string? payload = null;
        string? usedEndpoint = null;

        foreach (var endpoint in _options.OverpassEndpoints)
        {
            payload = await TryFetchAsync(endpoint, OverpassQuery, ct)
                      ?? await TryFetchAsync(endpoint, OverpassBboxQuery, ct);

            if (payload is not null)
            {
                usedEndpoint = endpoint;
                break;
            }
        }

        if (payload is null)
            return new OsmImportResult(false, 0, 0, 0, "osm.error.unreachable");

        List<OsmElement> elements;
        try
        {
            elements = Parse(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not parse the Overpass response.");
            return new OsmImportResult(false, 0, 0, 0, "osm.error.parse", usedEndpoint);
        }

        if (elements.Count == 0)
            return new OsmImportResult(false, 0, 0, 0, "osm.error.empty", usedEndpoint);

        var brands = await _db.Brands.ToListAsync(ct);
        var existing = await _db.Stations
            .Where(s => s.OsmId != null)
            .ToDictionaryAsync(s => s.OsmId!, s => s, ct);

        var created = 0;
        var updated = 0;

        foreach (var element in elements)
        {
            if (!GeoUtils.IsInsideCountry(element.Lat, element.Lon)) continue;

            var brand = MatchBrand(brands, element.Brand ?? element.Operator ?? element.Name);
            // One spelling per city: OSM tags mix scripts ("Cкoпje", "Skopje") and municipalities.
            var city = Translit.NormalizeCity(element.City);
            if (string.IsNullOrWhiteSpace(city)) city = NearestCity(element.Lat, element.Lon);

            if (existing.TryGetValue(element.OsmId, out var station))
            {
                station.Name = BuildName(element, brand);
                station.SearchKey = Translit.BuildSearchKey(station.Name, brand?.Name, brand?.NameMk, city, element.Address, element.OsmId);
                station.BrandId = brand?.Id ?? station.BrandId;
                station.Latitude = element.Lat;
                station.Longitude = element.Lon;
                station.City = city;
                station.Address = element.Address ?? station.Address;
                station.Phone = element.Phone ?? station.Phone;
                station.OpeningHours = element.OpeningHours ?? station.OpeningHours;
                station.IsOpen24h = element.OpeningHours?.Contains("24/7") == true || station.IsOpen24h;
                station.HasLpg = element.HasLpg || station.HasLpg;
                station.HasCng = element.HasCng || station.HasCng;
                station.HasShop = element.HasShop || station.HasShop;
                station.HasCarWash = element.HasCarWash || station.HasCarWash;
                station.HasToilets = element.HasToilets || station.HasToilets;
                station.Source = StationSource.OpenStreetMap;
                station.UpdatedAtUtc = DateTime.UtcNow;
                updated++;
            }
            else
            {
                var newName = BuildName(element, brand);

                _db.Stations.Add(new Station
                {
                    OsmId = element.OsmId,
                    Name = newName,
                    SearchKey = Translit.BuildSearchKey(newName, brand?.Name, brand?.NameMk, city, element.Address, element.OsmId),
                    BrandId = brand?.Id,
                    Latitude = element.Lat,
                    Longitude = element.Lon,
                    City = city,
                    Municipality = element.Municipality,
                    Address = element.Address,
                    Phone = element.Phone,
                    OpeningHours = element.OpeningHours,
                    IsOpen24h = element.OpeningHours?.Contains("24/7") == true,
                    HasLpg = element.HasLpg,
                    HasCng = element.HasCng,
                    HasShop = element.HasShop,
                    HasCarWash = element.HasCarWash,
                    HasToilets = element.HasToilets,
                    Source = StationSource.OpenStreetMap,
                    IsVerified = brand is not null,
                    IsActive = true
                });
                created++;
            }
        }

        await _db.SaveChangesAsync(ct);

        if (replaceSeedData && created > 50)
        {
            var seeded = await _db.Stations.Where(s => s.Source == StationSource.Seed).ToListAsync(ct);
            if (seeded.Count > 0)
            {
                _db.Stations.RemoveRange(seeded);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Removed {Count} fallback stations replaced by OpenStreetMap data.", seeded.Count);
            }
        }

        _logger.LogInformation("OSM import finished: {Created} new, {Updated} updated stations from {Endpoint}.",
            created, updated, usedEndpoint);

        return new OsmImportResult(true, elements.Count, created, updated, "osm.ok", usedEndpoint);
    }

    private async Task<string?> TryFetchAsync(string endpoint, string query, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent("data=" + Uri.EscapeDataString(query), Encoding.UTF8,
                "application/x-www-form-urlencoded");

            using var response = await _http.PostAsync(endpoint, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Overpass endpoint {Endpoint} answered {Status}.", endpoint, (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Overpass endpoint {Endpoint} is not reachable.", endpoint);
            return null;
        }
    }

    private sealed class OsmElement
    {
        public string OsmId { get; init; } = string.Empty;
        public double Lat { get; init; }
        public double Lon { get; init; }
        public string? Name { get; init; }
        public string? Brand { get; init; }
        public string? Operator { get; init; }
        public string? City { get; init; }
        public string? Municipality { get; init; }
        public string? Address { get; init; }
        public string? Phone { get; init; }
        public string? OpeningHours { get; init; }
        public bool HasLpg { get; init; }
        public bool HasCng { get; init; }
        public bool HasShop { get; init; }
        public bool HasCarWash { get; init; }
        public bool HasToilets { get; init; }
    }

    private static List<OsmElement> Parse(string json)
    {
        var list = new List<OsmElement>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("elements", out var elements)) return list;

        foreach (var element in elements.EnumerateArray())
        {
            var type = element.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is null || type == "count") continue;

            var id = element.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0;
            if (id == 0) continue;

            double lat, lon;
            if (element.TryGetProperty("lat", out var latProp) && element.TryGetProperty("lon", out var lonProp))
            {
                lat = latProp.GetDouble();
                lon = lonProp.GetDouble();
            }
            else if (element.TryGetProperty("center", out var center)
                     && center.TryGetProperty("lat", out var cLat)
                     && center.TryGetProperty("lon", out var cLon))
            {
                lat = cLat.GetDouble();
                lon = cLon.GetDouble();
            }
            else
            {
                continue;
            }

            var tags = element.TryGetProperty("tags", out var tagProp) ? tagProp : default;

            string? Tag(string key)
            {
                if (tags.ValueKind != JsonValueKind.Object) return null;
                return tags.TryGetProperty(key, out var value) ? value.GetString() : null;
            }

            bool Yes(string key)
            {
                var v = Tag(key);
                return v is not null && (v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v == "1");
            }

            var street = Tag("addr:street");
            var houseNumber = Tag("addr:housenumber");
            var address = street is null ? null : string.IsNullOrEmpty(houseNumber) ? street : $"{street} {houseNumber}";

            list.Add(new OsmElement
            {
                OsmId = $"{type}/{id}",
                Lat = lat,
                Lon = lon,
                Name = Tag("name") ?? Tag("name:mk") ?? Tag("name:en"),
                Brand = Tag("brand"),
                Operator = Tag("operator"),
                City = Tag("addr:city"),
                Municipality = Tag("addr:district") ?? Tag("addr:city"),
                Address = address,
                Phone = Tag("phone") ?? Tag("contact:phone"),
                OpeningHours = Tag("opening_hours"),
                HasLpg = Yes("fuel:lpg") || Yes("fuel:LPG"),
                HasCng = Yes("fuel:cng") || Yes("fuel:CNG"),
                HasShop = Tag("shop") is not null || Yes("shop:convenience"),
                HasCarWash = Yes("car_wash") || Tag("amenity:car_wash") is not null,
                HasToilets = Yes("toilets")
            });
        }

        return list;
    }

    private static string BuildName(OsmElement element, Brand? brand)
    {
        if (!string.IsNullOrWhiteSpace(element.Name)) return Trim(element.Name!, 200);
        if (brand is not null) return $"{brand.Name} - {element.City ?? "Бензинска станица"}";
        if (!string.IsNullOrWhiteSpace(element.Operator)) return Trim(element.Operator!, 200);
        return "Бензинска станица";
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

    private static Brand? MatchBrand(List<Brand> brands, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim().ToLowerInvariant();

        var aliases = new (string Slug, string[] Keys)[]
        {
            ("makpetrol",        new[] { "makpetrol", "mak petrol", "макпетрол", "мак петрол" }),
            ("lukoil",           new[] { "lukoil", "лукоил", "лукойл" }),
            ("okta",             new[] { "okta", "окта", "hellenic", "helleniq" }),
            ("dadi-oil",         new[] { "dadi", "дади" }),
            ("supertrejd",       new[] { "supertrejd", "supertrade", "super trade", "супертрејд" }),
            ("crna-reka-petrol", new[] { "crna reka", "црна река", "black river" }),
            ("detoil",           new[] { "detoil", "dettoil", "детоил" }),
            ("ompetrol",         new[] { "ompetrol", "om petrol", "омпетрол" }),
            ("pucko-petrol",     new[] { "pucko", "пуцко" }),
            ("javor-sped",       new[] { "javor", "јавор" }),
            ("euro-petrol",      new[] { "euro petrol", "euro-petrol", "еуро петрол", "europetrol" }),
            ("nafta-komerc",     new[] { "nafta komerc", "нафта комерц" }),
            ("sardich-mb",       new[] { "sardich", "sardic", "сардич" }),
            ("vero-petrol",      new[] { "vero petrol", "веро петрол" })
        };

        foreach (var (slug, keys) in aliases)
        {
            if (keys.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return brands.FirstOrDefault(b => b.Slug == slug);
        }

        return brands.FirstOrDefault(b => value.Contains(b.Name.ToLowerInvariant()));
    }

    private static string NearestCity(double lat, double lon)
    {
        var best = SeedData.Cities[0];
        var bestDistance = double.MaxValue;

        foreach (var city in SeedData.Cities)
        {
            var d = GeoUtils.HaversineKm(lat, lon, city.Lat, city.Lon);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = city;
            }
        }

        return best.Name;
    }
}
