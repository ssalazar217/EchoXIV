namespace EchoXIV.Services;

public interface IRomanizationService : IDisposable
{
    string Name { get; }
    Task<string?> RomanizeAsync(string text, string sourceLang, CancellationToken ct = default);
    bool CanRomanize(string sourceLang, string text);
}