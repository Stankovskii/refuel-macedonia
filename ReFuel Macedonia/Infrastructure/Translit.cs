using System.Text;

namespace ReFuel_Macedonia.Infrastructure;

public static class Translit
{
    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['ѓ'] = "gj",
        ['е'] = "e", ['ж'] = "zh", ['з'] = "z", ['ѕ'] = "dz", ['и'] = "i", ['ј'] = "j",
        ['к'] = "k", ['л'] = "l", ['љ'] = "lj", ['м'] = "m", ['н'] = "n", ['њ'] = "nj",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['ќ'] = "kj",
        ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "ch", ['џ'] = "dj",
        ['ш'] = "sh"
    };

    private static readonly (string Latin, string Cyrillic)[] LatinToCyrillic =
    {
        ("shch", "шч"), ("gj", "ѓ"), ("kj", "ќ"), ("lj", "љ"), ("nj", "њ"), ("dz", "ѕ"),
        ("zh", "ж"), ("ch", "ч"), ("sh", "ш"), ("dj", "џ"), ("c", "ц"), ("a", "а"),
        ("b", "б"), ("v", "в"), ("g", "г"), ("d", "д"), ("e", "е"), ("z", "з"),
        ("i", "и"), ("j", "ј"), ("k", "к"), ("l", "л"), ("m", "м"), ("n", "н"),
        ("o", "о"), ("p", "п"), ("r", "р"), ("s", "с"), ("t", "т"), ("u", "у"),
        ("f", "ф"), ("h", "х"), ("y", "и"), ("w", "в"), ("x", "кс"), ("q", "к")
    };

    public static string ToLatin(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (CyrillicToLatin.TryGetValue(ch, out var mapped)) builder.Append(mapped);
            else builder.Append(ch);
        }
        return builder.ToString();
    }

    public static string ToCyrillic(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var text = value.ToLowerInvariant();
        foreach (var (latin, cyrillic) in LatinToCyrillic)
            text = text.Replace(latin, cyrillic, StringComparison.Ordinal);

        return text;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var latin = ToLatin(value);
        var builder = new StringBuilder(latin.Length);

        foreach (var ch in latin)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Latin letters that look like Cyrillic ones. OpenStreetMap contributors sometimes type a
    /// Cyrillic word with a few Latin keys ("Cкoпje"), which then sorts and filters as a different
    /// city. Only applied to words that are otherwise Cyrillic.
    /// </summary>
    private static readonly Dictionary<char, char> LatinLookalikes = new()
    {
        ['A'] = 'А', ['B'] = 'В', ['C'] = 'С', ['E'] = 'Е', ['H'] = 'Н', ['K'] = 'К', ['M'] = 'М',
        ['O'] = 'О', ['P'] = 'Р', ['T'] = 'Т', ['X'] = 'Х', ['Y'] = 'У', ['J'] = 'Ј',
        ['a'] = 'а', ['c'] = 'с', ['e'] = 'е', ['o'] = 'о', ['p'] = 'р', ['x'] = 'х', ['y'] = 'у', ['j'] = 'ј'
    };

    /// <summary>Well-known Latin spellings and Skopje's municipalities, folded onto the city a customer would search for.</summary>
    private static readonly Dictionary<string, string> CityAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Skopje"] = "Скопје", ["Shkup"] = "Скопје", ["Skoplje"] = "Скопје",
        ["Аеродром"] = "Скопје", ["Бутел"] = "Скопје", ["Гази Баба"] = "Скопје", ["Ѓорче Петров"] = "Скопје",
        ["Карпош"] = "Скопје", ["Кисела Вода"] = "Скопје", ["Центар"] = "Скопје", ["Чаир"] = "Скопје",
        ["Шуто Оризари"] = "Скопје", ["Сарај"] = "Скопје", ["Сингелиќ"] = "Скопје", ["Идризово"] = "Скопје",
        ["Радишани"] = "Скопје", ["Драчево"] = "Скопје", ["Ново Лисиче"] = "Скопје", ["Ченто"] = "Скопје",
        ["Bitola"] = "Битола", ["Tetovo"] = "Тетово", ["Tetova"] = "Тетово", ["Ohrid"] = "Охрид",
        ["Kumanovo"] = "Куманово", ["Prilep"] = "Прилеп", ["Strumica"] = "Струмица", ["Veles"] = "Велес",
        ["Shtip"] = "Штип", ["Stip"] = "Штип", ["Gostivar"] = "Гостивар", ["Struga"] = "Струга",
        ["Kavadarci"] = "Кавадарци", ["Kocani"] = "Кочани", ["Gevgelija"] = "Гевгелија", ["Debar"] = "Дебар",
        ["Kicevo"] = "Кичево", ["Negotino"] = "Неготино", ["Radovish"] = "Радовиш", ["Resen"] = "Ресен",
        ["Sveti Nikole"] = "Свети Николе", ["Kriva Palanka"] = "Крива Паланка", ["Delcevo"] = "Делчево",
        ["Berovo"] = "Берово", ["Vinica"] = "Виница", ["Probistip"] = "Пробиштип", ["Kratovo"] = "Кратово",
        ["Krusevo"] = "Крушево", ["Demir Hisar"] = "Демир Хисар", ["Makedonski Brod"] = "Македонски Брод",
        ["Valandovo"] = "Валандово", ["Bogdanci"] = "Богданци", ["Dojran"] = "Стар Дојран", ["Pehcevo"] = "Пехчево"
    };

    /// <summary>
    /// The one spelling of a city the rest of the app should see: trims, fixes Latin look-alike
    /// letters inside Cyrillic words, maps Latin spellings and Skopje's municipalities to the city.
    /// </summary>
    public static string NormalizeCity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = value.Trim();
        while (text.Contains("  ")) text = text.Replace("  ", " ");

        // Fix look-alikes word by word, and only inside words that are otherwise Cyrillic.
        var words = text.Split(' ');
        for (var w = 0; w < words.Length; w++)
        {
            var word = words[w];
            var cyrillic = word.Count(ch => ch >= 'Ѐ' && ch <= 'ӿ');
            var latin = word.Count(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            if (cyrillic == 0 || latin == 0 || cyrillic < latin) continue;

            var chars = word.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (LatinLookalikes.TryGetValue(chars[i], out var fixedChar)) chars[i] = fixedChar;
            words[w] = new string(chars);
        }
        text = string.Join(' ', words);

        if (CityAliases.TryGetValue(text, out var alias)) return alias;

        // "Општина Карпош", "Скопје - Центар", "Skopje (Karposh)" and similar → the city itself
        var head = text.Split(new[] { " - ", " – ", " (", "," }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        head = head.Replace("Општина ", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (CityAliases.TryGetValue(head, out alias)) return alias;
        if (head.Length > 0 && head.Length < text.Length && head.Any(char.IsLetter)) return head;

        return text;
    }

    public static string BuildSearchKey(params string?[] parts)
    {
        var pieces = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Normalize);
        var key = string.Join(' ', pieces);
        return key.Length > 400 ? key[..400] : key;
    }

    public static bool Matches(string searchKey, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return true;

        return terms.All(term => searchKey.Contains(term, StringComparison.Ordinal));
    }
}
