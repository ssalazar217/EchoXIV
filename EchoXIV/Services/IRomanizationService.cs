namespace EchoXIV.Services;

public interface IRomanizationService : IDisposable
{
    string Name { get; }
    Task<string?> RomanizeAsync(string text, string sourceLang, CancellationToken ct = default);
    bool CanRomanize(string sourceLang, string text);
}

public interface ICombinedTranslator : IRomanizationService
{
    Task<(string Translation, string? RomanizedText, string OriginalScript)> TranslateWithRomanizationAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default, bool romanizeTranslation = false);
}
