using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace ReFuel_Macedonia.Infrastructure;

public static class SupportedCultures
{
    public const string Macedonian = "mk";
    public const string English = "en";
    public const string Albanian = "sq";

    public static readonly string[] All = { Macedonian, English, Albanian };

    public static string NativeName(string code) => code switch
    {
        Macedonian => "Македонски",
        Albanian => "Shqip",
        _ => "English"
    };

    public static string ShortName(string code) => code switch
    {
        Macedonian => "MK",
        Albanian => "AL",
        _ => "EN"
    };

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Macedonian;
        var two = code.Trim().ToLowerInvariant();
        if (two.Length > 2) two = two[..2];
        return All.Contains(two) ? two : Macedonian;
    }
}

public interface ITranslator
{
    string this[string key] { get; }

    string Format(string key, params object?[] args);

    string Culture { get; }
}

public class JsonTranslator : ITranslator
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache = new();
    private readonly string _resourcePath;
    private readonly ILogger<JsonTranslator> _logger;

    public JsonTranslator(IWebHostEnvironment env, ILogger<JsonTranslator> logger)
    {
        _logger = logger;
        _resourcePath = Path.Combine(env.ContentRootPath, "Resources");
    }

    public string Culture => SupportedCultures.Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public string this[string key] => Lookup(key);

    public string Format(string key, params object?[] args)
    {
        var template = Lookup(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private string Lookup(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;

        var culture = Culture;
        if (TryGet(culture, key, out var value)) return value;
        if (culture != SupportedCultures.Macedonian && TryGet(SupportedCultures.Macedonian, key, out value)) return value;
        if (TryGet(SupportedCultures.English, key, out value)) return value;

        return key;
    }

    private bool TryGet(string culture, string key, out string value)
    {
        var map = _cache.GetOrAdd(culture, Load);
        return map.TryGetValue(key, out value!) && !string.IsNullOrEmpty(value);
    }

    private Dictionary<string, string> Load(string culture)
    {
        var file = Path.Combine(_resourcePath, culture + ".json");
        try
        {
            if (!File.Exists(file))
            {
                _logger.LogWarning("Translation file {File} not found.", file);
                return new Dictionary<string, string>();
            }

            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load translation file {File}.", file);
            return new Dictionary<string, string>();
        }
    }
}
