using System.Linq;

namespace EchoXIV.Services;

public enum NonLatinScriptKind
{
    Latin = 0,
    Japanese = 1,
    ChineseSimplified = 2,
    ChineseTraditional = 3,
    Korean = 4,
    Cyrillic = 5,
    Other = 6
}

public static class NonLatinScriptDetector
{
    public static NonLatinScriptKind Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return NonLatinScriptKind.Latin;

        if (text.Any(c => c >= '\uAC00' && c <= '\uD7A3'))
            return NonLatinScriptKind.Korean;

        if (text.Any(c => (c >= '\u3040' && c <= '\u30FF') || (c >= '\u31F0' && c <= '\u31FF')))
            return NonLatinScriptKind.Japanese;

        if (text.Any(c => c >= '\u0400' && c <= '\u04FF'))
            return NonLatinScriptKind.Cyrillic;

        if (text.Any(c => c >= '\u4E00' && c <= '\u9FFF'))
            return NonLatinScriptKind.ChineseSimplified;

        return NonLatinScriptKind.Latin;
    }

    public static string ToLanguageCode(this NonLatinScriptKind script) => script switch
    {
        NonLatinScriptKind.Japanese => "ja",
        NonLatinScriptKind.Korean => "ko",
        NonLatinScriptKind.ChineseTraditional => "zh-TW",
        NonLatinScriptKind.ChineseSimplified => "zh-CN",
        NonLatinScriptKind.Cyrillic => "ru",
        _ => string.Empty,
    };
}
