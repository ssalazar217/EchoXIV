using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EchoXIV.Services;

public readonly record struct LanguageDetection(string Code, double Confidence)
{
    public bool IsConfident(double threshold = 0.85) =>
        !string.IsNullOrEmpty(Code) && Confidence >= threshold;
}

/// <summary>Detects message language locally without using a chat channel or provider.</summary>
public static class LanguageDetector
{
    private static readonly Regex WordPattern = new(@"[\p{L}]+", RegexOptions.Compiled);
    private static readonly Dictionary<string, HashSet<string>> Markers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = Words("el la los las un una que de del por para con como esto esta muy puedo gracias hola prueba traduccion"),
        ["en"] = Words("the a an and or of to in is are this that with for you can thank thanks hello test translation"),
        ["de"] = Words("der die das den dem ein eine und oder ist sind nicht mit für ich du danke hallo test übersetzung"),
        ["fr"] = Words("le la les un une des et ou de du est sont avec pour je vous merci bonjour test traduction"),
        ["it"] = Words("il lo la gli le un una e o di del della è sono con per io tu grazie ciao prova traduzione"),
        ["pt"] = Words("o a os as um uma e ou de do da que não com para eu você obrigado obrigada olá teste tradução"),
        ["no"] = Words("en et ei den det de som med for jeg du takk hei test oversettelse"),
    };

    public static LanguageDetection Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return default;

        var letters = text.Where(char.IsLetter).ToArray();
        if (letters.Length < 4)
            return default;

        if (letters.Any(IsHangul)) return new("ko", 0.99);
        if (letters.Any(IsKana)) return new("ja", 0.99);
        if (letters.Any(IsCyrillic)) return DetectCyrillic(text);
        if (letters.Any(IsHan)) return DetectChinese(text);

        var words = WordPattern.Matches(text.Normalize(NormalizationForm.FormD))
            .Select(m => RemoveMarks(m.Value).ToLowerInvariant())
            .Where(word => word.Length > 1)
            .ToArray();
        if (words.Length < 2)
            return default;

        var scores = Markers.ToDictionary(pair => pair.Key,
            pair => pair.Value.Sum(marker => words.Count(word => word == marker) * 2.0));
        var ordered = scores.OrderByDescending(pair => pair.Value).ToArray();
        if (ordered.Length == 0 || ordered[0].Value <= 0)
            return default;

        var best = ordered[0];
        var second = ordered.Length > 1 ? ordered[1].Value : 0;
        var confidence = Math.Clamp(0.5 + (best.Value - second) * 0.08 + Math.Min(0.25, best.Value / 20), 0, 0.99);
        return new(best.Key, confidence);
    }

    public static bool IsSameLanguage(string? detected, string? target) =>
        !string.IsNullOrWhiteSpace(detected) && !string.IsNullOrWhiteSpace(target) &&
        string.Equals(NormalizeCode(detected), NormalizeCode(target), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCode(string code) => code.ToLowerInvariant() switch
    {
        "pt-br" => "pt",
        "zh-hans" => "zh-cn",
        "zh-hant" => "zh-tw",
        _ => code.ToLowerInvariant()
    };

    private static LanguageDetection DetectCyrillic(string text) =>
        text.Contains('ї') || text.Contains('є') || text.Contains('і')
            ? new("uk", 0.98)
            : new("ru", 0.92);

    private static LanguageDetection DetectChinese(string text) =>
        text.Any(c => "國學體語說這麼對與為會來發現".Contains(c))
            ? new("zh-TW", 0.93)
            : new("zh-CN", 0.88);

    private static HashSet<string> Words(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string RemoveMarks(string value) =>
        new(value.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    private static bool IsKana(char c) => c is >= '\u3040' and <= '\u30FF' or >= '\u31F0' and <= '\u31FF';
    private static bool IsHangul(char c) => c is >= '\uAC00' and <= '\uD7A3';
    private static bool IsCyrillic(char c) => c is >= '\u0400' and <= '\u04FF';
    private static bool IsHan(char c) => c is >= '\u3400' and <= '\u9FFF';
}
