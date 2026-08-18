namespace ReFuel_Macedonia.Infrastructure;

public class ReFuelOptions
{
    public const string SectionName = "ReFuel";

    public bool EnableOsmImport { get; set; } = true;

    public string[] OverpassEndpoints { get; set; } =
    {
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter"
    };

    public bool EnablePriceScraper { get; set; } = true;
    public string ErcPricesUrl { get; set; } = "https://www.erc.org.mk/page.aspx?id=290";

    public bool EnableWeather { get; set; } = true;
    public string WeatherApiUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";

    public bool EnableRoadConditions { get; set; } = true;
    public string RoadConditionsUrl { get; set; } = "https://amsm.mk/sostojba-na-patishta/dnevni-informacii/";

    public int RefreshIntervalMinutes { get; set; } = 180;
    public int WeatherRefreshMinutes { get; set; } = 60;

    public bool EnableTelemetrySimulator { get; set; } = true;
    public int TelemetryIntervalSeconds { get; set; } = 45;
    public string IotApiKey { get; set; } = "refuel-device-key";

    public decimal BaseDeliveryFee { get; set; } = 150m;
    public decimal DeliveryFeePerKm { get; set; } = 18m;
    public decimal EmergencySurcharge { get; set; } = 350m;
    public decimal FreeDeliveryThreshold { get; set; } = 4500m;

    public double MinLitres { get; set; } = 10;
    public double MaxLitres { get; set; } = 120;

    public int PointsPer100Mkd { get; set; } = 5;
    public double DefaultRadiusKm { get; set; } = 15;

    public decimal VatRate { get; set; } = 18m;

    public string AdminEmail { get; set; } = "admin@refuel.mk";
    public string AdminPassword { get; set; } = "Admin123!";

    public string DemoEmail { get; set; } = "demo@refuel.mk";
    public string DemoPassword { get; set; } = "Demo123!";

    public string BusinessEmail { get; set; } = "biznis@refuel.mk";
    public string BusinessPassword { get; set; } = "Biznis123!";
}
