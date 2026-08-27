using Dalamud;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace EchoXIV.Services;

/// <summary>Loads glyph ranges required by the configured languages.</summary>
public sealed class FontManager : IDisposable
{
    private readonly Configuration _config;
    private readonly Dalamud.Plugin.IDalamudPluginInterface _pi;
    private IFontHandle? _font;
    private string _languageKey = string.Empty;

    public FontManager(Configuration config, Dalamud.Plugin.IDalamudPluginInterface pi)
    {
        _config = config;
        _pi = pi;
    }

    public void BuildFonts()
    {
        var key = string.Join('|', _config.SourceLanguage, _config.TargetLanguage);
        if (_font != null && string.Equals(_languageKey, key, StringComparison.Ordinal))
            return;

        _languageKey = key;
        _font?.Dispose();
        _font = _pi.UiBuilder.FontAtlas.NewDelegateFontHandle(builder =>
            builder.OnPreBuild(toolkit =>
            {
                var config = new SafeFontConfig
                {
                    SizePt = 12.75f,
                    GlyphRanges = BuildRanges()
                };
                config.MergeFont = new DalamudAssetFontAndFamilyId(DalamudAsset.NotoSansCjkRegular)
                    .AddToBuildToolkit(toolkit, config);

                config.SizePt = 12.75f;
                toolkit.AddGameSymbol(config);
                toolkit.Font = config.MergeFont;
            }));

        try
        {
            _pi.UiBuilder.FontAtlas.BuildFontsOnNextFrame();
        }
        catch
        {
            // Font atlas rebuild is best effort during shutdown.
        }
    }

    public IDisposable? Push() => _font?.Push();

    private unsafe ushort[] BuildRanges()
    {
        var ranges = new List<nint> { (nint)ImGui.GetIO().Fonts.GetGlyphRangesDefault() };
        var languages = new[]
        {
            _config.SourceLanguage,
            _config.TargetLanguage,
        };

        if (languages.Any(IsJapanese)) ranges.Add((nint)ImGui.GetIO().Fonts.GetGlyphRangesJapanese());
        if (languages.Any(IsKorean)) ranges.Add((nint)ImGui.GetIO().Fonts.GetGlyphRangesKorean());
        if (languages.Any(IsTraditionalChinese)) ranges.Add((nint)ImGui.GetIO().Fonts.GetGlyphRangesChineseFull());
        else if (languages.Any(IsSimplifiedChinese)) ranges.Add((nint)ImGui.GetIO().Fonts.GetGlyphRangesChineseSimplifiedCommon());
        if (languages.Any(IsCyrillic)) ranges.Add((nint)ImGui.GetIO().Fonts.GetGlyphRangesCyrillic());
        if (languages.Any(IsThai)) ranges.Add((nint)ImGui.GetIO().Fonts.GetGlyphRangesThai());
        if (languages.Any(IsVietnamese)) ranges.Add((nint)ImGui.GetIO().Fonts.GetGlyphRangesVietnamese());

        var glyphBuilder = new ImFontGlyphRangesBuilderPtr(ImGuiNative.ImFontGlyphRangesBuilder());
        foreach (var range in ranges)
            glyphBuilder.AddRanges((ushort*)range);
        if (languages.Any(IsGreek))
            glyphBuilder.AddText("ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩαβγδεζηθικλμνξοπρστυφχψω");
        return glyphBuilder.BuildRangesToArray();
    }

    private static bool IsJapanese(string? language) => language?.Equals("ja", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsKorean(string? language) => language?.Equals("ko", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsTraditionalChinese(string? language) => language?.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsSimplifiedChinese(string? language) => language?.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsCyrillic(string? language) => language is "ru" or "uk" or "bg" or "mk" or "sr";
    private static bool IsGreek(string? language) => language?.Equals("el", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsThai(string? language) => language?.Equals("th", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsVietnamese(string? language) => language?.Equals("vi", StringComparison.OrdinalIgnoreCase) == true;

    public void Dispose() => _font?.Dispose();
}
